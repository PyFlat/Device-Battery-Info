using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Logitech;

// Verified on a G Pro X Superlight 2, both behind its receiver (C54D) and on the cable (C09B).
// Slot 1 is the first paired device. A mouse on its cable answers to any index.
internal sealed class LogitechProtocol() : LogitechHidppProtocol(0xFF00, 0x0002, DeviceIndex)
{
    internal const byte DeviceIndex = 0x01;

    protected override ushort BatteryFeature => 0x1004;

    // Unified battery: getStatus.
    protected override byte BatteryFunction => 0x01;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("G Pro X Superlight 2", 0xC54D, 0xC09B)];

    protected override BatteryReading ParseBattery(ReadOnlySpan<byte> report) => ParseStatus(report);

    internal static BatteryReading ParseStatus(ReadOnlySpan<byte> report)
    {
        ThrowOnError(report, "reading the battery status");

        var percent = report[4];
        var status = report[6] switch
        {
            0 => percent >= 100 ? BatteryStatus.Full : BatteryStatus.Discharging,
            1 or 2 => BatteryStatus.Charging,
            3 => BatteryStatus.Full,
            _ => BatteryStatus.Unknown,
        };

        return new BatteryReading { Percent = percent, Status = status };
    }
}
