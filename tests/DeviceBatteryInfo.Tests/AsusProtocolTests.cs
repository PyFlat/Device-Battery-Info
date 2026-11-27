using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Asus;
using DeviceBatteryInfo.Sources.Hid;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class AsusProtocolTests
{
    // Logged by the owner on a Strix Impact II Wireless, trimmed from 65 bytes: on battery over the dongle,
    // the same mouse on its cable (it reads a step high there), and the dongle while the mouse is on the cable.
    private static readonly byte[] OnBattery = Convert.FromHexString("00120700000302010000000000000000");
    private static readonly byte[] OnCable = Convert.FromHexString("00120700000402010000000000000000");
    private static readonly byte[] Unreachable = Convert.FromHexString("00FFAA00000000000000000000000000");

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class MouseTransport : IHidTransport
    {
        public byte[] Answer { get; set; } = OnBattery;

        public IReadOnlyList<HidCandidate> FindCandidates(
            int vendorId,
            int productId,
            int? interfaceNumber,
            int minFeatureReportLength
        ) => [];

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
            TimeSpan budget,
            CancellationToken cancellationToken
        ) => isComplete(Answer) ? Task.FromResult(Answer) : throw new TimeoutException("Not answered.");

        public Task<byte[]> ExchangeGetInputReportAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private static byte[] WithLevel(byte level)
    {
        var report = OnBattery.ToArray();
        report[5] = level;
        return report;
    }

    [Test]
    public void Reads_the_level_in_quarters_over_the_dongle()
    {
        var reading = AsusProtocol.ParseBattery(OnBattery, onCable: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AsusProtocol.IsAnswer(OnBattery), Is.True);
            Assert.That(reading.Percent, Is.EqualTo(75));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
            Assert.That(
                AsusProtocol.ParseBattery(OnCable, onCable: false).Status,
                Is.EqualTo(BatteryStatus.Full)
            );
        }
    }

    [Test]
    public void On_the_cable_it_charges_with_no_level()
    {
        var reading = AsusProtocol.ParseBattery(OnCable, onCable: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));
            Assert.That(reading.Percent, Is.Null);
            Assert.That(
                AsusProtocol.ParseBattery(WithLevel(0), onCable: true).Status,
                Is.EqualTo(BatteryStatus.Charging)
            );
        }
    }

    [Test]
    public async Task Back_on_the_dongle_it_keeps_charging_until_the_level_settles()
    {
        var time = new ManualTimeProvider();
        var protocol = new AsusProtocol(time);
        var device = protocol.Devices[0];
        var transport = new MouseTransport();
        var dongle = new HidChannel(transport, "dongle", 0x1949, HidReportKind.InputOutput);
        var cable = new HidChannel(transport, "cable", AsusProtocol.CableProductId, HidReportKind.InputOutput);

        transport.Answer = OnCable;
        var charging = await protocol.ReadAsync(cable, device, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(10));
        var justUnplugged = await protocol.ReadAsync(dongle, device, CancellationToken.None);
        transport.Answer = OnBattery;
        time.Advance(AsusProtocol.CableSettleTime);
        var settled = await protocol.ReadAsync(dongle, device, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(charging.Status, Is.EqualTo(BatteryStatus.Charging));
            Assert.That(justUnplugged.Status, Is.EqualTo(BatteryStatus.Charging));
            Assert.That(justUnplugged.Percent, Is.Null);
            Assert.That(settled.Percent, Is.EqualTo(75));
            Assert.That(settled.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [Test]
    public async Task A_full_mouse_on_the_dongle_reads_full_when_it_was_never_on_the_cable()
    {
        var protocol = new AsusProtocol(new ManualTimeProvider());
        var transport = new MouseTransport { Answer = OnCable };
        var dongle = new HidChannel(transport, "dongle", 0x1949, HidReportKind.InputOutput);

        var reading = await protocol.ReadAsync(dongle, protocol.Devices[0], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(100));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Full));
        }
    }

    [Test]
    public void An_unreachable_mouse_is_an_answer_but_no_reading()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AsusProtocol.IsAnswer(Unreachable), Is.True);
            Assert.Throws<InvalidOperationException>(() =>
                AsusProtocol.ParseBattery(Unreachable, onCable: false)
            );
        }
    }

    [Test]
    public void Level_0_over_the_dongle_is_no_reading()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AsusProtocol.ParseBattery(WithLevel(0), onCable: false)
        );
    }

    [Test]
    public void A_level_above_4_is_no_reading()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AsusProtocol.ParseBattery(WithLevel(5), onCable: false)
        );
    }

    [Test]
    public void Other_reports_are_not_the_answer()
    {
        var other = OnBattery.ToArray();
        other[2] = 0x04;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AsusProtocol.IsAnswer(other), Is.False);
            Assert.That(AsusProtocol.IsAnswer(new byte[65]), Is.False);
            Assert.That(AsusProtocol.IsAnswer([0x00, 0x12, 0x07]), Is.False);
        }
    }
}
