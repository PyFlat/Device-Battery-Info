namespace DeviceBatteryInfo.Core;

// A source that cannot answer must return Unavailable, not a zero percentage.
public sealed record BatteryReading
{
    public int? Percent { get; init; }

    public BatteryStatus Status { get; init; } = BatteryStatus.Unknown;

    // Rarely available outside the host system's own battery.
    public TimeSpan? TimeToFull { get; init; }

    public TimeSpan? TimeToEmpty { get; init; }

    // Status is the source's guess, so ChargingInference may replace it.
    public bool StatusIsAssumed { get; init; }

    public bool IsCharging => Status is BatteryStatus.Charging;

    public bool HasValue => Percent is not null || Status is not BatteryStatus.Unknown;

    public static readonly BatteryReading Unavailable = new();

    public static BatteryReading FromPercent(
        int percent,
        BatteryStatus status = BatteryStatus.Unknown
    ) => new() { Percent = Math.Clamp(percent, 0, 100), Status = status };
}
