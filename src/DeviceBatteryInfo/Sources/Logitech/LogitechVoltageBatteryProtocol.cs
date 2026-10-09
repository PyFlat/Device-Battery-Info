using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Logitech;

// Mice that report a battery voltage (0x1001) instead of a percentage.
// Read on a G502 Lightspeed, on the cable (C08D) and behind its receiver (C539).
internal sealed class LogitechVoltageBatteryProtocol() : LogitechHidppProtocol(0xFF00, 0x0002, DeviceIndex)
{
    internal const byte DeviceIndex = 0x01;

    private const byte ExternalPower = 0x80;
    private const byte ChargeStateMask = 0x07;

    // The Linux kernel's 0x1001 curve at the points Solaar samples, highest voltage first.
    private static readonly (int Millivolts, int Percent)[] Curve =
    [
        (4186, 100),
        (4067, 90),
        (3989, 80),
        (3922, 70),
        (3859, 60),
        (3811, 50),
        (3778, 40),
        (3751, 30),
        (3717, 20),
        (3671, 10),
        (3646, 5),
        (3579, 2),
        (3500, 0),
    ];

    protected override ushort BatteryFeature => 0x1001;

    // getBatteryVoltage.
    protected override byte BatteryFunction => 0x00;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("G502 Lightspeed", 0xC539, 0xC08D)];

    protected override BatteryReading ParseBattery(ReadOnlySpan<byte> report) => ParseVoltage(report);

    internal static BatteryReading ParseVoltage(ReadOnlySpan<byte> report)
    {
        ThrowOnError(report, "reading the battery voltage");

        var millivolts = report[4] << 8 | report[5];
        if (millivolts == 0)
        {
            throw new InvalidOperationException("The mouse reported 0 mV: it is off or asleep.");
        }

        var percent = PercentFromMillivolts(millivolts);
        var flags = report[6];

        // The charge state bits only mean something while external power is connected.
        var status = (flags & ExternalPower) == 0
            ? percent >= 100 ? BatteryStatus.Full : BatteryStatus.Discharging
            : (flags & ChargeStateMask) switch
            {
                0 => BatteryStatus.Charging,
                1 => BatteryStatus.Full,
                2 => BatteryStatus.Discharging,
                _ => BatteryStatus.Unknown,
            };

        return new BatteryReading { Percent = percent, Status = status };
    }

    internal static int PercentFromMillivolts(int millivolts) =>
        LogitechHidppProtocol.PercentFromMillivolts(millivolts, Curve);
}
