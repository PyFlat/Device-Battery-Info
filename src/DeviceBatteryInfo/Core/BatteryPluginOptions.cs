namespace DeviceBatteryInfo.Core;

public sealed class BatteryPluginOptions
{
    public const string SectionName = "Battery";

    // Floored at 10 by BatteryPollingService: a read can mean spawning PowerShell or an adb round trip.
    public int PollIntervalSeconds { get; set; } = 10;

    public int StaleAfterFailures { get; set; } = 3;

    public int ReadTimeoutSeconds { get; set; } = 10;
}
