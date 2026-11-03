using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.SteelSeries;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class SteelSeriesProtocolTests
{
    // Captured from a Rival 3 Wireless over its dongle, before and after new AAA cells, trimmed from 65 bytes.
    private static readonly byte[] OldCells = Convert.FromHexString("0013000000000000");
    private static readonly byte[] NewCells = Convert.FromHexString("003B000000000000");

    [Test]
    public void Reads_level_from_the_answer()
    {
        var reading = SteelSeriesProtocol.ParseBattery(OldCells);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SteelSeriesProtocol.IsBatteryReport(OldCells), Is.True);
            Assert.That(reading.Percent, Is.EqualTo(19));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
            Assert.That(SteelSeriesProtocol.ParseBattery(NewCells).Percent, Is.EqualTo(59));
        }
    }

    [Test]
    public void A_level_above_100_is_no_reading()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SteelSeriesProtocol.ParseBattery(Convert.FromHexString("0065000000000000"))
        );
    }
}
