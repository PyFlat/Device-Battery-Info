namespace DeviceBatteryInfo.Core;

// Below a 1h window the text shows the raw delta over whatever window exists. From 1h on it shows
// the normalized percent-per-hour rate instead of stretching to something like "-15%/3h".
internal static class BatteryTrendFormatter
{
    private static readonly TimeSpan NormalizeFrom = TimeSpan.FromHours(1);

    public static string? FormatText(BatteryTrend? trend)
    {
        if (trend is not { DeltaPercent: not 0 } t)
        {
            return null;
        }

        if (t.Window < NormalizeFrom)
        {
            return FormatSigned(t.DeltaPercent, FormatWindow(t.Window));
        }

        var perHour = (int)Math.Round(t.DeltaPercent / t.Window.TotalHours, MidpointRounding.AwayFromZero);
        return perHour == 0 ? null : FormatSigned(perHour, "1h");
    }

    public static double? PercentPerHour(BatteryTrend? trend) =>
        trend is { DeltaPercent: not 0 } t
            ? Math.Round(t.DeltaPercent / t.Window.TotalHours, 1)
            : null;

    private static string FormatSigned(int value, string window) =>
        $"{(value > 0 ? "+" : "-")}{Math.Abs(value)}%/{window}";

    private static string FormatWindow(TimeSpan window)
    {
        var minutes = window.TotalMinutes;
        var step = minutes switch
        {
            < 10 => 1,
            < 60 => 5,
            _ => 15,
        };
        var rounded = Math.Max(step, (int)Math.Round(minutes / step) * step);

        if (rounded < 60)
        {
            return $"{rounded}m";
        }

        var hours = rounded / 60;
        var remainderMinutes = rounded % 60;
        return remainderMinutes == 0 ? $"{hours}h" : $"{hours}h{remainderMinutes}m";
    }
}
