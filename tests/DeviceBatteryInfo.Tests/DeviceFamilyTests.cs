using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class DeviceFamilyTests
{
    private sealed class DemoFamily : SimpleDeviceFamily
    {
        public bool Connected { get; set; } = true;

        public override IReadOnlyList<DeviceModel> Models { get; } =
            [new("Acme", "Wireless Mouse 3", BatterySourceKind.Mouse)];

        protected override ValueTask<BatteryReading> ReadAsync(
            DeviceModel model,
            BatterySlot slot,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(new BatteryReading { Percent = 42 });

        protected override ValueTask<bool> IsPresentAsync(
            DeviceModel model,
            BatterySlot slot,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(Connected);
    }

    [Test]
    public void A_model_derives_its_ids_from_brand_and_name()
    {
        var model = new DeviceModel("Razer", "DeathAdder V3 Pro");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Id, Is.EqualTo("razer-deathadder-v3-pro"));
            Assert.That(model.BrandId, Is.EqualTo("razer"));
        }
    }

    [Test]
    public void The_catalog_lists_the_models_of_every_family()
    {
        var catalog = new DeviceModelCatalog([new DemoFamily()]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Brands.Select(b => b.Id), Is.EqualTo(["acme"]));
            Assert.That(catalog.ById("acme-wireless-mouse-3"), Is.Not.Null);
        }
    }

    [Test]
    public async Task A_simple_family_needs_no_discovery_code_to_produce_a_source()
    {
        var devices = new DeviceCatalog();
        devices.Set(
            [
                new BatterySlot(
                    "desk-mouse",
                    "Desk mouse",
                    BatterySourceKind.Mouse,
                    DeviceType.Catalog,
                    CatalogDeviceId: "acme-wireless-mouse-3"
                ),
            ]
        );

        var sources = await new DeviceFamilyProvider([new DemoFamily()], devices).DiscoverAsync(
            CancellationToken.None
        );

        var reading = await sources.Single().ReadAsync(CancellationToken.None);
        Assert.That(reading.Percent, Is.EqualTo(42));
    }

    [Test]
    public async Task A_device_of_another_family_is_not_handed_to_this_one()
    {
        var devices = new DeviceCatalog();
        devices.Set(
            [
                new BatterySlot(
                    "x",
                    "X",
                    BatterySourceKind.Mouse,
                    DeviceType.Catalog,
                    CatalogDeviceId: "razer-deathadder-v3-pro"
                ),
            ]
        );

        Assert.That(
            await new DeviceFamilyProvider([new DemoFamily()], devices).DiscoverAsync(
                CancellationToken.None
            ),
            Is.Empty
        );
    }

    [Test]
    public async Task A_device_that_is_not_present_is_left_out_of_the_poll()
    {
        var devices = new DeviceCatalog();
        devices.Set(
            [
                new BatterySlot(
                    "desk-mouse",
                    "Desk mouse",
                    BatterySourceKind.Mouse,
                    DeviceType.Catalog,
                    CatalogDeviceId: "acme-wireless-mouse-3"
                ),
            ]
        );
        var family = new DemoFamily { Connected = false };
        var provider = new DeviceFamilyProvider([family], devices);

        Assert.That(await provider.DiscoverAsync(CancellationToken.None), Is.Empty);

        family.Connected = true;
        Assert.That(await provider.DiscoverAsync(CancellationToken.None), Has.Count.EqualTo(1));
    }
}
