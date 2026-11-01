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

    [Test]
    public void Builds_correct_mt760_query_request()
    {
        var request = RapooOfficeProtocol.BuildBatteryRequest();
        Assert.That(request.Length, Is.EqualTo(32));
        Assert.That(request[0..3], Is.EqualTo(new byte[] { 0xBA, 0xA5, 0xAA }));
    }

    [Test]
    public void Reads_mt760_battery_level_and_discharging_state()
    {
        // Live response captured from MT760: 0x01 (status), 0x01 (discharging), 0x42 (66%)
        byte[] liveResponse = [0x01, 0x01, 0x42, .. new byte[29]];
        Assert.That(RapooOfficeProtocol.IsBatteryReply(liveResponse), Is.True);

        var reading = RapooOfficeProtocol.ParseBattery(liveResponse);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(66));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [Test]
    public void Reads_mt760_charging_and_full_states()
    {
        byte[] chargingResponse = [0x01, 0x02, 85, .. new byte[29]];
        var reading = RapooOfficeProtocol.ParseBattery(chargingResponse);
        Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));

        byte[] fullResponse = [0x01, 0x02, 100, .. new byte[29]];
        var fullReading = RapooOfficeProtocol.ParseBattery(fullResponse);
        Assert.That(fullReading.Status, Is.EqualTo(BatteryStatus.Full));
    }

    [Test]
    public void Mt760_rejects_unrelated_or_invalid_reports()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RapooOfficeProtocol.IsBatteryReply([0x00, 0x01, 50, .. new byte[29]]), Is.False);
            Assert.That(RapooOfficeProtocol.IsBatteryReply([0x01, 0x01, 0, .. new byte[29]]), Is.False);
            Assert.That(RapooOfficeProtocol.IsBatteryReply([0x01, 0x01, 101, .. new byte[29]]), Is.False);
            Assert.That(RapooOfficeProtocol.IsBatteryReply([0x01, 0x00]), Is.False);
        }
    }
}
