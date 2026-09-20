using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Logitech;

// Logitech's headsets speak HID++ too, but on their own interface, as the receiver itself, and they
// report a battery voltage instead of a percentage. Verified on a G Pro X Wireless (0ABA).
internal sealed class LogitechHeadsetProtocol()
    : LogitechHidppProtocol(0xFF43, 0x0202, DeviceIndex)
{
    // The headset answers as the receiver, not as a paired device slot.
    internal const byte DeviceIndex = 0xFF;

    private const byte Charging = 0x03;

    // Discharge curve from HeadsetControl's G Pro calibration, highest voltage first.
    private static readonly (int Millivolts, int Percent)[] Curve =
    [
        (4150, 100),
        (3830, 50),
        (3780, 30),
        (3740, 20),
        (3670, 5),
        (3320, 0),
    ];

    // ADC measurement: the one battery feature these headsets expose.
    protected override ushort BatteryFeature => 0x1F20;

    // getVoltage.
    protected override byte BatteryFunction => 0x00;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("G Pro X Wireless", 0x0ABA) { Kind = BatterySourceKind.Headset }];

    protected override BatteryReading ParseBattery(ReadOnlySpan<byte> report) =>
        ParseVoltage(report);

    internal static BatteryReading ParseVoltage(ReadOnlySpan<byte> report)
    {
        ThrowOnError(report, "reading the battery voltage");

        var millivolts = report[4] << 8 | report[5];

        // A powered-off headset reads far below the curve, which would show as a real 0%.
        if (millivolts < Curve[^1].Millivolts)
        {
            throw new InvalidOperationException($"The headset reported {millivolts} mV: it is off.");
        }

        var percent = PercentFromMillivolts(millivolts);
        return new BatteryReading
        {
            Percent = percent,
            Status =
                report[6] == Charging ? BatteryStatus.Charging
                : percent >= 100 ? BatteryStatus.Full
                : BatteryStatus.Discharging,
        };
    }

    // Linear between the calibration points, which is as much as a voltage reading is worth.
    internal static int PercentFromMillivolts(int millivolts)
    {
        if (millivolts >= Curve[0].Millivolts)
        {
            return 100;
        }

        for (var i = 1; i < Curve.Length; i++)
        {
            var (low, lowPercent) = Curve[i];
            if (millivolts < low)
            {
                continue;
            }

            var (high, highPercent) = Curve[i - 1];
            return lowPercent
                + (int)
                    Math.Round(
                        (millivolts - low) / (double)(high - low) * (highPercent - lowPercent),
                        MidpointRounding.AwayFromZero
                    );
        }

        return 0;
    }
}
