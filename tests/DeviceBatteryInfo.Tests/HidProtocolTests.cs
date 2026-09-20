using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Sources.Hid;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class HidProtocolTests
{
    private sealed class AcmeProtocol() : HidProtocol("Acme", vendorId: 0x1234, reportLength: 8)
    {
        public override IReadOnlyList<HidDeviceInfo> Devices { get; } = [new("Air Mouse", 0x0001)];

        public override async Task<BatteryReading> ReadAsync(
            HidChannel channel,
            HidDeviceInfo device,
            CancellationToken cancellationToken
        )
        {
            var response = await channel.ExchangeAsync(
                [0x00, 0xB0],
                r => r.Length > 2 && r[1] == 0xB0,
                cancellationToken
            );
            return BatteryReading.FromPercent(response[2]);
        }
    }

    private sealed class FakeTransport : IHidTransport
    {
        private static readonly HidCandidate Device = new(
            @"\?\hid#vid_1234&pid_0001&mi_01#8&aaaa&0&0000#{guid}",
            0x1234,
            0x0001,
            1,
            "Acme Air Mouse",
            8
        );

        public IReadOnlyList<HidCandidate> FindCandidates(
            int vendorId,
            int productId,
            int? interfaceNumber,
            int minFeatureReportLength
        ) => vendorId == 0x1234 && productId == 0x0001 ? [Device] : [];

        public IReadOnlyList<HidCandidate> ListFeatureReportDevices() => [Device];

        public Task<byte[]> ExchangeReportsAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<byte[]> ExchangeAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            CancellationToken cancellationToken
        ) => Task.FromResult(new byte[] { 0x00, request[1], 77 });
    }

    [Test]
    public async Task A_protocol_is_only_its_devices_and_one_read()
    {
        var devices = new DeviceCatalog();
        devices.Set(
            [
                new BatterySlot(
                    "air",
                    "Air",
                    BatterySourceKind.Mouse,
                    DeviceType.Catalog,
                    CatalogDeviceId: "acme-air-mouse"
                ),
            ]
        );
        var family = new HidFamily([new AcmeProtocol()], new FakeTransport(), Serilog.Core.Logger.None);

        var sources = await new DeviceFamilyProvider([family], devices).DiscoverAsync(
            CancellationToken.None
        );

        var reading = await sources.Single().ReadAsync(CancellationToken.None);
        Assert.That(reading.Percent, Is.EqualTo(77));
    }

    private sealed class DongleOrCableProtocol()
        : HidProtocol("Acme", vendorId: 0x1234, reportLength: 8)
    {
        public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
            [new("Wireless Mouse", 0x00AB, 0x00AA)];

        public override async Task<BatteryReading> ReadAsync(
            HidChannel channel,
            HidDeviceInfo device,
            CancellationToken cancellationToken
        )
        {
            var response = await channel.ExchangeAsync([0x00, 0xB0], _ => true, cancellationToken);
            return BatteryReading.FromPercent(response[2]);
        }
    }

    private sealed class DongleAndCableTransport : IHidTransport
    {
        private static readonly HidCandidate Dongle = new(@"dongle", 0x1234, 0x00AB, 0, "Dongle", 8);
        private static readonly HidCandidate Cable = new(@"cable", 0x1234, 0x00AA, 0, "Cable", 8);

        public bool DongleReachesMouse { get; set; } = true;

        public IReadOnlyList<HidCandidate> FindCandidates(
            int vendorId,
            int productId,
            int? interfaceNumber,
            int minFeatureReportLength
        ) =>
            productId switch
            {
                0x00AB => [Dongle],
                0x00AA => [Cable],
                _ => [],
            };

        public IReadOnlyList<HidCandidate> ListFeatureReportDevices() => [Dongle, Cable];

        public Task<byte[]> ExchangeReportsAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<byte[]> ExchangeAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            CancellationToken cancellationToken
        ) =>
            devicePath == "dongle" && !DongleReachesMouse
                ? throw new InvalidOperationException("The dongle cannot reach the mouse.")
                : Task.FromResult(new byte[] { 0x00, 0x00, (byte)(devicePath == "dongle" ? 50 : 80) });
    }

    private static DeviceCatalog CatalogWithMouse()
    {
        var devices = new DeviceCatalog();
        devices.Set(
            [
                new BatterySlot(
                    "mouse",
                    "Mouse",
                    BatterySourceKind.Mouse,
                    DeviceType.Catalog,
                    CatalogDeviceId: "acme-wireless-mouse"
                ),
            ]
        );
        return devices;
    }

    [Test]
    public async Task A_mouse_on_its_cable_is_found_when_its_dongle_cannot_reach_it()
    {
        var transport = new DongleAndCableTransport { DongleReachesMouse = false };
        var family = new HidFamily([new DongleOrCableProtocol()], transport, Serilog.Core.Logger.None);

        var sources = await new DeviceFamilyProvider([family], CatalogWithMouse()).DiscoverAsync(
            CancellationToken.None
        );

        var reading = await sources.Single().ReadAsync(CancellationToken.None);
        Assert.That(reading.Percent, Is.EqualTo(80));
    }

    [Test]
    public async Task Switching_from_dongle_to_cable_is_picked_up_without_a_restart()
    {
        var transport = new DongleAndCableTransport();
        var provider = new DeviceFamilyProvider(
            [new HidFamily([new DongleOrCableProtocol()], transport, Serilog.Core.Logger.None)],
            CatalogWithMouse()
        );

        var wireless = (await provider.DiscoverAsync(CancellationToken.None)).Single();
        Assert.That((await wireless.ReadAsync(CancellationToken.None)).Percent, Is.EqualTo(50));

        transport.DongleReachesMouse = false;
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await wireless.ReadAsync(CancellationToken.None)
        );

        var wired = (await provider.DiscoverAsync(CancellationToken.None)).Single();
        Assert.That((await wired.ReadAsync(CancellationToken.None)).Percent, Is.EqualTo(80));
    }
}
