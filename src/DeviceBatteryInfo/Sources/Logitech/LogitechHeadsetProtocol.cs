using System.Collections.Concurrent;
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

    internal const int OffMillivolts = 3300;

    // Sampled from G Hub's power model for the 0ABA (pro_x_wireless_headset_battery.xml).
    private static readonly (int Millivolts, int Percent)[] Curve =
    [
        (4100, 100),
        (4080, 98),
        (4060, 97),
        (4040, 94),
        (4020, 92),
        (4000, 91),
        (3980, 89),
        (3960, 86),
        (3940, 84),
        (3920, 81),
        (3900, 78),
        (3880, 75),
        (3860, 73),
        (3840, 70),
        (3820, 67),
        (3800, 63),
        (3780, 58),
        (3760, 51),
        (3740, 42),
        (3720, 35),
        (3700, 28),
        (3680, 24),
        (3660, 20),
        (3640, 15),
        (3600, 12),
        (3500, 8),
        (3400, 4),
        (OffMillivolts, 0),
    ];

    // ADC measurement: the one battery feature these headsets expose.
    protected override ushort BatteryFeature => 0x1F20;

    // getVoltage.
    protected override byte BatteryFunction => 0x00;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("G Pro X Wireless", 0x0ABA) { Kind = BatterySourceKind.Headset }];

    private readonly ConcurrentDictionary<string, LogitechHeadsetCharge> _charges = new();

    protected override BatteryReading ParseBattery(ReadOnlySpan<byte> report) =>
        ParseVoltage(report);

    protected override BatteryReading ParseBattery(HidChannel channel, ReadOnlySpan<byte> report)
    {
        if (report.Length < 7 || report[6] != Charging)
        {
            _charges.TryRemove(channel.DevicePath, out _);
            return ParseVoltage(report);
        }

        return ParseVoltage(report, _charges.GetOrAdd(channel.DevicePath, _ => new()));
    }

    internal static BatteryReading ParseVoltage(
        ReadOnlySpan<byte> report,
        LogitechHeadsetCharge? charge = null
    )
    {
        ThrowOnError(report, "reading the battery voltage");

        var millivolts = report[4] << 8 | report[5];

        // A powered-off headset reads far below the curve, which would show as a real 0%.
        if (millivolts < OffMillivolts)
        {
            throw new InvalidOperationException($"The headset reported {millivolts} mV: it is off.");
        }

        if (report[6] == Charging)
        {
            return new BatteryReading
            {
                Percent =
                    charge?.Update(millivolts)
                    ?? LogitechHidppProtocol.PercentFromMillivolts(
                        millivolts,
                        LogitechHeadsetCharge.Rising
                    ),
                Status = BatteryStatus.Charging,
            };
        }

        var percent = PercentFromMillivolts(millivolts);
        return new BatteryReading
        {
            Percent = percent,
            Status = percent >= 100 ? BatteryStatus.Full : BatteryStatus.Discharging,
        };
    }

    internal static int PercentFromMillivolts(int millivolts) =>
        LogitechHidppProtocol.PercentFromMillivolts(millivolts, Curve);
}

// G Hub's charge curve rises to ~4275 mV at 87%, then falls while the battery fills, so one charge is
// followed past its peak; a voltage alone cannot tell the two parts apart.
internal sealed class LogitechHeadsetCharge
{
    internal static readonly (int Millivolts, int Percent)[] Rising =
    [
        (4275, 87),
        (4250, 84),
        (4224, 80),
        (4199, 76),
        (4173, 72),
        (4147, 67),
        (4122, 61),
        (4097, 55),
        (4072, 48),
        (4046, 39),
        (4021, 26),
        (3996, 19),
        (3971, 15),
        (3946, 11),
        (3921, 6),
        (3895, 5),
        (3836, 4),
        (3806, 3),
        (3700, 2),
        (3590, 1),
        (3444, 0),
    ];

    internal static readonly (int Millivolts, int Percent)[] Falling =
    [
        (4273, 87),
        (4264, 88),
        (4257, 90),
        (4249, 91),
        (4241, 93),
        (4234, 94),
        (4227, 95),
        (4219, 97),
        (4212, 98),
        (4206, 100),
        // Lower still is full, not the 0 below a curve's end.
        (LogitechHeadsetProtocol.OffMillivolts, 100),
    ];

    private const int PeakSlack = 5;

    private readonly Lock _lock = new();
    private int _peak;
    private bool _falling;
    private int _percent;

    public int Update(int millivolts)
    {
        lock (_lock)
        {
            // Lower down the voltage still rises, so only the falling part's range can be the turn.
            _falling |= _peak >= Falling[^2].Millivolts && millivolts <= _peak - PeakSlack;
            _peak = Math.Max(_peak, millivolts);

            var percent = LogitechHidppProtocol.PercentFromMillivolts(
                millivolts,
                _falling ? Falling : Rising
            );
            _percent = Math.Max(_percent, percent);
            return _percent;
        }
    }
}
