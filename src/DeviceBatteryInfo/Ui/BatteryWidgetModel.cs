using System.Globalization;
using DeviceBatteryInfo.Core;

namespace DeviceBatteryInfo.Ui;

internal sealed record BatteryWidgetModel(
    IReadOnlyList<BatteryWidgetRow> Rows,
    BatteryWidgetOptions Options
);

internal sealed record BatteryWidgetRow(
    string Id,
    string Name,
    int? Percent,
    BatteryStatus Status,
    bool Charging,
    bool Stale,
    string? TimeToFull,
    string? Trend = null,
    BatterySourceKind Kind = BatterySourceKind.Other
)
{
    // Apple's system colours.
    public const string Green = "#34C759";
    public const string Yellow = "#FFCC00";
    public const string Orange = "#FF9500";
    public const string Red = "#FF3B30";
    public const string Cyan = "#32ADE6";
    public const string Grey = "#8E8E93";

    private static readonly Dictionary<BatterySourceKind, string> KindColors = new()
    {
        [BatterySourceKind.System] = "#5856D6",
        [BatterySourceKind.Phone] = "#007AFF",
        [BatterySourceKind.Tablet] = Cyan,
        [BatterySourceKind.Mouse] = "#AF52DE",
        [BatterySourceKind.Keyboard] = Orange,
        [BatterySourceKind.Headset] = "#FF2D55",
        [BatterySourceKind.Earbuds] = "#00C7BE",
        [BatterySourceKind.Controller] = Yellow,
        [BatterySourceKind.Pen] = "#A2845E",
        [BatterySourceKind.Speaker] = "#30B0C7",
        [BatterySourceKind.VrHeadset] = "#FF2D55",
        [BatterySourceKind.Other] = Green,
    };

    public const string White = "#FFFFFF";

    public string Color(BatteryWidgetOptions options) =>
        Color(options.LowThreshold, options.Colors, options.RingColor, options.LowInRed);

    // Shapes take a hex only, so a custom colour that is not #rrggbb falls back to white.
    public string Color(
        int lowThreshold,
        BatteryColorScheme scheme = BatteryColorScheme.LevelsCharging,
        string? customColor = null,
        bool lowInRed = true
    )
    {
        if (Stale || Percent is not { } percent)
        {
            return Grey;
        }

        var custom = IsHexColor(customColor) ? customColor!.ToUpperInvariant() : White;
        if (percent <= lowThreshold && (lowInRed || scheme != BatteryColorScheme.Custom))
        {
            return Red;
        }

        return scheme switch
        {
            BatteryColorScheme.Custom => custom,
            BatteryColorScheme.Simple => Green,
            BatteryColorScheme.Levels => StepColor(percent),
            BatteryColorScheme.Device => KindColors.GetValueOrDefault(Kind, Green),
            BatteryColorScheme.Gradient => GradientColor(percent, lowThreshold),
            _ => Charging ? Cyan : StepColor(percent),
        };
    }

    public static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);

    private static string StepColor(int percent) =>
        percent switch
        {
            <= 40 => Orange,
            <= 60 => Yellow,
            _ => Green,
        };

    // Red at the threshold to green at full.
    private static string GradientColor(int percent, int lowThreshold)
    {
        var t = Math.Clamp(
            (percent - lowThreshold) / (double)Math.Max(1, 100 - lowThreshold),
            0,
            1
        );
        return Hsv(4 + (131 * t), 0.78, 0.86);
    }

    private static string Hsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - chroma;
        var (r, g, b) = (hue / 60) switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            _ => (0.0, chroma, x),
        };
        return $"#{Channel(r + m)}{Channel(g + m)}{Channel(b + m)}";
    }

    private static string Channel(double value) =>
        ((int)Math.Round(value * 255)).ToString("X2", CultureInfo.InvariantCulture);

    public double Level => Percent is { } p ? Math.Clamp(p, 0, 100) / 100.0 : 0;

    public string PercentText() => Percent is { } p ? $"{p}%" : "--";
}

internal enum BatteryColorScheme
{
    LevelsCharging,

    Levels,

    Simple,

    Device,

    Gradient,

    Custom,
}

internal enum BatteryWidgetLayout
{
    Rings,

    List,
}

internal enum BatteryListAlignment
{
    Top,

    Center,

    Bottom,
}

internal enum BatteryNamePosition
{
    Below,

    Inside,
}

internal enum BatterySortMode
{
    Manual,

    LowestFirst,

    Alphabetical,

    ChargingFirst,
}

internal sealed record BatteryWidgetOptions(
    IReadOnlyList<string> SourceIds,
    bool ShowBar,
    bool ShowPercent,
    bool ShowCharging,
    bool ShowTimeToFull,
    bool ShowTrend,
    int LowThreshold,
    BatterySortMode Sort = BatterySortMode.Manual,
    string Title = "",
    BatteryWidgetLayout Layout = BatteryWidgetLayout.Rings,
    bool ShowNames = false,
    BatteryColorScheme Colors = BatteryColorScheme.LevelsCharging,
    BatteryListAlignment ListAlign = BatteryListAlignment.Top,
    bool ShowRingTrend = false,
    string? BackgroundColor = null,
    string? TextColor = null,
    int RingSize = BatteryWidgetOptions.MaxRingSize,
    BatteryNamePosition NamePosition = BatteryNamePosition.Below,
    string RingColor = BatteryWidgetRow.White,
    bool LowInRed = true
)
{
    public const string NameBelow = "below";
    public const string NameInside = "inside";

    public static BatteryNamePosition ParseNamePosition(string? value) =>
        value == NameInside ? BatteryNamePosition.Inside : BatteryNamePosition.Below;

    public static string NamePositionValue(BatteryNamePosition position) =>
        position == BatteryNamePosition.Inside ? NameInside : NameBelow;

    public bool NameInRing => NamePosition == BatteryNamePosition.Inside;

    public const int MinRingSize = 50;
    public const int MaxRingSize = 100;

    public double RingScale => Math.Clamp(RingSize, MinRingSize, MaxRingSize) / 100.0;

    public static readonly BatteryWidgetOptions Default = new(
        [],
        ShowBar: true,
        ShowPercent: true,
        ShowCharging: true,
        ShowTimeToFull: true,
        ShowTrend: true,
        LowThreshold: 20
    );

    public const string SortManual = "manual";
    public const string SortLowestFirst = "lowest-first";
    public const string SortAlphabetical = "alphabetical";
    public const string SortChargingFirst = "charging-first";

    public const string LayoutRings = "rings";
    public const string LayoutList = "list";

    public static BatteryWidgetLayout ParseLayout(string? value) =>
        value == LayoutList ? BatteryWidgetLayout.List : BatteryWidgetLayout.Rings;

    public static string LayoutValue(BatteryWidgetLayout layout) =>
        layout == BatteryWidgetLayout.List ? LayoutList : LayoutRings;

    public const string ListAlignTop = "top";
    public const string ListAlignCenter = "center";
    public const string ListAlignBottom = "bottom";

    public static BatteryListAlignment ParseListAlign(string? value) =>
        value switch
        {
            ListAlignCenter => BatteryListAlignment.Center,
            ListAlignBottom => BatteryListAlignment.Bottom,
            _ => BatteryListAlignment.Top,
        };

    public static string ListAlignValue(BatteryListAlignment align) =>
        align switch
        {
            BatteryListAlignment.Center => ListAlignCenter,
            BatteryListAlignment.Bottom => ListAlignBottom,
            _ => ListAlignTop,
        };

    public const string ColorsLevelsCharging = "levels-charging";
    public const string ColorsLevels = "levels";
    public const string ColorsSimple = "simple";
    public const string ColorsDevice = "device";
    public const string ColorsGradient = "gradient";
    public const string ColorsCustom = "custom";

    public static BatteryColorScheme ParseColors(string? value) =>
        value switch
        {
            ColorsLevels => BatteryColorScheme.Levels,
            ColorsSimple => BatteryColorScheme.Simple,
            ColorsDevice => BatteryColorScheme.Device,
            ColorsGradient => BatteryColorScheme.Gradient,
            ColorsCustom => BatteryColorScheme.Custom,
            _ => BatteryColorScheme.LevelsCharging,
        };

    public static string ColorsValue(BatteryColorScheme scheme) =>
        scheme switch
        {
            BatteryColorScheme.Levels => ColorsLevels,
            BatteryColorScheme.Simple => ColorsSimple,
            BatteryColorScheme.Device => ColorsDevice,
            BatteryColorScheme.Gradient => ColorsGradient,
            BatteryColorScheme.Custom => ColorsCustom,
            _ => ColorsLevelsCharging,
        };

    public static BatterySortMode ParseSort(string? value) =>
        value switch
        {
            SortLowestFirst => BatterySortMode.LowestFirst,
            SortAlphabetical => BatterySortMode.Alphabetical,
            SortChargingFirst => BatterySortMode.ChargingFirst,
            _ => BatterySortMode.Manual,
        };

    public static string SortValue(BatterySortMode mode) =>
        mode switch
        {
            BatterySortMode.LowestFirst => SortLowestFirst,
            BatterySortMode.Alphabetical => SortAlphabetical,
            BatterySortMode.ChargingFirst => SortChargingFirst,
            _ => SortManual,
        };

    public IReadOnlyList<BatteryWidgetRow> Order(IReadOnlyList<BatteryWidgetRow> rows) =>
        Sort switch
        {
            BatterySortMode.LowestFirst => rows.OrderBy(r => r.Stale || r.Percent is null ? 1 : 0)
                .ThenBy(r => r.Stale || r.Percent is null ? int.MaxValue : r.Percent!.Value)
                .ToArray(),
            BatterySortMode.Alphabetical => rows.OrderBy(
                    r => r.Name,
                    StringComparer.CurrentCultureIgnoreCase
                )
                .ToArray(),
            BatterySortMode.ChargingFirst => rows.OrderBy(r => r.Charging ? 0 : 1).ToArray(),
            _ => rows,
        };
}
