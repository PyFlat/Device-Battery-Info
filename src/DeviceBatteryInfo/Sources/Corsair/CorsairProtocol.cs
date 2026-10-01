using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Corsair;

// The VOID headsets answer an output report (C9 64) with an input report: byte 2 is the level,
// byte 4 the state. Same exchange as HeadsetControl and the corsair-battery-tray app.
internal sealed class CorsairProtocol()
    : HidProtocol(
        "Corsair",
        vendorId: 0x1B1C,
        reportLength: 2,
        HidReportKind.InputOutput,
        usagePage: 0xFFC5,
        usage: 0x0001
    )
{
    private static readonly byte[] BatteryRequest = [0xC9, 0x64];

    private const byte BatteryReportId = 0x64;
    private const int LevelIndex = 2;
    private const int StateIndex = 4;

    // The high bit of the level byte flags the mic boom as raised.
    private const byte MicUpFlag = 0x80;

    private const byte Connected = 1;
    private const byte LowBattery = 2;

    // Reported on the cable with the level still at 96, so trust the state, not the level.
    private const byte FullyCharged = 4;
    private const byte Charging = 5;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("VOID PRO Wireless", 0x0A75) { Kind = BatterySourceKind.Headset }];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    ) =>
        ParseBattery(
            await channel.ExchangeAsync(BatteryRequest, IsBatteryReport, cancellationToken)
        );

    internal static bool IsBatteryReport(byte[] report) =>
        report.Length > StateIndex && report[0] == BatteryReportId;

    internal static BatteryReading ParseBattery(ReadOnlySpan<byte> report)
    {
        var state = report[StateIndex];

        // The dongle still answers with the headset off; that is no reading, not a 0%.
        if (state is not (Connected or LowBattery or FullyCharged or Charging))
        {
            throw new InvalidOperationException($"The headset reported state {state}: it is off.");
        }

        return BatteryReading.FromPercent(
            report[LevelIndex] & ~MicUpFlag,
            state switch
            {
                Charging => BatteryStatus.Charging,
                FullyCharged => BatteryStatus.Full,
                _ => BatteryStatus.Discharging,
            }
        );
    }
}
