using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Sony;

// The DualSense streams input reports on its own, so a read only listens. The status byte (layout from
// Linux hid-playstation) holds the level in the low nibble and the charge state in the high one.
internal sealed class SonyProtocol()
    : HidProtocol(
        "Sony",
        vendorId: 0x054C,
        reportLength: 48,
        HidReportKind.InputOutput,
        usagePage: 0x0001,
        usage: 0x0005
    )
{
    private const byte UsbReportId = 0x01;
    private const byte BluetoothFullReportId = 0x31;
    private const int UsbReportLength = 64;
    private const int UsbStatusIndex = 53;

    // The Bluetooth report carries one extra header byte before the same layout.
    private const int BluetoothStatusIndex = 54;

    // Reading the calibration feature switches a Bluetooth DualSense from its short 0x01 report (no
    // battery) to the full 0x31 one, until it disconnects.
    private const byte CalibrationFeatureId = 0x05;

    private const int Discharging = 0;
    private const int Charging = 1;
    private const int Full = 2;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("DualSense", 0x0CE6) { Kind = BatterySourceKind.Controller }];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    )
    {
        var report = await channel.ExchangeAsync([], IsInputReport, cancellationToken);
        if (!IsStatusReport(report))
        {
            await channel.GetFeatureAsync(CalibrationFeatureId, cancellationToken);
            report = await channel.ExchangeAsync([], IsStatusReport, cancellationToken);
        }

        return ParseStatus(report);
    }

    private static bool IsInputReport(byte[] report) =>
        report.Length > 0 && report[0] is UsbReportId or BluetoothFullReportId;

    // Over USB the 0x01 report is the full one (64 bytes); over Bluetooth 0x01 is the short one, read
    // into the longer Bluetooth buffer.
    internal static bool IsStatusReport(byte[] report) =>
        (report.Length == UsbReportLength && report[0] == UsbReportId)
        || (report.Length > BluetoothStatusIndex && report[0] == BluetoothFullReportId);

    internal static BatteryReading ParseStatus(byte[] report)
    {
        var status = report[report[0] == BluetoothFullReportId ? BluetoothStatusIndex : UsbStatusIndex];
        var percent = Math.Min((status & 0x0F) * 10, 100);

        return (status >> 4) switch
        {
            Discharging => BatteryReading.FromPercent(percent, BatteryStatus.Discharging),
            Charging => BatteryReading.FromPercent(percent, BatteryStatus.Charging),
            Full => BatteryReading.FromPercent(percent, BatteryStatus.Full),
            var state => throw new InvalidOperationException($"The controller reported charge state {state}."),
        };
    }
}
