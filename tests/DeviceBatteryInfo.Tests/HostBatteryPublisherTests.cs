using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.HostBatteries;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Batteries;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class HostBatteryPublisherTests
{
    private static readonly Guid MouseEntry = Guid.Parse("6f1c2b0e-8d4a-4a51-9e57-0c1f3b2a9d11");
    private static readonly Guid PhoneEntry = Guid.Parse("0b9e7d3c-2a14-4f6e-8c35-71d2e4a6b822");
    private static readonly string MouseId = HostBatteryDevices.IdOf(MouseEntry);
    private static readonly string PhoneId = HostBatteryDevices.IdOf(PhoneEntry);

    private sealed class FakeSource(string id) : IBatterySource
    {
        public string Id => id;

        public string DisplayName => id;

        public BatterySourceKind Kind => BatterySourceKind.Other;

        public ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(BatteryReading.Unavailable);
    }

    private sealed class Setup
    {
        public DeviceCatalog Catalog { get; } = new();

        public BatteryRegistry Registry { get; } = new(TimeProvider.System);

        public FakeBatteryProviderContext Context { get; } = new();

        public HostBatteryPublisher Publisher { get; }

        public Setup()
        {
            Publisher = new HostBatteryPublisher(Catalog, Registry, Serilog.Core.Logger.None);
            Catalog.Set(
                [
                    new BatterySlot(
                        "mouse",
                        "Mouse",
                        BatterySourceKind.Mouse,
                        DeviceType.Catalog,
                        EntryId: MouseEntry
                    ),
                    new BatterySlot(
                        "phone",
                        "Phone",
                        BatterySourceKind.Phone,
                        DeviceType.AdbPhone,
                        EntryId: PhoneEntry
                    ),
                ]
            );
        }

        public void Read(string slotId, BatteryReading reading) =>
            Registry.Update(new FakeSource(slotId), reading);
    }

    [Test]
    public async Task Publishes_every_configured_device_in_one_call()
    {
        var setup = new Setup();
        setup.Read("mouse", BatteryReading.FromPercent(80, BatteryStatus.Discharging));
        setup.Read("phone", BatteryReading.FromPercent(45, BatteryStatus.Charging));

        await setup.Publisher.AttachAsync(setup.Context, CancellationToken.None);

        var mouse = setup.Context.Devices[MouseId];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(setup.Context.Publishes, Has.Count.EqualTo(1));
            Assert.That(setup.Context.Publishes[0], Has.Count.EqualTo(2));
            Assert.That(mouse.Name, Is.EqualTo("Mouse"));
            Assert.That(mouse.Kind, Is.EqualTo(BatteryDeviceKinds.Mouse));
            Assert.That(mouse.LevelPercent, Is.EqualTo(80));
            Assert.That(mouse.ChargeState, Is.EqualTo(BatteryChargeState.Discharging));
            Assert.That(mouse.IsConnected, Is.True);
            Assert.That(
                setup.Context.Devices[PhoneId].ChargeState,
                Is.EqualTo(BatteryChargeState.Charging)
            );
        }
    }

    [Test]
    public async Task Publishes_again_only_when_something_changed()
    {
        var setup = new Setup();
        setup.Read("mouse", BatteryReading.FromPercent(80, BatteryStatus.Discharging));
        await setup.Publisher.AttachAsync(setup.Context, CancellationToken.None);

        await setup.Publisher.SyncAsync(CancellationToken.None);
        Assert.That(setup.Context.Publishes, Has.Count.EqualTo(1));

        setup.Read("mouse", BatteryReading.FromPercent(79, BatteryStatus.Discharging));
        await setup.Publisher.SyncAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(setup.Context.Publishes, Has.Count.EqualTo(2));
            Assert.That(setup.Context.Devices[MouseId].LevelPercent, Is.EqualTo(79));
        }
    }

    [Test]
    public async Task A_renamed_device_keeps_its_id()
    {
        var setup = new Setup();
        await setup.Publisher.AttachAsync(setup.Context, CancellationToken.None);

        setup.Catalog.Set(
            [
                new BatterySlot(
                    "office-mouse",
                    "Office mouse",
                    BatterySourceKind.Mouse,
                    DeviceType.Catalog,
                    EntryId: MouseEntry
                ),
                setup.Catalog.Devices[1],
            ]
        );
        await setup.Publisher.SyncAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(setup.Context.RemovedIds, Is.Empty);
            Assert.That(setup.Context.Devices[MouseId].Name, Is.EqualTo("Office mouse"));
        }
    }

    [Test]
    public async Task A_deleted_entry_is_removed_for_good()
    {
        var setup = new Setup();
        await setup.Publisher.AttachAsync(setup.Context, CancellationToken.None);

        setup.Catalog.Set([setup.Catalog.Devices[1]]);
        await setup.Publisher.SyncAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(setup.Context.RemovedIds, Is.EqualTo(new[] { MouseId }));
            Assert.That(setup.Context.Devices.Keys, Is.EqualTo(new[] { PhoneId }));
        }
    }

    [Test]
    public async Task A_device_out_of_reach_keeps_its_last_level_as_disconnected()
    {
        var setup = new Setup();
        setup.Read("mouse", BatteryReading.FromPercent(60, BatteryStatus.Discharging));
        await setup.Publisher.AttachAsync(setup.Context, CancellationToken.None);

        setup.Registry.Retain(new HashSet<string>());
        await setup.Publisher.SyncAsync(CancellationToken.None);

        var mouse = setup.Context.Devices[MouseId];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mouse.IsConnected, Is.False);
            Assert.That(mouse.LevelPercent, Is.EqualTo(60));
        }
    }

    [Test]
    public async Task A_stale_reading_is_published_as_disconnected()
    {
        var setup = new Setup();
        setup.Read("mouse", BatteryReading.FromPercent(60, BatteryStatus.Discharging));
        setup.Registry.RecordFailure(new FakeSource("mouse"), staleAfter: 1);

        await setup.Publisher.AttachAsync(setup.Context, CancellationToken.None);

        var mouse = setup.Context.Devices[MouseId];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mouse.IsConnected, Is.False);
            Assert.That(mouse.LevelPercent, Is.EqualTo(60));
        }
    }

    [Test]
    public async Task A_device_never_read_is_published_without_a_level()
    {
        var setup = new Setup();

        await setup.Publisher.AttachAsync(setup.Context, CancellationToken.None);

        var phone = setup.Context.Devices[PhoneId];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(phone.IsConnected, Is.False);
            Assert.That(phone.LevelPercent, Is.Null);
        }
    }

    [Test]
    public async Task Nothing_is_published_after_shutdown()
    {
        var setup = new Setup();
        await setup.Publisher.AttachAsync(setup.Context, CancellationToken.None);
        await setup.Publisher.DetachAsync(CancellationToken.None);

        setup.Read("mouse", BatteryReading.FromPercent(10, BatteryStatus.Discharging));
        await setup.Publisher.SyncAsync(CancellationToken.None);

        Assert.That(setup.Context.Publishes, Has.Count.EqualTo(1));
    }

    [TestCase(BatterySourceKind.System, BatteryDeviceKinds.Laptop)]
    [TestCase(BatterySourceKind.Headset, BatteryDeviceKinds.Headphones)]
    [TestCase(BatterySourceKind.Earbuds, BatteryDeviceKinds.Headphones)]
    [TestCase(BatterySourceKind.Pen, BatteryDeviceKinds.Other)]
    [TestCase(BatterySourceKind.VrHeadset, BatteryDeviceKinds.Other)]
    public void Kinds_without_a_host_counterpart_map_to_the_closest_one(
        BatterySourceKind kind,
        string expected
    ) => Assert.That(HostBatteryDevices.KindOf(kind), Is.EqualTo(expected));

    [Test]
    public void Every_kind_maps_to_a_kind_the_host_knows()
    {
        foreach (var kind in Enum.GetValues<BatterySourceKind>())
        {
            Assert.That(
                BatteryDeviceKinds.IsKnown(HostBatteryDevices.KindOf(kind)),
                Is.True,
                $"{kind}"
            );
        }
    }
}
