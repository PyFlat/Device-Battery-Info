using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.Batteries;
using Serilog;

namespace DeviceBatteryInfo.HostBatteries;

// Mirrors the configured devices into Macro Deck's battery catalog. Every call counts toward the
// plugin's shared callback throttle, so it sends the whole set in one publish and only on change.
public sealed class HostBatteryPublisher(
    DeviceCatalog catalog,
    BatteryRegistry registry,
    ILogger logger
) : IDisposable
{
    private readonly DeviceCatalog _catalog = catalog;
    private readonly BatteryRegistry _registry = registry;
    private readonly ILogger _logger = logger.ForContext<HostBatteryPublisher>();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IBatteryProviderContext? _context;
    private IReadOnlyList<BatteryDevice> _published = [];
    private bool _failing;

    public async Task AttachAsync(
        IBatteryProviderContext context,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // The host withdraws everything on (re)initialization, so the next sync sends the full set.
            _context = context;
            _published = [];
        }
        finally
        {
            _gate.Release();
        }

        await SyncAsync(cancellationToken);
    }

    public async Task DetachAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _context = null;
            _published = [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_context is not { } context)
            {
                return;
            }

            var previous = _published.ToDictionary(d => d.Id, StringComparer.Ordinal);
            var devices = HostBatteryDevices.Build(_catalog.Devices, _registry, previous);

            // A deleted config entry is gone for good, so the host deletes its variables too.
            foreach (var id in previous.Keys.Except(devices.Select(d => d.Id)))
            {
                await context.RemoveAsync(id, cancellationToken);
            }

            if (devices.Count > 0 && !devices.SequenceEqual(_published))
            {
                await context.PublishAsync(devices, cancellationToken);
            }

            _published = devices;
            if (_failing)
            {
                _failing = false;
                _logger.Information(
                    "Publishing devices to the Macro Deck battery catalog recovered."
                );
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Left unchanged, _published makes the next poll retry the same publish.
            if (!_failing)
            {
                _failing = true;
                _logger.Warning(
                    exception,
                    "Could not publish devices to the Macro Deck battery catalog."
                );
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
