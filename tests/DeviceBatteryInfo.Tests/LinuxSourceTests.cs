using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Bluetooth;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.SystemBattery;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class PowerSupplyBatteryParserTests
{
    // Captured from a laptop whose battery reports charge (uAh) and current (uA) instead of energy.
    private const string ChargeBattery = """
        DEVTYPE=power_supply
        POWER_SUPPLY_NAME=BAT1
        POWER_SUPPLY_TYPE=Battery
        POWER_SUPPLY_STATUS=Discharging
        POWER_SUPPLY_PRESENT=1
        POWER_SUPPLY_TECHNOLOGY=Li-ion
        POWER_SUPPLY_CYCLE_COUNT=34
        POWER_SUPPLY_VOLTAGE_MIN_DESIGN=10800000
        POWER_SUPPLY_VOLTAGE_NOW=10826000
        POWER_SUPPLY_CURRENT_NOW=973000
        POWER_SUPPLY_CHARGE_FULL_DESIGN=4500000
        POWER_SUPPLY_CHARGE_FULL=2511000
        POWER_SUPPLY_CHARGE_NOW=1997000
        POWER_SUPPLY_CAPACITY=80
        POWER_SUPPLY_CAPACITY_LEVEL=Normal
        POWER_SUPPLY_TYPE=Battery
        POWER_SUPPLY_MODEL_NAME=CP700280-03
        POWER_SUPPLY_MANUFACTURER=PAC
        POWER_SUPPLY_SERIAL_NUMBER=01B-Z171221004149Z
        """;

    private const string Mains = """
        DEVTYPE=power_supply
        POWER_SUPPLY_NAME=ACAD
        POWER_SUPPLY_TYPE=Mains
        POWER_SUPPLY_ONLINE=0
        POWER_SUPPLY_TYPE=Mains
        """;

    private static string EnergyBattery(string status, int capacity, long now, long full, long power) =>
        $"""
        POWER_SUPPLY_NAME=BAT0
        POWER_SUPPLY_TYPE=Battery
        POWER_SUPPLY_STATUS={status}
        POWER_SUPPLY_PRESENT=1
        POWER_SUPPLY_POWER_NOW={power}
        POWER_SUPPLY_ENERGY_FULL={full}
        POWER_SUPPLY_ENERGY_NOW={now}
        POWER_SUPPLY_CAPACITY={capacity}
        """;

    [Test]
    public void Reads_a_discharging_charge_based_battery_with_time_to_empty()
    {
        var reading = PowerSupplyBatteryParser.Parse([Mains, ChargeBattery]);

        Assert.That(reading, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(80));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
            Assert.That(reading.TimeToEmpty, Is.EqualTo(TimeSpan.FromHours(1997000.0 / 973000)));
            Assert.That(reading.TimeToFull, Is.Null);
        }
    }

    [Test]
    public void Reads_a_charging_energy_based_battery_with_time_to_full()
    {
        var reading = PowerSupplyBatteryParser.Parse(
            [EnergyBattery("Charging", 50, 25_000_000, 50_000_000, 12_500_000)]
        );

        Assert.That(reading, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(50));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Charging));
            Assert.That(reading.TimeToFull, Is.EqualTo(TimeSpan.FromHours(2)));
            Assert.That(reading.TimeToEmpty, Is.Null);
        }
    }

    [Test]
    public void A_zero_rate_leaves_the_time_empty()
    {
        var reading = PowerSupplyBatteryParser.Parse([EnergyBattery("Discharging", 50, 25, 50, 0)]);

        Assert.That(reading?.TimeToEmpty, Is.Null);
    }

    [Test]
    public void A_negative_discharge_current_is_read_as_its_magnitude()
    {
        var reading = PowerSupplyBatteryParser.Parse(
            [ChargeBattery.Replace("CURRENT_NOW=973000", "CURRENT_NOW=-973000", StringComparison.Ordinal)]
        );

        Assert.That(reading?.TimeToEmpty, Is.EqualTo(TimeSpan.FromHours(1997000.0 / 973000)));
    }

    [TestCase("Full", 100, BatteryStatus.Full)]
    [TestCase("Not charging", 100, BatteryStatus.Full)]
    [TestCase("Not charging", 80, BatteryStatus.Unknown)]
    [TestCase("Unknown", 64, BatteryStatus.Unknown)]
    public void Maps_the_kernel_status(string status, int capacity, BatteryStatus expected)
    {
        var reading = PowerSupplyBatteryParser.Parse([EnergyBattery(status, capacity, capacity, 100, 0)]);

        Assert.That(reading?.Status, Is.EqualTo(expected));
    }

    [Test]
    public void Two_batteries_combine_into_one_reading_weighted_by_capacity()
    {
        var reading = PowerSupplyBatteryParser.Parse(
            [
                EnergyBattery("Discharging", 100, 20_000_000, 20_000_000, 5_000_000),
                EnergyBattery("Unknown", 25, 15_000_000, 60_000_000, 5_000_000),
            ]
        );

        Assert.That(reading, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(44));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
            Assert.That(reading.TimeToEmpty, Is.EqualTo(TimeSpan.FromHours(3.5)));
        }
    }

    [Test]
    public void Without_a_capacity_the_level_comes_from_the_charge()
    {
        var reading = PowerSupplyBatteryParser.Parse(
            [ChargeBattery.Replace("POWER_SUPPLY_CAPACITY=80\n", "", StringComparison.Ordinal)]
        );

        Assert.That(reading?.Percent, Is.EqualTo(80));
    }

    [Test]
    public void A_desktop_with_only_mains_has_no_battery()
    {
        Assert.That(PowerSupplyBatteryParser.Parse([Mains]), Is.Null);
    }

    [Test]
    public void An_absent_battery_or_a_peripheral_is_not_the_computer_battery()
    {
        var absent = ChargeBattery.Replace("PRESENT=1", "PRESENT=0", StringComparison.Ordinal);
        var mouse = """
            POWER_SUPPLY_NAME=hidpp_battery_0
            POWER_SUPPLY_TYPE=Battery
            POWER_SUPPLY_SCOPE=Device
            POWER_SUPPLY_STATUS=Discharging
            POWER_SUPPLY_PRESENT=1
            POWER_SUPPLY_CAPACITY=55
            """;

        Assert.That(PowerSupplyBatteryParser.Parse([absent, mouse]), Is.Null);
    }
}

[TestFixture]
public sealed class BlueZDeviceParserTests
{
    // The busctl --json=short shape of GetManagedObjects. A connected device with Battery1 is constructed from
    // that shape until one is captured from real hardware.
    private const string Objects = """
        {"type":"a{oa{sa{sv}}}","data":[{
          "/org/bluez":{"org.bluez.AgentManager1":{}},
          "/org/bluez/hci0":{"org.bluez.Adapter1":{"Name":{"type":"s","data":"laptop"},"Powered":{"type":"b","data":true}}},
          "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_01":{
            "org.bluez.Device1":{"Name":{"type":"s","data":"WH-1000XM4"},"Alias":{"type":"s","data":"My Headphones"},"Connected":{"type":"b","data":true}},
            "org.bluez.Battery1":{"Percentage":{"type":"y","data":70},"Source":{"type":"s","data":"HFP"}}},
          "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_02":{
            "org.bluez.Device1":{"Name":{"type":"s","data":"Speaker"},"Connected":{"type":"b","data":true}}},
          "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_03":{
            "org.bluez.Device1":{"Alias":{"type":"s","data":"Old Mouse"},"Connected":{"type":"b","data":false}},
            "org.bluez.Battery1":{"Percentage":{"type":"y","data":12}}}
        }]}
        """;

    [Test]
    public void Lists_connected_devices_by_alias_with_their_battery()
    {
        var devices = BlueZDeviceParser.ParseConnected(Objects);

        Assert.That(
            devices,
            Is.EqualTo(new (string, string?)[] { ("My Headphones", "70"), ("Speaker", null) })
        );
    }

    [Test]
    public void An_empty_object_tree_lists_nothing()
    {
        Assert.That(BlueZDeviceParser.ParseConnected("""{"type":"a{oa{sa{sv}}}","data":[{}]}"""), Is.Empty);
    }

    [Test]
    public async Task Concurrent_reads_share_one_busctl_call()
    {
        var calls = 0;
        using var reader = new BlueZBatteryReader(async ct =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50, ct);
            return Objects;
        });

        var results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => reader.ReadRawAsync("My Headphones", CancellationToken.None))
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(results, Is.All.EqualTo("70"));
        }
    }
}

[TestFixture]
public sealed class LinuxHidPathTests
{
    private const string Path =
        "/sys/devices/pci0000:00/0000:00:14.0/usb1/1-5/1-5.3/1-5.3:1.{n}/0003:1532:00B7.0002/hidraw/hidraw1";

    private static HidCandidate Candidate(string path, string? serial) =>
        new(path, 0x1532, 0x00B7, null, "Mouse", 91, serial);

    [Test]
    public void Reads_the_interface_number_from_a_sysfs_path()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HidSharpTransport.ParseInterfaceNumber(Path.Replace("{n}", "0")), Is.EqualTo(0));
            Assert.That(HidSharpTransport.ParseInterfaceNumber(Path.Replace("{n}", "2")), Is.EqualTo(2));
            Assert.That(HidSharpTransport.ParseInterfaceNumber(Path.Replace("{n}", "10")), Is.EqualTo(10));
        }
    }

    [Test]
    public void The_feature_ioctls_carry_the_report_length()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LinuxHidraw.Request(0x06, 91), Is.EqualTo((nuint)0xC05B4806));
            Assert.That(LinuxHidraw.Request(0x07, 91), Is.EqualTo((nuint)0xC05B4807));
            Assert.Throws<ArgumentOutOfRangeException>(() => LinuxHidraw.Request(0x06, 0x4000));
            Assert.Throws<ArgumentOutOfRangeException>(() => LinuxHidraw.Request(0x06, 0));
        }
    }

    [Test]
    public void The_hidraw_node_comes_from_the_end_of_the_sysfs_path()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LinuxHidraw.DeviceNode(Path.Replace("{n}", "0")), Is.EqualTo("/dev/hidraw1"));
            Assert.Throws<InvalidOperationException>(() => LinuxHidraw.DeviceNode("/sys/devices/x/input0"));
        }
    }

    [Test]
    public void A_bluetooth_hid_device_has_no_interface_number()
    {
        Assert.That(
            HidSharpTransport.ParseInterfaceNumber(
                "/sys/devices/virtual/misc/uhid/0005:046D:B023.0007/hidraw/hidraw6"
            ),
            Is.Null
        );
    }

    [Test]
    public void Interfaces_of_one_unit_share_a_key_and_a_second_port_does_not()
    {
        var unitA0 = HidFamily.PhysicalUnitKey(Candidate(Path.Replace("{n}", "0"), "000000000000"));
        var unitA2 = HidFamily.PhysicalUnitKey(Candidate(Path.Replace("{n}", "2"), "000000000000"));
        var unitB0 = HidFamily.PhysicalUnitKey(
            Candidate(Path.Replace("1-5.3", "1-5.4").Replace("{n}", "0"), "000000000000")
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unitA0, Is.EqualTo(unitA2));
            Assert.That(unitA0, Is.EqualTo("l:/sys/devices/pci0000:00/0000:00:14.0/usb1/1-5/1-5.3"));
            Assert.That(unitA0, Is.Not.EqualTo(unitB0));
        }
    }
}

[TestFixture]
public sealed class LinuxUdevRuleTests
{
    private static string RuleFile() => RepositoryFile("packaging", "linux", "70-device-battery-info.rules");

    private static string RepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
        {
            var file = System.IO.Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(file))
            {
                // A Windows checkout ends lines with CRLF, which would leave a '\r' on every split line.
                return File.ReadAllText(file).ReplaceLineEndings("\n");
            }
        }

        throw new FileNotFoundException($"{string.Join('/', parts)} not found above the test directory.");
    }

    private static IEnumerable<(int VendorId, int ProductId)> SupportedUsbIds() =>
        typeof(HidProtocol)
            .Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false } && t.IsAssignableTo(typeof(HidProtocol)))
            .Select(t => (HidProtocol)Activator.CreateInstance(t, nonPublic: true)!)
            .SelectMany(p => p.Devices.SelectMany(d => d.ProductIds.Select(id => (p.VendorId, id))));

    // Without its line a model reads as not connected on Linux.
    [Test]
    public void Every_supported_product_id_has_a_udev_line()
    {
        var rule = RuleFile();
        var ids = SupportedUsbIds().ToArray();

        Assert.That(ids, Is.Not.Empty);
        Assert.That(
            ids.Where(id =>
                !rule.Contains(
                    $"ATTRS{{idVendor}}==\"{id.VendorId:x4}\", ATTRS{{idProduct}}==\"{id.ProductId:x4}\"",
                    StringComparison.Ordinal
                )
            ),
            Is.Empty
        );
    }

    // A vendor-wide grant would also expose that vendor's keyboards to every program the user runs.
    [Test]
    public void The_rule_never_grants_a_whole_vendor()
    {
        var grants = RuleFile()
            .Split('\n')
            .Where(line => line.Contains("uaccess", StringComparison.Ordinal) && !line.StartsWith('#'));

        Assert.That(
            grants.Where(line => !line.Contains("idProduct", StringComparison.Ordinal) && !line.Contains("KERNELS", StringComparison.Ordinal)),
            Is.Empty
        );
    }

    // The guide prints the rule one single-quoted line at a time (fish has no heredocs) instead of
    // downloading it as root, so the two must not drift apart.
    [Test]
    public void The_setup_guide_carries_the_rule_verbatim()
    {
        var guide = RepositoryFile("docs", "linux-setup.md");
        var lines = RuleFile().TrimEnd('\n').Split('\n');

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines.Where(line => !guide.Contains($"'{line}'", StringComparison.Ordinal)), Is.Empty);
            Assert.That(guide, Does.Not.Contain("curl"));
        }
    }
}
