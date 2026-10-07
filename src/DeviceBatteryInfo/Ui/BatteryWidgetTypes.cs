using System.Text.Json;
using DeviceBatteryInfo.Actions;
using MacroDeck.Sdk.Widgets;

namespace DeviceBatteryInfo.Ui;

internal static class BatteryWidgetTypes
{
    public const string PanelId = "panel";
    public const string TileId = "tile";

    public static bool Matches(string widgetTypeAttribute, string localId) =>
        string.Equals(widgetTypeAttribute, localId, StringComparison.Ordinal)
        || widgetTypeAttribute.EndsWith("::" + localId, StringComparison.Ordinal);

    public static IReadOnlyList<WidgetTypeDescriptor> All =>
        [
            new WidgetTypeDescriptor(
                PanelId,
                Strings.Widgets.Panel.Name(),
                Strings.Widgets.Panel.Description(),
                DefaultData: DefaultData,
                DataSchema: Schema,
                HasConfiguration: true
            )
            {
                SupportsFlows = true,
                DefaultShortPressAction = RefreshOnPress,
                AppearanceProperties = Appearance,
            },
            new WidgetTypeDescriptor(
                TileId,
                Strings.Widgets.Tile.Name(),
                Strings.Widgets.Tile.Description(),
                DefaultData: DefaultData,
                DataSchema: Schema,
                HasConfiguration: true
            )
            {
                SupportsFlows = true,
                DefaultShortPressAction = RefreshOnPress,
                AppearanceProperties = Appearance,
            },
        ];

    // Lets Macro Deck's Set Background Color and Set Label Color actions reach these widgets.
    private static readonly WidgetAppearanceProperty[] Appearance =
    [
        WidgetAppearanceProperty.BackgroundColor,
        WidgetAppearanceProperty.LabelColor,
    ];

    // Runs until the user gives the widget a Short Press action of their own; their action wins.
    private static readonly WidgetDefaultAction RefreshOnPress = new(RefreshBatteryAction.ActionId);

    public static JsonElement StoredFlows(JsonElement? data) =>
        data is { ValueKind: JsonValueKind.Object } stored
        && stored.TryGetProperty("flows", out var flows)
            ? flows
            : default;

    private const string DefaultData =
        """{"sourceIds":[],"showBar":true,"showPercent":true,"showCharging":true,"showTimeToFull":true,"showTrend":true,"lowThreshold":20,"sort":"manual","title":"","layout":"rings","showNames":false,"colors":"levels-charging","listAlign":"top","showRingTrend":false,"ringSize":100}""";

    private const string Schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "sourceIds": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Battery source ids to show, in display order. Empty means every device the plugin sees."
            },
            "showBar": { "type": "boolean", "default": true },
            "showPercent": { "type": "boolean", "default": true },
            "showCharging": { "type": "boolean", "default": true },
            "showTimeToFull": { "type": "boolean", "default": true },
            "showTrend": {
              "type": "boolean",
              "default": true,
              "description": "Show the recent charge/drain rate (for example -13%/1h) when there is no time-to-full to show instead."
            },
            "lowThreshold": { "type": "integer", "minimum": 1, "maximum": 99, "default": 20 },
            "ringSize": {
              "type": "integer",
              "minimum": 50,
              "maximum": 100,
              "default": 100,
              "description": "The ring's size in percent of the room it has. The list layout has no ring."
            },
            "sort": {
              "type": "string",
              "enum": ["manual", "lowest-first", "alphabetical", "charging-first"],
              "default": "manual",
              "description": "Row order. 'manual' keeps the order the devices are listed in above."
            },
            "layout": {
              "type": "string",
              "enum": ["rings", "list"],
              "default": "rings",
              "description": "How the panel draws its devices: a grid of level rings, or a list of rows with bars."
            },
            "colors": {
              "type": "string",
              "enum": ["levels-charging", "levels", "simple", "device", "gradient"],
              "default": "levels-charging",
              "description": "Ring and bar colours. Every scheme shows a level at or below lowThreshold in red."
            },
            "listAlign": {
              "type": "string",
              "enum": ["top", "center", "bottom"],
              "default": "top",
              "description": "Where the list layout places its rows when they do not fill the widget."
            },
            "flows": {
              "type": "array",
              "description": "The actions a press runs, edited in the widget's action list."
            },
            "border": {
              "type": "object",
              "properties": {
                "style": { "type": "string" },
                "color": { "type": "string" }
              },
              "description": "The border Macro Deck draws around the widget, written by the border fields and the Set Border action."
            },
            "backgroundColor": {
              "type": "string",
              "description": "The widget's background, #rrggbb or transparent. Absent means the deck's own tile face."
            },
            "labelColor": {
              "type": "string",
              "description": "The text colour, #rrggbb. Secondary and muted text use it fainter. Absent means the theme's text colours."
            },
            "showNames": {
              "type": "boolean",
              "default": false,
              "description": "Show each device's name under its ring. The list layout and the tile always show names."
            },
            "showRingTrend": {
              "type": "boolean",
              "default": false,
              "description": "Show the recent charge/drain rate (for example -13%/1h) under each ring, below the name when names are shown. The list layout uses showTrend instead."
            },
            "title": {
              "type": "string",
              "default": "",
              "description": "Optional heading shown above the rows. Empty means no heading."
            }
          }
        }
        """;
}
