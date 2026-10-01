using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Corsair;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class CorsairProtocolTests
{
    [Test]
    public void Reads_level_without_the_mic_up_flag()
    {
        var reading = CorsairProtocol.ParseBattery([0x64, 0x00, 0x80 | 57, 0x00, 1]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(57));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [Test]
    public void Charging_and_full_states_map_to_status()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CorsairProtocol.ParseBattery([0x64, 0, 40, 0, 5]).Status,
                Is.EqualTo(BatteryStatus.Charging)
            );
            Assert.That(
                CorsairProtocol.ParseBattery([0x64, 0, 100, 0, 4]).Status,
                Is.EqualTo(BatteryStatus.Full)
            );
        }
    }

    [Test]
    public void A_disconnected_headset_is_no_reading() =>
        Assert.Throws<InvalidOperationException>(() =>
            CorsairProtocol.ParseBattery([0x64, 0, 0, 0, 0])
        );
}
