using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace DeviceBatteryInfo.Ui;

internal static class BatteryWidgetPreviews
{
    private const int CornerRadius = 16;

    [UiPreview("Panel", View = nameof(BatteryWidgetView), Profile = UiPreviewProfiles.Widget)]
    public static UiElement Panel() => PanelOf(BatteryWidgetSamples.Panel());

    [UiPreview(
        "Panel - names and heading",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement PanelNamed() => PanelOf(BatteryWidgetSamples.PanelNamed());

    [UiPreview(
        "Panel - names and trend",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement PanelTrend() => PanelOf(BatteryWidgetSamples.PanelTrend());

    [UiPreview("Panel - list", View = nameof(BatteryWidgetView), Profile = UiPreviewProfiles.Widget)]
    public static UiElement PanelList() => PanelOf(BatteryWidgetSamples.PanelList());

    [UiPreview(
        "Panel - list with device colours",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement PanelListDevice() => PanelOf(BatteryWidgetSamples.PanelListDevice());

    [UiPreview(
        "Panel - gradient colours",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement PanelGradient() => PanelOf(BatteryWidgetSamples.PanelGradient());

    [UiPreview(
        "Panel - low battery",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement PanelLow() => PanelOf(BatteryWidgetSamples.PanelLow());

    [UiPreview(
        "Panel - nothing configured",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement PanelEmpty() => PanelOf(BatteryWidgetSamples.PanelEmpty());

    [UiPreview(
        "Tile - charging",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement TileCharging() => TileOf(BatteryWidgetSamples.TileCharging());

    [UiPreview(
        "Tile - discharging",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement TileDischarging() => TileOf(BatteryWidgetSamples.TileDischarging());

    [UiPreview(
        "Tile - low battery",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement TileLow() => TileOf(BatteryWidgetSamples.TileLow());

    [UiPreview(
        "Tile - full",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement TileFull() => TileOf(BatteryWidgetSamples.TileFull());

    [UiPreview(
        "Tile - no signal",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement TileNoSignal() => TileOf(BatteryWidgetSamples.TileNoSignal());

    [UiPreview(
        "Tile - custom colours",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement TileCustomColors() => TileOf(BatteryWidgetSamples.TileCustomColors());

    [UiPreview(
        "Tile - transparent background",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement TileTransparent() => TileOf(BatteryWidgetSamples.TileTransparent());

    [UiPreview(
        "Panel - list with custom colours",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement PanelListCustomColors() =>
        PanelOf(BatteryWidgetSamples.PanelListCustomColors());

    [UiPreview(
        "Tile - smaller ring",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement TileSmallRing() => TileOf(BatteryWidgetSamples.TileSmallRing());

    [UiPreview(
        "Panel - smaller rings",
        View = nameof(BatteryWidgetView),
        Profile = UiPreviewProfiles.Widget
    )]
    public static UiElement PanelSmallRings() => PanelOf(BatteryWidgetSamples.PanelSmallRings());

    private static UiElement PanelOf(BatteryWidgetModel model) =>
        BatteryWidgetView.Build(
            BatteryWidgetTypes.PanelId,
            new UiState<BatteryWidgetModel>(model),
            CornerRadius
        );

    private static UiElement TileOf(BatteryWidgetModel model) =>
        BatteryWidgetView.Build(
            BatteryWidgetTypes.TileId,
            new UiState<BatteryWidgetModel>(model),
            CornerRadius
        );
}
