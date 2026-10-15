using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Mchose;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class MchoseProtocolTests
{
    // Captured from a V9 Pro dongle (USBPcap) by the device's owner, first 8 of 64 bytes.
    private static readonly byte[] OnBattery = Convert.FromHexString("55655A0200000000");
    private static readonly byte[] Charging = Convert.FromHexString("5565320300000000");
    private static readonly byte[] OnCable = Convert.FromHexString("55655A0000000000");
    private static readonly byte[] HeadsetOff = Convert.FromHexString("5565001A00000000");
    private static readonly byte[] Connected = Convert.FromHexString("5511020000000000");

    [Test]
    public void Reads_the_level_on_battery()
    {
        var reading = MchoseProtocol.ParseBattery(OnBattery);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(90));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [TestCaseSource(nameof(CableFrames))]
    public void On_the_cable_it_is_charging_with_no_percent(byte[] frame)
    {
        var reading = MchoseProtocol.ParseBattery(frame);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.Null);
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));
        }
    }

    // The pushed frame while charging, and the answer to a query with the cable in (charging or full).
    private static IEnumerable<byte[]> CableFrames() => [Charging, OnCable];

    [Test]
    public void A_headset_that_is_off_is_no_reading() =>
        Assert.Throws<InvalidOperationException>(() => MchoseProtocol.ParseBattery(HeadsetOff));

    [Test]
    public void Only_the_battery_report_counts()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(MchoseProtocol.IsBatteryReport(OnBattery), Is.True);
            Assert.That(MchoseProtocol.IsBatteryReport(Connected), Is.False);
        }
    }
}
