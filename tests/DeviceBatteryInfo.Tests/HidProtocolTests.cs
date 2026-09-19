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
}
