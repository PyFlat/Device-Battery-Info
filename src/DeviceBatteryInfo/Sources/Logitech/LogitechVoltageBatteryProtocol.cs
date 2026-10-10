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

    // The Linux kernel's 0x1001 table (hidpp20_map_battery_capacity): entry i is 100 - i percent.
    private static readonly int[] KernelMillivolts =
    [
        4186, 4156, 4143, 4133, 4122, 4113, 4103, 4094, 4086, 4075,
        4067, 4059, 4051, 4043, 4035, 4027, 4019, 4011, 4003, 3997,
        3989, 3983, 3976, 3969, 3961, 3955, 3949, 3942, 3935, 3929,
        3922, 3916, 3909, 3902, 3896, 3890, 3883, 3877, 3870, 3865,
        3859, 3853, 3848, 3842, 3837, 3833, 3828, 3824, 3819, 3815,
        3811, 3808, 3804, 3800, 3797, 3793, 3790, 3787, 3784, 3781,
        3778, 3775, 3772, 3770, 3767, 3764, 3762, 3759, 3757, 3754,
        3751, 3748, 3744, 3741, 3737, 3734, 3730, 3726, 3724, 3720,
        3717, 3714, 3710, 3706, 3702, 3697, 3693, 3688, 3683, 3677,
        3671, 3666, 3662, 3658, 3654, 3646, 3633, 3612, 3579, 3537,
    ];

    private static readonly (int Millivolts, int Percent)[] Curve =
    [
        .. KernelMillivolts.Select((millivolts, i) => (millivolts, 100 - i)),
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
