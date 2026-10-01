using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.Sony;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class SonyProtocolTests
{
    // Status bytes captured from a DualSense at 20%: 0x12 charging on USB, 0x02 discharging on Bluetooth.
    private static byte[] UsbReport(byte status)
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[53] = status;
        return report;
    }

    private static byte[] BluetoothFullReport(byte status)
    {
        var report = new byte[78];
        report[0] = 0x31;
        report[54] = status;
        return report;
    }

    private static byte[] BluetoothShortReport()
    {
        var report = new byte[78];
        report[0] = 0x01;
        return report;
    }

    [Test]
    public void Reads_level_and_state_over_usb_and_bluetooth()
    {
        var usb = SonyProtocol.ParseStatus(UsbReport(0x12));
        var bluetooth = SonyProtocol.ParseStatus(BluetoothFullReport(0x02));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(usb.Percent, Is.EqualTo(20));
            Assert.That(usb.Status, Is.EqualTo(BatteryStatus.Charging));
            Assert.That(bluetooth.Percent, Is.EqualTo(20));
            Assert.That(bluetooth.Status, Is.EqualTo(BatteryStatus.Discharging));
            Assert.That(SonyProtocol.ParseStatus(UsbReport(0x2A)).Status, Is.EqualTo(BatteryStatus.Full));
            Assert.That(SonyProtocol.ParseStatus(UsbReport(0x2A)).Percent, Is.EqualTo(100));
        }
    }

    [Test]
    public void The_short_bluetooth_report_carries_no_status() =>
        Assert.That(SonyProtocol.IsStatusReport(BluetoothShortReport()), Is.False);

    [Test]
    public void An_error_charge_state_is_no_reading() =>
        Assert.Throws<InvalidOperationException>(() => SonyProtocol.ParseStatus(UsbReport(0xF2)));

    // Streams the short report until the calibration feature is read, as a fresh Bluetooth link does.
    private sealed class BluetoothTransport : IHidTransport
    {
        private static readonly HidCandidate Pad = new(
            "bt",
            0x054C,
            0x0CE6,
            null,
            "DualSense Wireless Controller",
            547,
            InputReportLength: 78,
            OutputReportLength: 547,
            UsagePage: 0x0001,
            Usage: 0x0005
        );

        public List<byte> FeaturesRead { get; } = [];

        public IReadOnlyList<HidCandidate> FindCandidates(
            int vendorId,
            int productId,
            int? interfaceNumber,
            int minFeatureReportLength
        ) => productId == 0x0CE6 ? [Pad] : [];

        public IReadOnlyList<HidCandidate> ListFeatureReportDevices() => [Pad];

        public Task<byte[]> ExchangeAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<byte[]> ExchangeReportsAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        )
        {
            var report = FeaturesRead.Contains(0x05) ? BluetoothFullReport(0x02) : BluetoothShortReport();
            return isComplete(report)
                ? Task.FromResult(report)
                : throw new InvalidOperationException("No matching report within the budget.");
        }

        public Task<byte[]> GetFeatureAsync(
            string devicePath,
            byte reportId,
            CancellationToken cancellationToken
        )
        {
            FeaturesRead.Add(reportId);
            return Task.FromResult(new byte[547]);
        }
    }

    [Test]
    public async Task A_short_bluetooth_report_reads_calibration_then_the_full_report()
    {
        var devices = new DeviceCatalog();
        devices.Set(
            [
                new BatterySlot(
                    "pad",
                    "Pad",
                    BatterySourceKind.Controller,
                    DeviceType.Catalog,
                    CatalogDeviceId: "sony-dualsense"
                ),
            ]
        );
        var transport = new BluetoothTransport();
        var family = new HidFamily([new SonyProtocol()], transport, Serilog.Core.Logger.None);

        var sources = await new DeviceFamilyProvider([family], devices).DiscoverAsync(
            CancellationToken.None
        );
        var reading = await sources.Single().ReadAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(transport.FeaturesRead, Is.EqualTo(new byte[] { 0x05 }));
            Assert.That(reading.Percent, Is.EqualTo(20));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }
}
