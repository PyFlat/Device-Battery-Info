using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Asus;
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
