using DeviceBatteryInfo.Actions;
using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Variables;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace DeviceBatteryInfo;

public sealed partial class BatteryIntegration(
    BatteryPollingService polling,
    BatteryRegistry registry,
    BatteryTrendTracker trend,
    DeviceCatalog catalog,
    DeviceModelCatalog models,
    IDeviceDiscovery discovery,
    ILogger logger
) : IPluginIntegration, IVariableProvider, IEventProvider, IConfigFlowProvider
{
    internal const string ChargingStartedEventId = "charging-started";
    internal const string ChargingStoppedEventId = "charging-stopped";
    internal const string BatteryLowEventId = "battery-low";

    private const int LowThresholdPercent = 20;

    private readonly BatteryPollingService _polling = polling;
    private readonly BatteryRegistry _registry = registry;
    private readonly BatteryTrendTracker _trend = trend;
    private readonly DeviceCatalog _catalog = catalog;
    private readonly DeviceModelCatalog _models = models;
    private readonly IDeviceDiscovery _discovery = discovery;
    private readonly ILogger _logger = logger.ForContext<BatteryIntegration>();

    private IIntegrationContext? _context;
    private IVariableSink? _sink;

    public IReadOnlyList<IActionDefinition> Actions { get; } = [new RefreshBatteryAction(polling)];

    // Each config entry adds one device, so the flow must be runnable more than once.
    public bool AllowsMultipleConfigurations => true;

    public IConfigFlow CreateConfigFlow() =>
        new DeviceConfigFlow(_discovery, _models, _catalog.Devices, _logger);

    public async Task InitializeAsync(IIntegrationContext context)
    {
        _context = context;

        _registry.Changed -= OnSnapshotChanged;
        _registry.Changed += OnSnapshotChanged;
        _catalog.Changed -= OnDeviceCatalogChanged;
        _catalog.Changed += OnDeviceCatalogChanged;

        try
        {
            var configured = await DeviceEntryReader.ReadAsync(
                context.Config,
                _models,
                CancellationToken.None
            );
            _catalog.Set(configured);
        }
        // The host calls this repeatedly after config changes; a failed read must not tear it down.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warning(
                exception,
                "Could not read the device config entries; keeping the current device set."
            );
        }

        _logger.Information(
            "Initialized with {DeviceCount} configured device(s).",
            _catalog.Devices.Count
        );
    }

    public Task ShutdownAsync()
    {
        _registry.Changed -= OnSnapshotChanged;
        _catalog.Changed -= OnDeviceCatalogChanged;
        _context = null;
        _sink = null;
        return Task.CompletedTask;
    }

    // Empty on purpose: every variable is served through the catalog below (SupportsCatalog), not this list.
    public IReadOnlyList<VariableDefinition> Variables => Array.Empty<VariableDefinition>();

    private IReadOnlyList<VariableDefinition> CatalogDefinitions() =>
        BatteryVariableCatalog.Build(CurrentSlots());

    // Configured devices plus any the registry has seen, so a contributed provider needs no config flow.
    private List<BatterySlot> CurrentSlots()
    {
        var slots = _catalog.Devices.ToList();
        var known = slots.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var snapshot in _registry.Snapshots)
        {
            if (known.Add(snapshot.Id))
            {
                slots.Add(
                    new BatterySlot(
                        snapshot.Id,
                        snapshot.DisplayName,
                        snapshot.Kind,
                        DeviceType.System
                    )
                );
            }
        }

        return slots;
    }

    private void OnDeviceCatalogChanged(object? sender, EventArgs e)
    {
        // Just re-poll. Never call IPluginCatalogNotifier.CatalogChanged from here: it re-enters
        // InitializeAsync, which would call it again: an infinite re-registration loop.
        _polling.RequestRefresh();
    }

    public bool SupportsCatalog => true;

    public bool SupportsPush => true;

    public string CatalogName => "Battery";

    public int? CatalogEntryCount => CurrentSlots().Count * BatteryVariableCatalog.FieldsPerDevice;

    public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken = default)
    {
        _sink = sink;
        return Task.CompletedTask;
    }

    public ValueTask<VariableCatalogPage> DiscoverAsync(
        VariableCatalogQuery query,
        CancellationToken cancellationToken = default
    )
    {
        IEnumerable<VariableDefinition> defs = CatalogDefinitions();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            defs = defs.Where(d =>
                (
                    d.Name is { } name
                    && name.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                )
                || (d.Id is { } id && id.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            );
        }

        var ordered = defs.ToArray();
        var start = int.TryParse(query.ContinuationToken, out var parsed)
            ? Math.Clamp(parsed, 0, ordered.Length)
            : 0;
        var size = query.PageSize > 0 ? query.PageSize : 100;
        var items = ordered.Skip(start).Take(size).ToArray();
        var consumed = start + items.Length;

        return ValueTask.FromResult(
            new VariableCatalogPage
            {
                Items = items,
                ContinuationToken =
                    consumed < ordered.Length
                        ? consumed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : null,
            }
        );
    }

    public ValueTask<VariableDefinition?> ResolveAsync(
        string localId,
        CancellationToken cancellationToken = default
    ) =>
        ValueTask.FromResult(
            CatalogDefinitions()
                .FirstOrDefault(d => string.Equals(d.ResolvedId, localId, StringComparison.Ordinal))
        );

    public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
        IReadOnlyCollection<string> localIds,
        CancellationToken cancellationToken = default
    )
    {
        var values = new List<VariableValue>(localIds.Count);
        foreach (var localId in localIds)
        {
            if (!BatteryVariableCatalog.TryResolve(localId, out var slotId, out var field))
            {
                values.Add(VariableValue.Unavailable(localId));
                continue;
            }

            var snapshot = _registry.TryGet(slotId, out var found) ? found : null;
            values.Add(VariableValue.Of(localId, ReadField(field, snapshot)));
        }

        return ValueTask.FromResult<IReadOnlyList<VariableValue>>(values);
    }

    public ValueTask<VariableReading> ReadAsync(
        string localId,
        CancellationToken cancellationToken = default
    )
    {
        if (!BatteryVariableCatalog.TryResolve(localId, out var slotId, out var field))
        {
            return ValueTask.FromResult(VariableReading.Unavailable);
        }

        var snapshot = _registry.TryGet(slotId, out var found) ? found : null;
        return ValueTask.FromResult(ReadField(field, snapshot));
    }

    public IReadOnlyList<EventDefinition> EventDefinitions =>
        [
            ChargeEvent(
                ChargingStartedEventId,
                Strings.Events.ChargingStarted.Name(),
                Strings.Events.ChargingStarted.Description()
            ),
            ChargeEvent(
                ChargingStoppedEventId,
                Strings.Events.ChargingStopped.Name(),
                Strings.Events.ChargingStopped.Description()
            ),
            ChargeEvent(
                BatteryLowEventId,
                Strings.Events.BatteryLow.Name(),
                Strings.Events.BatteryLow.Description()
            ),
        ];

    private static EventDefinition ChargeEvent(
        string id,
        LocalizedText name,
        LocalizedText description
    ) =>
        new()
        {
            Id = id,
            Name = name,
            Description = description,
            PayloadParameters =
            [
                ActionParameter.Text("device", Strings.Events.Field.Device.Label()),
                ActionParameter.Number("percent", Strings.Events.Field.Percent.Label()),
            ],
        };

    private VariableReading ReadField(BatteryVariableCatalog.Field field, BatterySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return field == BatteryVariableCatalog.Field.Online
                ? VariableReading.Of(false)
                : VariableReading.Unavailable;
        }

        var reading = snapshot.Reading;
        return field switch
        {
            BatteryVariableCatalog.Field.Percent => reading.Percent is { } percent
                ? VariableReading.Of(percent, 0, 100, 1)
                : VariableReading.Unavailable,
            BatteryVariableCatalog.Field.Status => VariableReading.Of(StatusText(reading.Status)),
            BatteryVariableCatalog.Field.Charging => VariableReading.Of(reading.IsCharging),
            BatteryVariableCatalog.Field.Online => VariableReading.Of(!snapshot.IsStale),
            BatteryVariableCatalog.Field.TimeToFull => VariableReading.Of(
                FormatDuration(reading.TimeToFull)
            ),
            BatteryVariableCatalog.Field.Trend => VariableReading.Of(
                BatteryTrendFormatter.FormatText(_trend.GetTrend(snapshot)) ?? string.Empty
            ),
            BatteryVariableCatalog.Field.TrendRate => BatteryTrendFormatter.PercentPerHour(
                _trend.GetTrend(snapshot)
            )
                is { } rate
                ? VariableReading.Of(rate)
                : VariableReading.Unavailable,
            _ => VariableReading.Unavailable,
        };
    }

    private void OnSnapshotChanged(object? sender, BatterySnapshotChangedEventArgs e)
    {
        if (e.Current is not { } current)
        {
            return;
        }

        PublishEvents(e.Previous?.Reading, current);
        PushVariables(current);
    }

    private void PublishEvents(BatteryReading? previous, BatterySnapshot current)
    {
        if (_context is null)
        {
            return;
        }

        var now = current.Reading;
        if (now.IsCharging && previous?.IsCharging != true)
        {
            Publish(ChargingStartedEventId, current);
        }
        else if (!now.IsCharging && previous?.IsCharging == true)
        {
            Publish(ChargingStoppedEventId, current);
        }

        if (
            now.Percent is { } percent and <= LowThresholdPercent
            && !now.IsCharging
            && previous?.Percent is not (<= LowThresholdPercent)
        )
        {
            Publish(BatteryLowEventId, current);
        }
    }

    private void PushVariables(BatterySnapshot snapshot)
    {
        var sink = _sink;
        if (sink is null)
        {
            return;
        }

        var values = BatteryVariableCatalog
            .FieldsFor(snapshot.Id)
            .Select(entry => VariableValue.Of(entry.LocalId, ReadField(entry.Field, snapshot)))
            .ToArray();

        _ = PublishAsync(sink, values);
    }

    private async Task PublishAsync(IVariableSink sink, IReadOnlyList<VariableValue> values)
    {
        try
        {
            await sink.PublishAsync(values);
        }
        catch (Exception exception)
        {
            _logger.Debug(exception, "Pushing variable updates failed.");
        }
    }

    private void Publish(string eventId, BatterySnapshot snapshot) =>
        _context?.Events.Publish(
            eventId,
            new Dictionary<string, object?>
            {
                ["device"] = snapshot.DisplayName,
                ["percent"] = snapshot.Reading.Percent,
            }
        );

    private static string StatusText(BatteryStatus status) =>
        status switch
        {
            BatteryStatus.Discharging => "discharging",
            BatteryStatus.Charging => "charging",
            BatteryStatus.Full => "full",
            _ => "unknown",
        };

    private static string FormatDuration(TimeSpan? value) =>
        value is { } span && span > TimeSpan.Zero
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}"
            : string.Empty;
}
