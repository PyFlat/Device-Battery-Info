using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Logitech;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class LogitechHeadsetProtocolTests
{
    // Frames recorded from a G Pro X Wireless (0ABA) on its own receiver interface FF43:0202.
    private static byte[] Frame(string hex) => Convert.FromHexString(hex.PadRight(40, '0'));

    private static readonly byte[] FeatureAnswer = Frame("11FF000106000400");
    private static readonly byte[] VoltageAnswer = Frame("11FF06010FA60100");

    private static bool IsReplyTo(byte[] report, byte featureIndex, byte function) =>
        LogitechHidppProtocol.IsReplyTo(
            report,
            LogitechHeadsetProtocol.DeviceIndex,
            featureIndex,
            function
        );

    [Test]
    public void The_feature_lookup_asks_for_the_adc_feature_as_the_receiver()
    {
        Assert.That(
            LogitechHidppProtocol.BuildFeatureRequest(LogitechHeadsetProtocol.DeviceIndex, 0x1F20),
            Is.EqualTo(new byte[] { 0x11, 0xFF, 0x00, 0x01, 0x1F, 0x20 })
        );
    }

    [Test]
    public void The_recorded_answers_give_feature_index_six_and_four_volts()
    {
        var reading = LogitechHeadsetProtocol.ParseVoltage(VoltageAnswer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(LogitechHidppProtocol.ParseFeatureIndex(FeatureAnswer), Is.EqualTo(6));
            Assert.That(reading.Percent, Is.EqualTo(91));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [Test]
    public void State_three_is_reported_as_charging()
    {
        var charging = Frame("11FF06010FA60300");

        Assert.That(
            LogitechHeadsetProtocol.ParseVoltage(charging).Status,
            Is.EqualTo(BatteryStatus.Charging)
        );
    }

    [Test]
    public void A_charging_headset_reads_from_the_charge_curve()
    {
        // 4281 mV on the cable, read while G Hub showed 87%.
        var charging = LogitechHeadsetProtocol.ParseVoltage(Frame("11FF060110B90300"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(charging.Percent, Is.EqualTo(87));
            Assert.That(charging.Status, Is.EqualTo(BatteryStatus.Charging));
        }
    }

    [Test]
    public void A_charge_rises_turns_after_its_peak_and_never_reads_lower()
    {
        var charge = new LogitechHeadsetCharge();

        int[] readings =
        [
            charge.Update(4097),
            charge.Update(4199),
            charge.Update(4275),
            charge.Update(4273),
            charge.Update(4264),
            charge.Update(4241),
            charge.Update(4245),
            charge.Update(4206),
        ];

        Assert.That(readings, Is.EqualTo(new[] { 55, 76, 87, 87, 88, 93, 93, 100 }));
    }

    [Test]
    public void A_dip_low_on_the_rising_part_is_not_taken_for_the_turn()
    {
        var charge = new LogitechHeadsetCharge();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(charge.Update(4100), Is.EqualTo(56));
            Assert.That(charge.Update(4090), Is.EqualTo(56));
            Assert.That(charge.Update(4199), Is.EqualTo(76));
        }
    }

    [Test]
    public void A_voltage_below_the_curve_throws_instead_of_reading_as_empty()
    {
        var off = Frame("11FF06010C000100");

        Assert.That(
            () => LogitechHeadsetProtocol.ParseVoltage(off),
            Throws.TypeOf<InvalidOperationException>()
        );
    }

    [Test]
    public void An_error_frame_throws_rather_than_parsing_as_a_voltage()
    {
        var error = Frame("11FFFF0001050000");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => LogitechHeadsetProtocol.ParseVoltage(error),
                Throws.TypeOf<InvalidOperationException>()
            );
            Assert.That(
                () => LogitechHidppProtocol.ParseFeatureIndex(error),
                Throws.TypeOf<InvalidOperationException>()
            );
        }
    }

    [Test]
    public void A_feature_index_of_zero_means_the_device_has_no_battery_feature()
    {
        Assert.That(
            () => LogitechHidppProtocol.ParseFeatureIndex(Frame("11FF00010000")),
            Throws.TypeOf<NotSupportedException>()
        );
    }

    [Test]
    public void Reports_for_other_features_are_not_taken_for_the_answer()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IsReplyTo(Frame("11FF0B0000000000"), 6, 0x01), Is.False);
            Assert.That(IsReplyTo(VoltageAnswer, 6, 0x01), Is.True);
            Assert.That(IsReplyTo(VoltageAnswer, 6, 0x11), Is.False);
        }
    }

    [TestCase(4200, 100)]
    [TestCase(4100, 100)]
    [TestCase(3990, 90)]
    [TestCase(3924, 82)] // read on the headset while G Hub showed 79%
    [TestCase(3760, 51)]
    [TestCase(3550, 10)]
    [TestCase(3300, 0)]
    public void The_curve_interpolates_between_its_calibration_points(int millivolts, int percent)
    {
        Assert.That(LogitechHeadsetProtocol.PercentFromMillivolts(millivolts), Is.EqualTo(percent));
    }
}
