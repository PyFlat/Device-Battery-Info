using DeviceBatteryInfo.Sources.Bluetooth;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class BluetoothPnpLevelsTests
{
    // Captured on Windows 11: only the Hands-Free AG node carries the level.
    private static readonly PnpNode[] SleepA30 =
    [
        new(@"BTHENUM\DEV_7CE913058466\B&815C426&0&BLUETOOTHDEVICE_7CE913058466", "soundcore Sleep A30", null),
        new(
            @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&000102B0_PID&0000\B&815C426&0&7CE913058466_C00000000",
            "soundcore Sleep A30",
            null
        ),
        new(
            @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&000102B0_PID&0000\B&815C426&0&7CE913058466_C00000000",
            "soundcore Sleep A30 Hands-Free AG",
            "100"
        ),
        new(
            @"BTHENUM\{0000110E-0000-1000-8000-00805F9B34FB}_VID&000102B0_PID&0000\B&815C426&0&7CE913058466_C00000000",
            "soundcore Sleep A30 AVRCP-Transport",
            null
        ),
    ];

    // A speaker with only A2DP and AVRCP: no node that could ever carry a level.
    private static readonly PnpNode[] PlainSpeaker =
    [
        new(@"BTHENUM\DEV_A1B2C3D4E5F6\B&815C426&0&BLUETOOTHDEVICE_A1B2C3D4E5F6", "JBL Go 4", null),
        new(
            @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&00010057_PID&0001\B&815C426&0&A1B2C3D4E5F6_C00000000",
            "JBL Go 4",
            null
        ),
    ];

    private static string? Level(IReadOnlyList<PnpNode> nodes, string name) =>
        BluetoothPnpLevels
            .ByName(nodes)
            .FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
            .RawBattery;

    [Test]
    public void The_root_name_gets_the_level_a_sibling_node_carries()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Level(SleepA30, "soundcore Sleep A30"), Is.EqualTo("100"));
            Assert.That(Level(SleepA30, "soundcore Sleep A30 Hands-Free AG"), Is.EqualTo("100"));
        }
    }

    [Test]
    public void The_bluetooth_base_uuid_is_not_mistaken_for_an_address()
    {
        // Both devices' SDP nodes end in 00805F9B34FB.
        Assert.That(Level([.. SleepA30, .. PlainSpeaker], "JBL Go 4"), Is.Null);
    }

    [Test]
    public void A_ble_node_wins_over_a_classic_one()
    {
        PnpNode[] nodes =
        [
            new(@"BTHENUM\DEV_112233445566\B&1&0&BLUETOOTHDEVICE_112233445566", "Buds", null),
            new(@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_X\B&1&0&112233445566_C00000000", "Buds Hands-Free AG", "60"),
            new(@"BTHLE\DEV_112233445566\7&1&0&112233445566", "Buds", "70"),
        ];

        Assert.That(Level(nodes, "Buds"), Is.EqualTo("70"));
    }

    [Test]
    public void Names_match_ignoring_case_like_get_pnpdevice()
    {
        Assert.That(Level(SleepA30, "SOUNDCORE sleep a30"), Is.EqualTo("100"));
    }

    [Test]
    public void The_picker_lists_root_names_that_can_carry_a_level()
    {
        var pickable = BluetoothPnpLevels.Pickable([.. PlainSpeaker, .. SleepA30]);

        Assert.That(pickable, Is.EqualTo(new[] { ("soundcore Sleep A30", (string?)"100") }));
    }

    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task Parallel_reads_share_one_walk_of_the_pnp_tree()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The reader is Windows only.");
            return;
        }

        var walks = 0;
        using var reader = new WindowsBluetoothBatteryReader(() =>
        {
            Interlocked.Increment(ref walks);
            return SleepA30;
        });

        var levels = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => reader.ReadRawAsync("soundcore Sleep A30", CancellationToken.None))
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(levels, Is.All.EqualTo("100"));
            Assert.That(walks, Is.EqualTo(1));
        }
    }
}
