using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Aula;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class AulaProtocolTests
{
    // Captured from an F75 at 97% on its 2.4G receiver: on battery, then with the cable in (reads 100).
    private static readonly byte[] OnBattery = Convert.FromHexString("134A0100026101000000000000000000000000C2");
    private static readonly byte[] OnCable = Convert.FromHexString("134A0100026410000000000000000000000000D4");

    [Test]
    public void Builds_the_battery_request_with_its_checksum() =>
        Assert.That(
            Convert.ToHexString(AulaProtocol.BuildRequest(0x4A)),
            Is.EqualTo("134A00000000000000000000000000000000005D")
        );

    [Test]
    public void Reads_level_and_state_from_the_reply()
    {
        var reading = AulaProtocol.ParseBattery(OnBattery);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AulaProtocol.IsBatteryReply(OnBattery), Is.True);
            Assert.That(reading.Percent, Is.EqualTo(97));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [Test]
    public void On_the_cable_it_charges_with_no_level()
    {
        var reading = AulaProtocol.ParseBattery(OnCable);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.Null);
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));
        }
    }

    [Test]
    public void Other_frames_and_bad_checksums_are_not_the_reply()
    {
        var corrupt = (byte[])OnBattery.Clone();
        corrupt[^1] ^= 0xFF;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                AulaProtocol.IsBatteryReply(Convert.FromHexString("130A0100040564010000000000000000000000008C")),
                Is.False
            );
            Assert.That(AulaProtocol.IsBatteryReply(corrupt), Is.False);
        }
    }
}
