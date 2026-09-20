using System.Text.Json;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Ui;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace DeviceBatteryInfo;

public sealed partial class BatteryIntegration : IWidgetTypeProvider, IUiProvider
{
    private readonly Dictionary<string, string> _widgetTypeIds = new(StringComparer.Ordinal);

    string IWidgetTypeProvider.ProviderName => "Device Battery Info";

    public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => BatteryWidgetTypes.All;

    async Task IWidgetTypeProvider.InitializeAsync(
        IWidgetTypeProviderContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var descriptor in BatteryWidgetTypes.All)
        {
            var registration = await context.RegisterWidgetTypeAsync(descriptor, cancellationToken);
            _widgetTypeIds[descriptor.Id] = registration.WidgetTypeId;
            _logger.Information(
                "Registered widget type {LocalId} as {QualifiedId}.",
                descriptor.Id,
                registration.WidgetTypeId
            );
        }
    }

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
        [
            new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
            new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
            new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
        ];

    public Task<IUiSession?> CreateSessionAsync(
        UiSessionRequest request,
        CancellationToken cancellationToken
    )
    {
        var surface = request.Surface;
        _logger.Information(
            "UI session requested: kind={Kind} attrs=[{Attributes}]",
            surface.Kind,
            string.Join(", ", surface.Attributes.Select(a => $"{a.Key}={Preview(a.Value)}"))
        );

        IUiSession? session = surface.Kind switch
        {
            UiSurfaceKinds.Widget or UiSurfaceKinds.Preview => CreateWidgetSession(surface),
            UiSurfaceKinds.Config => CreateConfigSession(surface),
            _ => null,
        };

        _logger.Information(
            "UI session {Outcome} for kind={Kind}.",
            session is null ? "declined" : "created",
            surface.Kind
        );
        return Task.FromResult(session);
    }

    private static string Preview(JsonElement value)
    {
        var text = value.ToString();
        return text.Length <= 120 ? text : text[..120] + "...";
    }

    private UiViewSession? CreateWidgetSession(UiSurface surface)
    {
        var widgetType = Attribute(surface, UiWidgetSurfaceAttributes.WidgetType) ?? string.Empty;
        var localId = LocalWidgetId(widgetType);
        if (localId is null)
        {
            return null;
        }

        var cornerRadius = CornerRadius(surface);

        var isSample =
            AttributeJson(surface, UiWidgetSurfaceAttributes.Sample)
                is { ValueKind: JsonValueKind.True };
        if (isSample)
        {
            var demoState = new UiState<BatteryWidgetModel>(BatteryWidgetSamples.For(localId));
            return new UiViewSession(
                new UiView(surface, BatteryWidgetView.Build(localId, demoState, cornerRadius))
            );
        }

        var options = ParseOptions(AttributeJson(surface, UiWidgetSurfaceAttributes.Data));
        BatteryWidgetModel Compute() => BuildModel(options);

        var initial = Compute();
        _logger.Information(
            "Opening {LocalId} widget with {RowCount} row(s), selected=[{Selected}].",
            localId,
            initial.Rows.Count,
            string.Join(", ", options.SourceIds)
        );

        var state = new UiState<BatteryWidgetModel>(initial);
        var view = new UiView(
            surface,
            BatteryWidgetView.Build(localId, state, cornerRadius, _polling.RequestRefresh)
        );

        void Refresh() => state.Set(Compute());
        void OnRegistryChanged(object? sender, BatterySnapshotChangedEventArgs e) => Refresh();
        void OnCatalogChanged(object? sender, EventArgs e) => Refresh();
        _registry.Changed += OnRegistryChanged;
        _catalog.Changed += OnCatalogChanged;

        _polling.RequestRefresh();

        return new UiViewSession(
            view,
            onDispose: () =>
            {
                _registry.Changed -= OnRegistryChanged;
                _catalog.Changed -= OnCatalogChanged;
            }
        );
    }

    private UiViewSession? CreateConfigSession(UiSurface surface)
    {
        if (
            Attribute(surface, UiConfigSurfaceAttributes.EntryPoint)
            != UiConfigEntryPoints.WidgetConfig
        )
        {
            return null;
        }

        var widgetType = Attribute(surface, UiConfigSurfaceAttributes.WidgetType) ?? string.Empty;
        if (LocalWidgetId(widgetType) is null)
        {
            return null;
        }

        var options = ParseOptions(AttributeJson(surface, UiConfigSurfaceAttributes.WidgetData));
        var view = new UiView(surface, BatteryWidgetConfigView.Build(options, CurrentSlots()));
        return new UiViewSession(view);
    }

    private BatteryWidgetModel BuildModel(BatteryWidgetOptions options)
    {
        var rows = ResolveDevices(options.SourceIds)
            .Select(slot =>
            {
                if (_registry.TryGet(slot.Id, out var snapshot))
                {
                    var reading = snapshot.Reading;
                    return new BatteryWidgetRow(
                        slot.Id,
                        slot.DisplayName,
                        reading.Percent,
                        reading.Status,
                        reading.IsCharging,
                        snapshot.IsStale,
                        FormatTimeToFull(reading.TimeToFull),
                        BatteryTrendFormatter.FormatText(_trend.GetTrend(snapshot))
                    );
                }

                return new BatteryWidgetRow(
                    slot.Id,
                    slot.DisplayName,
                    null,
                    BatteryStatus.Unknown,
                    false,
                    true,
                    null
                );
            })
            .ToArray();

        return new BatteryWidgetModel(options.Order(rows), options);
    }

    private IReadOnlyList<BatterySlot> ResolveDevices(IReadOnlyList<string> sourceIds)
    {
        var all = CurrentSlots();
        if (sourceIds.Count == 0)
        {
            return all;
        }

        var byId = all.ToDictionary(slot => slot.Id, StringComparer.Ordinal);
        return sourceIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
    }

    private string? LocalWidgetId(string widgetTypeAttribute)
    {
        foreach (var (localId, qualifiedId) in _widgetTypeIds)
        {
            if (string.Equals(widgetTypeAttribute, qualifiedId, StringComparison.Ordinal))
            {
                return localId;
            }
        }

        if (BatteryWidgetTypes.Matches(widgetTypeAttribute, BatteryWidgetTypes.PanelId))
        {
            return BatteryWidgetTypes.PanelId;
        }

        return BatteryWidgetTypes.Matches(widgetTypeAttribute, BatteryWidgetTypes.TileId)
            ? BatteryWidgetTypes.TileId
            : null;
    }

    private static BatteryWidgetOptions ParseOptions(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } obj)
        {
            return BatteryWidgetOptions.Default;
        }

        var sourceIds =
            obj.TryGetProperty("sourceIds", out var ids) && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToArray()
                : [];

        bool Flag(string key, bool fallback) =>
            obj.TryGetProperty(key, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : fallback;

        var low =
            obj.TryGetProperty("lowThreshold", out var threshold)
            && threshold.ValueKind == JsonValueKind.Number
            && threshold.TryGetInt32(out var parsed)
                ? Math.Clamp(parsed, 1, 99)
                : BatteryWidgetOptions.Default.LowThreshold;

        var sort =
            obj.TryGetProperty("sort", out var sortValue)
            && sortValue.ValueKind == JsonValueKind.String
                ? BatteryWidgetOptions.ParseSort(sortValue.GetString())
                : BatterySortMode.Manual;

        var title =
            obj.TryGetProperty("title", out var titleValue)
            && titleValue.ValueKind == JsonValueKind.String
                ? titleValue.GetString() ?? string.Empty
                : string.Empty;

        return new BatteryWidgetOptions(
            sourceIds,
            Flag("showBar", true),
            Flag("showPercent", true),
            Flag("showCharging", true),
            Flag("showTimeToFull", true),
            Flag("showTrend", true),
            low,
            sort,
            title
        );
    }

    private static string? Attribute(UiSurface surface, string key) =>
        surface.Attributes.TryGetValue(key, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement? AttributeJson(UiSurface surface, string key) =>
        surface.Attributes.TryGetValue(key, out var value) ? value : null;

    private static int CornerRadius(UiSurface surface) =>
        surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.CornerRadius, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var radius)
            ? radius
            : 16;

    private static string? FormatTimeToFull(TimeSpan? value) =>
        value is { } span && span > TimeSpan.Zero
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}"
            : null;
}
