using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Adb;
using DeviceBatteryInfo.Sources.Bluetooth;
using DeviceBatteryInfo.Sources.Razer;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Sources.SystemBattery;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Android;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class AdbBatterySourceTests
{
    private static AdbBatterySource Source(FakeAndroidDeviceManager android, string address) =>
        new(
            android,
            new BatterySlot(
                "phone",
                "Phone",
                BatterySourceKind.Phone,
                DeviceType.AdbPhone,
                AdbAddress: address
            )
        );

    [Test]
    public async Task Reads_level_and_charging_status_from_an_attached_device()
    {
        var android = new FakeAndroidDeviceManager();
        var phone = android.AddDevice("R58M123");
        phone.Battery = new AndroidBatteryState
        {
            Level = 87,
            IsCharging = true,
            Status = AndroidBatteryStatus.Charging,
        };

        var reading = await Source(android, "R58M123").ReadAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(87));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));
        }
    }

    [TestCase(AndroidBatteryStatus.Discharging, false, BatteryStatus.Discharging)]
    [TestCase(AndroidBatteryStatus.NotCharging, false, BatteryStatus.Discharging)]
    [TestCase(AndroidBatteryStatus.Full, true, BatteryStatus.Full)]
    [TestCase(AndroidBatteryStatus.Unknown, true, BatteryStatus.Charging)]
    [TestCase(AndroidBatteryStatus.Unknown, false, BatteryStatus.Unknown)]
    public void Maps_the_android_status(
        AndroidBatteryStatus status,
        bool isCharging,
        BatteryStatus expected
    )
    {
        var reading = AdbBatteryMapper.ToReading(
            new AndroidBatteryState { Level = 50, IsCharging = isCharging, Status = status }
        );

        Assert.That(reading.Status, Is.EqualTo(expected));
    }

    [Test]
    public void Fails_when_the_host_does_not_allow_adb()
    {
        var android = new FakeAndroidDeviceManager();
        android.AddDevice("R58M123");
        android.SetAccess(AndroidDeviceAccess.AdbNotAllowed);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Source(android, "R58M123").ReadAsync(CancellationToken.None)
        );
    }

    [Test]
    public void Fails_for_a_usb_serial_that_is_not_attached()
    {
        var android = new FakeAndroidDeviceManager();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Source(android, "R58M123").ReadAsync(CancellationToken.None)
        );
    }

    [Test]
    public async Task Connects_a_wireless_address_that_is_not_attached_yet()
    {
        var android = new FakeAndroidDeviceManager();

        var reading = await Source(android, "192.168.1.20:5555").ReadAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(android.FindDevice("192.168.1.20:5555"), Is.Not.Null);
            Assert.That(reading.Percent, Is.Not.Null);
        }
    }
}

[TestFixture]
public sealed class AndroidDeviceDiscoveryTests
{
    // Only the Android listing is exercised, so the HID and PnP readers are never touched.
    private static WindowsDeviceDiscovery Discovery(FakeAndroidDeviceManager android) =>
        new(null!, null!, android);

    [Test]
    public async Task Lists_attached_phones_with_their_battery_and_authorization()
    {
        var android = new FakeAndroidDeviceManager();
        var online = android.AddDevice("R58M123", info: new AndroidDeviceInfo("SM-G991B", "samsung", "o1s"));
        online.Battery = new AndroidBatteryState { Level = 64, Status = AndroidBatteryStatus.Discharging };
        android.AddDevice("9A1B2C", info: new AndroidDeviceInfo("Pixel 8", "Google", "shiba"));
        android.SetDeviceState("9A1B2C", AndroidDeviceState.Unauthorized);

        var phones = await Discovery(android).ListAndroidDevicesAsync(CancellationToken.None);

        Assert.That(
            phones,
            Is.EquivalentTo(
                new[]
                {
                    new AndroidDeviceCandidate("R58M123", "SM-G991B", 64, NeedsAuthorization: false),
                    new AndroidDeviceCandidate("9A1B2C", "Pixel 8", null, NeedsAuthorization: true),
                }
            )
        );
    }

    [Test]
    public async Task Lists_nothing_while_the_host_does_not_allow_adb()
    {
        var android = new FakeAndroidDeviceManager();
        android.AddDevice("R58M123");
        android.SetAccess(AndroidDeviceAccess.AdbNotEnabled);

        Assert.That(
            await Discovery(android).ListAndroidDevicesAsync(CancellationToken.None),
            Is.Empty
        );
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
