using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.Logitech;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class LogitechProtocolTests
{
    // Frames recorded from a G Pro X Superlight 2 behind receiver C54D.
    private static byte[] Frame(string hex) => Convert.FromHexString(hex.PadRight(40, '0'));

    private static readonly byte[] FeatureAnswer = Frame("110100010600050000000000");
    private static readonly byte[] StatusAnswer = Frame("110106115008000000000000");

    private static bool IsReplyTo(byte[] report, byte featureIndex, byte function) =>
        LogitechHidppProtocol.IsReplyTo(report, LogitechProtocol.DeviceIndex, featureIndex, function);

    [Test]
    public void The_battery_feature_lookup_asks_for_feature_1004_in_bytes_four_and_five()
    {
        Assert.That(
            LogitechHidppProtocol.BuildFeatureRequest(LogitechProtocol.DeviceIndex, 0x1004),
            Is.EqualTo(new byte[] { 0x11, 0x01, 0x00, 0x01, 0x10, 0x04 })
        );
    }

    [Test]
    public void The_recorded_answers_give_feature_index_six_and_eighty_percent()
    {
        var reading = LogitechProtocol.ParseStatus(StatusAnswer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(LogitechHidppProtocol.ParseFeatureIndex(FeatureAnswer), Is.EqualTo(6));
            Assert.That(reading.Percent, Is.EqualTo(80));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [Test]
    public void The_status_recorded_on_the_cable_is_reported_as_charging()
    {
        var charging = Frame("110106115008010100000000");

        Assert.That(LogitechProtocol.ParseStatus(charging).Status, Is.EqualTo(BatteryStatus.Charging));
    }

    [Test]
    public void Notifications_for_other_features_are_not_taken_for_the_answer()
    {
        var notification = Frame("11010B00000000");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(IsReplyTo(notification, 6, 0x11), Is.False);
            Assert.That(IsReplyTo(StatusAnswer, 6, 0x11), Is.True);
            Assert.That(IsReplyTo(StatusAnswer, 6, 0x01), Is.False);
        }
    }

    [Test]
    public void A_missing_feature_is_an_error_not_a_battery_reading()
    {
        var missing = Frame("110100010000000000000000");

        Assert.Throws<NotSupportedException>(() => LogitechHidppProtocol.ParseFeatureIndex(missing));
    }

    private sealed class ReceiverTransport : IHidTransport
    {
        private static readonly HidCandidate Short =
            new(@"receiver-short", 0x046D, 0xC54D, 2, "USB Receiver", 0, null, 7, 7, 0xFF00, 0x0001);

        private static readonly HidCandidate Long =
            new(@"receiver-long", 0x046D, 0xC54D, 2, "USB Receiver", 0, null, 20, 20, 0xFF00, 0x0002);

        public List<string> Used { get; } = [];

        public IReadOnlyList<HidCandidate> FindCandidates(
            int vendorId,
            int productId,
            int? interfaceNumber,
            int minFeatureReportLength
        ) => vendorId == 0x046D && productId == 0xC54D ? [Short, Long] : [];

        public IReadOnlyList<HidCandidate> ListFeatureReportDevices() => [];

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
            CancellationToken cancellationToken
        )
        {
            Used.Add(devicePath);
            return Task.FromResult(request[2] == 0x00 ? FeatureAnswer : StatusAnswer);
        }
    }

    [Test]
    public async Task The_family_reads_through_the_long_report_interface_only()
    {
        var transport = new ReceiverTransport();
        var devices = new DeviceCatalog();
        devices.Set(
            [
                new BatterySlot(
                    "mouse",
                    "Mouse",
                    BatterySourceKind.Mouse,
                    DeviceType.Catalog,
                    CatalogDeviceId: "logitech-g-pro-x-superlight-2"
                ),
            ]
        );
        var family = new HidFamily([new LogitechProtocol()], transport, Serilog.Core.Logger.None);

        var sources = await new DeviceFamilyProvider([family], devices).DiscoverAsync(
            CancellationToken.None
        );
        var reading = await sources.Single().ReadAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(80));
            Assert.That(transport.Used, Is.All.EqualTo("receiver-long"));
        }
    }
}
