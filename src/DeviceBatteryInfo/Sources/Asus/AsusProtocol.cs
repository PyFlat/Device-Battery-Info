using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Asus;

// ROG mice answer an output report (12 07) on their FF01 interface, the exchange G-Helper uses.
// Byte 0 is the report id on both sides; the answer echoes the command.
internal sealed class AsusProtocol()
    : HidProtocol(
        "ASUS",
        vendorId: 0x0B05,
        reportLength: 65,
        HidReportKind.InputOutput,
        usagePage: 0xFF01
    )
{
    private static readonly byte[] BatteryRequest = [ReportId, 0x12, 0x07];

    private const byte ReportId = 0x00;
    private const int LevelIndex = 5;
    private const int ChargingIndex = 10;

    // The Strix Impact II Wireless reports its level in quarters (0-4).
    private const int LevelStep = 25;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("ROG Strix Impact II Wireless", 0x1949, 0x1947)];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    ) => ParseBattery(await channel.ExchangeAsync(BatteryRequest, IsAnswer, cancellationToken));

    // A dongle whose mouse is asleep or off answers FF AA instead.
    internal static bool IsAnswer(byte[] report) =>
        report.Length > ChargingIndex
        && report[0] == ReportId
        && (IsBatteryReport(report) || IsMouseUnreachable(report));

    private static bool IsBatteryReport(byte[] report) => report[1] == 0x12 && report[2] == 0x07;

    private static bool IsMouseUnreachable(byte[] report) => report[1] == 0xFF && report[2] == 0xAA;

    internal static BatteryReading ParseBattery(byte[] report)
    {
        if (IsMouseUnreachable(report))
        {
            throw new InvalidOperationException("The dongle cannot reach the mouse: it is asleep or off.");
        }

        var level = report[LevelIndex];
        var charging = report[ChargingIndex] > 0;
        if (level > 100 / LevelStep)
        {
            throw new InvalidOperationException($"The mouse reported level {level}.");
        }

        // G-Helper reads a 0 off the charger as a mouse that is not ready, not as an empty battery.
        if (level == 0 && !charging)
        {
            throw new InvalidOperationException("The mouse reported level 0: it is asleep or off.");
        }

        var percent = level * LevelStep;
        return BatteryReading.FromPercent(
            percent,
            charging ? BatteryStatus.Charging
            : percent >= 100 ? BatteryStatus.Full
            : BatteryStatus.Discharging
        );
    }
}
