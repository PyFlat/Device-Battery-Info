using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Mchose;

// Report 55 65 01 asks the dongle for the battery; it answers 55 65 with the level in byte 2 (10% steps)
// and the state in byte 3. It also pushes the same frame on changes, and 55 11 when the headset connects.
internal sealed class MchoseProtocol()
    : HidProtocol(
        "MCHOSE",
        vendorId: 0x291D,
        reportLength: 64,
        HidReportKind.InputOutput,
        usagePage: 0xFF90,
        usage: 0x0001
    )
{
    private const byte ReportId = 0x55;
    private const byte BatteryCommand = 0x65;
    private const byte Query = 0x01;
    private const int LevelIndex = 2;
    private const int StateIndex = 3;

    private const byte OnBattery = 0x02;

    // The level freezes at its value from when the cable went in, so it says nothing about the charge.
    // A query answers OnCable both while charging and when full; the pushed frames say Charging.
    private const byte OnCable = 0x00;
    private const byte Charging = 0x03;

    private const byte HeadsetOff = 0x1A;

    internal static readonly byte[] BatteryRequest = [ReportId, BatteryCommand, Query];

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("V9 Pro", 0x385D) { Kind = BatterySourceKind.Headset }];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    ) => ParseBattery(await channel.ExchangeAsync(BatteryRequest, IsBatteryReport, cancellationToken));

    internal static bool IsBatteryReport(byte[] report) =>
        report.Length > StateIndex && report[0] == ReportId && report[1] == BatteryCommand;

    internal static BatteryReading ParseBattery(ReadOnlySpan<byte> report)
    {
        var level = report[LevelIndex];
        var state = report[StateIndex];

        return state switch
        {
            OnCable or Charging => new BatteryReading { Status = BatteryStatus.Charging },
            OnBattery when level <= 100 => BatteryReading.FromPercent(level, BatteryStatus.Discharging),
            HeadsetOff => throw new InvalidOperationException("The headset is off."),
            _ => throw new InvalidOperationException($"The headset reported level {level}, state {state}."),
        };
    }
}
