using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Logitech;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class LogitechVoltageBatteryProtocolTests
{
    // Frames recorded from a G502 Lightspeed behind receiver C539.
    private static byte[] Frame(string hex) => Convert.FromHexString(hex.PadRight(40, '0'));

    private static readonly byte[] FeatureAnswer = Frame("110100010600020000000000");
    private static readonly byte[] VoltageAnswer = Frame("110106011049000000000000");

    private static BatteryStatus StatusWithFlags(byte flags) =>
        LogitechVoltageBatteryProtocol.ParseVoltage(Frame($"110106011049{flags:X2}")).Status;

    [Test]
    public void The_battery_feature_lookup_asks_for_feature_1001()
    {
        Assert.That(
            LogitechHidppProtocol.BuildFeatureRequest(LogitechVoltageBatteryProtocol.DeviceIndex, 0x1001),
            Is.EqualTo(new byte[] { 0x11, 0x01, 0x00, 0x01, 0x10, 0x01 })
        );
    }

    [Test]
    public void The_recorded_answers_give_feature_index_six_and_a_voltage()
    {
        var reading = LogitechVoltageBatteryProtocol.ParseVoltage(VoltageAnswer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(LogitechHidppProtocol.ParseFeatureIndex(FeatureAnswer), Is.EqualTo(6));
            Assert.That(reading.Percent, Is.EqualTo(99));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [TestCase(0x80, BatteryStatus.Charging)]
    [TestCase(0x81, BatteryStatus.Full)]
    [TestCase(0x82, BatteryStatus.Discharging)]
    [TestCase(0x83, BatteryStatus.Unknown)]
    [TestCase(0x87, BatteryStatus.Unknown)]
    [TestCase(0x88, BatteryStatus.Charging)]
    public void On_external_power_the_charge_state_bits_give_the_status(byte flags, BatteryStatus status)
    {
        Assert.That(StatusWithFlags(flags), Is.EqualTo(status));
    }

    [TestCase(0x01)]
    [TestCase(0x20)]
    public void Without_external_power_the_mouse_is_discharging(byte flags)
    {
        // 0x20 is the critical level bit, not a full battery.
        Assert.That(StatusWithFlags(flags), Is.EqualTo(BatteryStatus.Discharging));
    }

    [Test]
    public void Zero_millivolts_throws()
    {
        Assert.That(
            () => LogitechVoltageBatteryProtocol.ParseVoltage(Frame("110106010000")),
            Throws.TypeOf<InvalidOperationException>()
        );
    }

    [Test]
    public void An_error_report_throws()
    {
        Assert.That(
            () => LogitechVoltageBatteryProtocol.ParseVoltage(Frame("1101FF06010500")),
            Throws.TypeOf<InvalidOperationException>()
        );
    }

    [TestCase(4300, 100)]
    [TestCase(4186, 100)]
    [TestCase(3989, 80)]
    [TestCase(3900, 67)]
    [TestCase(3500, 0)]
    [TestCase(3400, 0)]
    public void The_curve_interpolates_between_its_calibration_points(int millivolts, int percent)
    {
        Assert.That(LogitechVoltageBatteryProtocol.PercentFromMillivolts(millivolts), Is.EqualTo(percent));
    }
}
