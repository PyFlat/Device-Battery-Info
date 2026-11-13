using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Asus;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

// Built from G-Helper's layout (AsusMouse.cs, StrixImpactIIWireless.cs), not yet captured on the mouse.
[TestFixture]
public sealed class AsusProtocolTests
{
    private static byte[] Answer(byte level, byte charging)
    {
        var report = new byte[65];
        report[1] = 0x12;
        report[2] = 0x07;
        report[5] = level;
        report[10] = charging;
        return report;
    }

    private static byte[] Unreachable()
    {
        var report = new byte[65];
        report[1] = 0xFF;
        report[2] = 0xAA;
        return report;
    }

    [Test]
    public void Reads_the_level_in_quarters()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AsusProtocol.IsAnswer(Answer(3, 0)), Is.True);
            Assert.That(AsusProtocol.ParseBattery(Answer(3, 0)).Percent, Is.EqualTo(75));
            Assert.That(
                AsusProtocol.ParseBattery(Answer(3, 0)).Status,
                Is.EqualTo(BatteryStatus.Discharging)
            );
            Assert.That(AsusProtocol.ParseBattery(Answer(4, 0)).Status, Is.EqualTo(BatteryStatus.Full));
        }
    }

    [Test]
    public void Reads_charging()
    {
        var reading = AsusProtocol.ParseBattery(Answer(2, 1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(50));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));
            Assert.That(AsusProtocol.ParseBattery(Answer(0, 1)).Percent, Is.EqualTo(0));
        }
    }

    [Test]
    public void An_unreachable_mouse_is_an_answer_but_no_reading()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AsusProtocol.IsAnswer(Unreachable()), Is.True);
            Assert.Throws<InvalidOperationException>(() => AsusProtocol.ParseBattery(Unreachable()));
        }
    }

    [Test]
    public void Level_0_off_the_charger_is_no_reading()
    {
        Assert.Throws<InvalidOperationException>(() => AsusProtocol.ParseBattery(Answer(0, 0)));
    }

    [Test]
    public void A_level_above_4_is_no_reading()
    {
        Assert.Throws<InvalidOperationException>(() => AsusProtocol.ParseBattery(Answer(5, 0)));
    }

    [Test]
    public void Other_reports_are_not_the_answer()
    {
        var other = Answer(3, 0);
        other[2] = 0x04;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AsusProtocol.IsAnswer(other), Is.False);
            Assert.That(AsusProtocol.IsAnswer(new byte[65]), Is.False);
            Assert.That(AsusProtocol.IsAnswer([0x00, 0x12, 0x07]), Is.False);
        }
    }
}
