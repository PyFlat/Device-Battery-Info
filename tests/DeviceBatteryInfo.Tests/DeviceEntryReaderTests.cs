using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.ConfigFlow;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class DeviceEntryReaderTests
{
    private sealed class FakeConfig(
        params (Guid Id, string Title, Dictionary<string, string?> Data)[] entries
    ) : IIntegrationConfig
    {
        public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(
                entries.Select(e => new ConfigEntrySnapshot(e.Id, e.Title)).ToArray()
            );

        public Task<string?> GetStringAsync(
            Guid entryId,
            string key,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(entries.First(e => e.Id == entryId).Data.GetValueOrDefault(key));

        public Task<string?> GetSecretAsync(
            Guid entryId,
            string key,
            CancellationToken cancellationToken = default
        ) => GetStringAsync(entryId, key, cancellationToken);

        public Task SetStringAsync(
            Guid entryId,
            string key,
            string? value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetSecretAsync(
            Guid entryId,
            string key,
            string value,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    [Test]
    public async Task No_entries_yields_an_empty_list()
    {
        var slots = await DeviceEntryReader.ReadAsync(new FakeConfig(), TestModels.Catalog(), CancellationToken.None);

        Assert.That(slots, Is.Empty);
    }

    [Test]
    public async Task Each_type_maps_to_its_slot_shape()
    {
        var config = new FakeConfig(
            (
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "Phone",
                new()
                {
                    ["type"] = "adb-phone",
                    ["name"] = "Phone",
                    ["adbAddress"] = " 10.0.0.9:5555 ",
                    ["adbExecutable"] = "adb",
                }
            ),
            (
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                "Buds",
                new()
                {
                    ["type"] = "bluetooth",
                    ["name"] = "Buds",
                    ["bluetoothName"] = "soundcore Buds",
                    ["bluetoothKind"] = "earbuds",
                }
            ),
            (
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                "Mouse",
                new()
                {
                    ["type"] = "razer-deathadder-v3-pro",
                    ["name"] = "Mouse",
                }
            )
        );

        var slots = (await DeviceEntryReader.ReadAsync(config, TestModels.Catalog(), CancellationToken.None)).ToArray();

        Assert.That(slots.Select(s => s.Id), Is.EqualTo(["phone", "buds", "mouse"]));

        var phone = slots.Single(s => s.Type == DeviceType.AdbPhone);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(phone.AdbAddress, Is.EqualTo("10.0.0.9:5555"));
            Assert.That(phone.Kind, Is.EqualTo(BatterySourceKind.Phone));
        }

        var buds = slots.Single(s => s.Type == DeviceType.Bluetooth);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(buds.BluetoothFriendlyName, Is.EqualTo("soundcore Buds"));
            Assert.That(buds.Kind, Is.EqualTo(BatterySourceKind.Earbuds));
        }

        var mouse = slots.Single(s => s.Type == DeviceType.Catalog);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mouse.CatalogDeviceId, Is.EqualTo("razer-deathadder-v3-pro"));
            Assert.That(mouse.Kind, Is.EqualTo(BatterySourceKind.Mouse));
        }
    }

    [Test]
    public async Task A_stored_kind_sets_the_icon_of_adb_and_bluetooth_devices()
    {
        var config = new FakeConfig(
            (
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "Quest",
                new()
                {
                    ["type"] = "adb-phone",
                    ["name"] = "Quest",
                    ["adbAddress"] = "10.0.0.9:5555",
                    ["kind"] = "vr-headset",
                }
            ),
            (
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                "Go 4",
                new()
                {
                    ["type"] = "bluetooth",
                    ["name"] = "Go 4",
                    ["bluetoothName"] = "JBL Go 4",
                    ["kind"] = "speaker",
                    ["bluetoothKind"] = "headset",
                }
            )
        );

        var slots = await DeviceEntryReader.ReadAsync(config, TestModels.Catalog(), CancellationToken.None);

        Assert.That(
            slots.Select(s => s.Kind),
            Is.EqualTo([BatterySourceKind.VrHeadset, BatterySourceKind.Speaker])
        );
    }

    [Test]
    public async Task A_catalog_mouse_resolves_its_model_from_the_catalog_id()
    {
        var config = new FakeConfig(
            (
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "Mouse",
                new()
                {
                    ["type"] = "razer-deathadder-v3-pro",
                    ["name"] = "Mouse",
                    ["catalogDevice"] = "razer-deathadder-v3-pro",
                }
            )
        );

        var mouse = (await DeviceEntryReader.ReadAsync(config, TestModels.Catalog(), CancellationToken.None)).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mouse.Type, Is.EqualTo(DeviceType.Catalog));
            Assert.That(mouse.CatalogDeviceId, Is.EqualTo("razer-deathadder-v3-pro"));
        }
    }

    [Test]
    public async Task Duplicate_names_get_distinct_ids()
    {
        var config = new FakeConfig(
            (
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "Phone",
                new()
                {
                    ["type"] = "adb-phone",
                    ["name"] = "Phone",
                    ["adbAddress"] = "a:1",
                }
            ),
            (
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                "Phone",
                new()
                {
                    ["type"] = "adb-phone",
                    ["name"] = "Phone",
                    ["adbAddress"] = "b:2",
                }
            )
        );

        var slots = (await DeviceEntryReader.ReadAsync(config, TestModels.Catalog(), CancellationToken.None)).ToArray();

        Assert.That(slots.Select(s => s.Id), Is.EqualTo(["phone", "phone-2"]));
    }
}
