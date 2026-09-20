using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Adb;
using DeviceBatteryInfo.Sources.Bluetooth;
using DeviceBatteryInfo.Sources.Razer;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Sources.SystemBattery;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class AdbBatteryParserTests
{
    [Test]
    public void Parses_level_and_charging_status()
    {
        const string dumpsys = """
            Current Battery Service state:
              AC powered: true
              status: 2
              level: 87
              scale: 100
            """;

        var reading = AdbBatteryParser.Parse(dumpsys);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(87));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));
        }
    }

    [Test]
    public void Rescales_when_scale_is_not_a_hundred()
    {
        var reading = AdbBatteryParser.Parse("level: 128\nscale: 255\nstatus: 3\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(50));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [Test]
    public void Returns_unavailable_when_no_level_line()
    {
        Assert.That(AdbBatteryParser.Parse("status: 2\n"), Is.EqualTo(BatteryReading.Unavailable));
    }
}

[TestFixture]
public sealed class RazerProtocolTests
{
    [Test]
    public void Request_has_command_and_checksum()
    {
        var request = RazerProtocol.BuildRequest(RazerProtocol.CommandBatteryLevel);

        Assert.That(request, Has.Length.EqualTo(90));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request[1], Is.EqualTo(0x1F));
            Assert.That(request[5], Is.EqualTo(0x02));
            Assert.That(request[6], Is.EqualTo(0x07));
            Assert.That(request[7], Is.EqualTo(0x80));
            // XOR of bytes 3..87: only 0x02, 0x07 and 0x80 are non-zero
            Assert.That(request[88], Is.EqualTo(0x02 ^ 0x07 ^ 0x80));
        }
    }

    [Test]
    public void Percent_scales_from_byte_range()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RazerProtocol.PercentFromRaw(255), Is.EqualTo(100));
            Assert.That(RazerProtocol.PercentFromRaw(0), Is.EqualTo(0));
            Assert.That(RazerProtocol.PercentFromRaw(128), Is.EqualTo(50));
        }
    }

    private static byte[] CompletedFrame(byte commandId, byte value)
    {
        var report = new byte[91];
        report[1] = 0x02; // status: successful
        report[7] = 0x07; // command class echo: power
        report[8] = commandId; // command id echo
        report[10] = value; // Razer argument 1
        return report;
    }

    [Test]
    public void Completed_response_needs_success_status_and_the_matching_command_echo()
    {
        Assert.That(
            RazerProtocol.IsCompletedResponse(
                CompletedFrame(RazerProtocol.CommandBatteryLevel, 128),
                RazerProtocol.CommandBatteryLevel
            ),
            Is.True
        );
    }

    [Test]
    public void A_not_ready_placeholder_frame_is_not_a_completed_response()
    {
        Assert.That(
            RazerProtocol.IsCompletedResponse(
                new byte[91],
                RazerProtocol.CommandBatteryLevel
            ),
            Is.False
        );
    }

    [Test]
    public void A_frame_answering_a_different_command_is_not_accepted()
    {
        var chargingFrame = CompletedFrame(RazerProtocol.CommandChargingStatus, 1);

        Assert.That(
            RazerProtocol.IsCompletedResponse(
                chargingFrame,
                RazerProtocol.CommandBatteryLevel
            ),
            Is.False
        );
    }
}

[TestFixture]
public sealed class BluetoothBatteryParserTests
{
    [TestCase("90", 90)]
    [TestCase(" 70 ", 70)]
    [TestCase("battery: 55%", 55)]
    public void Parses_a_level(string raw, int expected)
    {
        Assert.That(BluetoothBatteryParser.ParsePercent(raw), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    [TestCase("250")]
    public void Rejects_missing_or_out_of_range(string? raw)
    {
        Assert.That(BluetoothBatteryParser.ParsePercent(raw), Is.Null);
    }
}

[TestFixture]
public sealed class SystemBatteryReadingFactoryTests
{
    [Test]
    public void Charging_flag_wins()
    {
        var status = new NativePowerStatus
        {
            AcLineStatus = NativePowerStatus.AcOnline,
            BatteryFlag = NativePowerStatus.FlagCharging,
            BatteryLifePercent = 64,
            BatteryLifeTime = NativePowerStatus.TimeUnknown,
        };

        var reading = SystemBatteryReadingFactory.Create(status);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(64));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));
        }
    }

    [Test]
    public void On_ac_at_full_is_full()
    {
        var status = new NativePowerStatus
        {
            AcLineStatus = NativePowerStatus.AcOnline,
            BatteryFlag = 0,
            BatteryLifePercent = 100,
        };

        Assert.That(
            SystemBatteryReadingFactory.Create(status).Status,
            Is.EqualTo(BatteryStatus.Full)
        );
    }

    [Test]
    public void Off_ac_reports_time_to_empty()
    {
        var status = new NativePowerStatus
        {
            AcLineStatus = NativePowerStatus.AcOffline,
            BatteryFlag = 0,
            BatteryLifePercent = 42,
            BatteryLifeTime = 3600,
        };

        var reading = SystemBatteryReadingFactory.Create(status);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
            Assert.That(reading.TimeToEmpty, Is.EqualTo(TimeSpan.FromHours(1)));
        }
    }

    [Test]
    public void A_desktop_has_no_battery()
    {
        var status = new NativePowerStatus
        {
            BatteryFlag = NativePowerStatus.FlagNoBattery,
            BatteryLifePercent = NativePowerStatus.PercentUnknown,
        };

        Assert.That(SystemBatteryReadingFactory.HasBattery(status), Is.False);
    }
}
