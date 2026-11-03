using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.SteelSeries;

// The Rival 3 Wireless answers an output report (AA 01) with an input report whose byte 1 is the level.
// Same exchange as rivalcfg; byte 0 is the report id on both sides. It runs on AAA cells, so it never charges.
internal sealed class SteelSeriesProtocol()
    : HidProtocol(
        "SteelSeries",
        vendorId: 0x1038,
        reportLength: 65,
        HidReportKind.InputOutput,
        usagePage: 0xFFC0,
        usage: 0x0001
    )
{
    private static readonly byte[] BatteryRequest = [0x00, 0xAA, 0x01];

    private const byte ReportId = 0x00;
    private const int LevelIndex = 1;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } = [new("Rival 3 Wireless", 0x1830)];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    ) => ParseBattery(await channel.ExchangeAsync(BatteryRequest, IsBatteryReport, cancellationToken));

    internal static bool IsBatteryReport(byte[] report) =>
        report.Length > LevelIndex && report[0] == ReportId;

    internal static BatteryReading ParseBattery(ReadOnlySpan<byte> report)
    {
        var level = report[LevelIndex];
        if (level > 100)
        {
            throw new InvalidOperationException($"The mouse reported level {level}.");
        }

        return BatteryReading.FromPercent(level, BatteryStatus.Discharging);
    }
}
