using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Rapoo;

// The mouse pushes a status report (BB B0 ...) on its own, so a read only listens: byte 5 is the
// state, byte 6 the level. Steady every ~3.2 s over the dongle, irregular on the cable.
internal sealed class RapooProtocol()
    : HidProtocol(
        "Rapoo",
        vendorId: 0x24AE,
        reportLength: 7,
        HidReportKind.InputOutput,
        usagePage: 0xFF00,
        usage: 0x0002
    )
{
    private const byte StatusReportId = 0xBB;
    private const int StateIndex = 5;
    private const int LevelIndex = 6;

    private const byte OnBattery = 1;
    private const byte Charging = 2;

    // Charging firmware sends this instead of a level.
    private const byte LevelUnknown = 0x7F;

    // Dongle, then cable. Only the FF00:0002 collection with 7-byte reports pushes; its 10-byte sibling
    // stays silent and loses the probe.
    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("VT3 PRO", 0x1215, 0x4415)];

    // Covers one dongle push interval. The cable's gaps can run far longer, so a read there may miss.
    public override TimeSpan? ReadBudget => TimeSpan.FromSeconds(4);

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    ) => ParseStatus(await channel.ExchangeAsync([], IsStatusReport, cancellationToken));

    internal static bool IsStatusReport(byte[] report) =>
        report.Length > LevelIndex && report[0] == StatusReportId;

    internal static BatteryReading ParseStatus(ReadOnlySpan<byte> report)
    {
        var state = report[StateIndex];
        var level = report[LevelIndex];

        return state switch
        {
            Charging when level == LevelUnknown => new BatteryReading { Status = BatteryStatus.Charging },
            // There is no full state: a full mouse on the cable keeps reporting charging at 100.
            Charging when level >= 100 => BatteryReading.FromPercent(level, BatteryStatus.Full),
            Charging => BatteryReading.FromPercent(level, BatteryStatus.Charging),
            OnBattery => BatteryReading.FromPercent(level, BatteryStatus.Discharging),
            _ => throw new InvalidOperationException($"The mouse reported unknown state {state}."),
        };
    }
}
