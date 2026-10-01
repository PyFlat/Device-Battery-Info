using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Rapoo;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class RapooProtocolTests
{
    // Captured from a VT3 PRO: over the dongle, then on the cable before and after a firmware update.
    private static readonly byte[] OnBattery = Convert.FromHexString("BBB051E803012E");
    private static readonly byte[] Charging = Convert.FromHexString("BBB051E8030225");
    private static readonly byte[] ChargingNoLevel = Convert.FromHexString("BBB051E803027F");
    private static readonly byte[] FullOnCable = Convert.FromHexString("BBB051E8030264");

    [Test]
    public void Reads_level_and_state_from_the_pushed_status()
    {
        var reading = RapooProtocol.ParseStatus(OnBattery);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(46));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
            Assert.That(RapooProtocol.ParseStatus(Charging).Status, Is.EqualTo(BatteryStatus.Charging));
        }
    }

    [Test]
    public void Charging_without_a_level_has_no_percent()
    {
        var reading = RapooProtocol.ParseStatus(ChargingNoLevel);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.Null);
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));
        }
    }

    [Test]
    public void Charging_at_100_is_full() =>
        Assert.That(RapooProtocol.ParseStatus(FullOnCable).Status, Is.EqualTo(BatteryStatus.Full));

    [Test]
    public void Only_the_status_report_counts() =>
        Assert.That(RapooProtocol.IsStatusReport([0x07, 0, 0, 0, 0, 1, 50]), Is.False);
}
