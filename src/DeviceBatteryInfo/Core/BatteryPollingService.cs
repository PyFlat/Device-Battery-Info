using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace DeviceBatteryInfo.Core;

public sealed class BatteryPollingService(
    IEnumerable<IBatterySourceProvider> providers,
    BatteryRegistry registry,
    ChargingInference charging,
    IOptionsMonitor<BatteryPluginOptions> options,
    ILogger logger
) : BackgroundService
{
    private readonly IReadOnlyList<IBatterySourceProvider> _providers = providers.ToArray();
    private readonly BatteryRegistry _registry = registry;
    private readonly ChargingInference _charging = charging;
    private readonly IOptionsMonitor<BatteryPluginOptions> _options = options;
    private readonly ILogger _logger = logger.ForContext<BatteryPollingService>();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Dictionary<string, bool> _lastFailed = new(StringComparer.Ordinal);

    public void RequestRefresh()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A refresh is already pending.
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Battery poll cycle failed.");
            }

            var interval = TimeSpan.FromSeconds(
                Math.Max(10, _options.CurrentValue.PollIntervalSeconds)
            );
            try
            {
                await _wake.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var sources = await DiscoverAsync(cancellationToken);
        _registry.Retain(sources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal));

        var readTimeout = TimeSpan.FromSeconds(Math.Max(2, options.ReadTimeoutSeconds));
        await Parallel.ForEachAsync(
            sources,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 6,
            },
            async (source, ct) =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(readTimeout);
                try
                {
                    var reading = await source.ReadAsync(timeout.Token);
                    _registry.Update(source, _charging.Apply(source.Id, reading));
                    if (_lastFailed.TryGetValue(source.Id, out var wasFailing) && wasFailing)
                    {
                        _logger.Information("Source {SourceId} recovered.", source.Id);
                    }

                    _lastFailed[source.Id] = false;
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException || !ct.IsCancellationRequested
                    )
                {
                    _registry.RecordFailure(source, Math.Max(1, options.StaleAfterFailures));
                    if (!_lastFailed.TryGetValue(source.Id, out var wasFailing) || !wasFailing)
                    {
                        _logger.Warning(
                            exception,
                            "Source {SourceId} could not be read.",
                            source.Id
                        );
                    }

                    _lastFailed[source.Id] = true;
                }
            }
        );
    }

    private async Task<IReadOnlyList<IBatterySource>> DiscoverAsync(
        CancellationToken cancellationToken
    )
    {
        var sources = new List<IBatterySource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in _providers)
        {
            try
            {
                foreach (var source in await provider.DiscoverAsync(cancellationToken))
                {
                    if (seen.Add(source.Id))
                    {
                        sources.Add(source);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Warning(
                    exception,
                    "Source provider {Provider} failed to enumerate.",
                    provider.GetType().Name
                );
            }
        }

        return sources;
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }
}
