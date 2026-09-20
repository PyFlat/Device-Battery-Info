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
            Assert.That(reading.Percent, Is.EqualTo(78));
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
    [TestCase(4150, 100)]
    [TestCase(3990, 75)]
    [TestCase(3830, 50)]
    [TestCase(3740, 20)]
    [TestCase(3320, 0)]
    public void The_curve_interpolates_between_its_calibration_points(int millivolts, int percent)
    {
        Assert.That(LogitechHeadsetProtocol.PercentFromMillivolts(millivolts), Is.EqualTo(percent));
    }
}
