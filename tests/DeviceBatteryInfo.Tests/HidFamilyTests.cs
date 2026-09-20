using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.Razer;
using DeviceBatteryInfo.Sources;
using NUnit.Framework;
using Serilog;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class HidFamilyTests
{
    private sealed class FakeTransport(params HidCandidate[] candidates) : IHidTransport
    {
        public List<string> Queried { get; } = [];

        public IReadOnlyList<HidCandidate> FindCandidates(
            int vendorId,
            int productId,
            int? interfaceNumber,
            int minFeatureReportLength
        ) =>
            candidates
                .Where(c => interfaceNumber is null || c.InterfaceNumber == interfaceNumber)
                .Where(c => c.FeatureReportLength >= minFeatureReportLength)
                .ToArray();

        public IReadOnlyList<HidCandidate> ListFeatureReportDevices() => candidates;

        public Task<byte[]> ExchangeReportsAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<byte[]> ExchangeAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        )
        {
            Queried.Add(devicePath);
            // mi_00 is a HID collection that accepts the open but never answers the feature report;
            // the probe has to move on to the collection that does.
            if (devicePath.Contains("mi_00", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("HidD_GetFeature failed.");
            }

            // A minimal completed frame with the value 180 at the response index.
            var response = new byte[11];
            response[10] = 180;
            return Task.FromResult(response);
        }
    }

    // One HID collection. "unit" is the parent-instance token the provider groups on: the same value
    // for every collection of one physical mouse, a different value for a second identical mouse.
    private static HidCandidate Candidate(int iface, string unit = "0000") =>
        new(
            $@"\\?\hid#vid_1532&pid_00b7&mi_{iface:D2}#8&{unit}&0&0000#{{4d1e55b2-f16f-11cf-88cb-001111000030}}",
            0x1532,
            0x00B7,
            iface,
            "Razer DeathAdder V3 Pro",
            91
        );

    private static BatterySlot MouseSlot(string id, string name) =>
        new(
            id,
            name,
            BatterySourceKind.Mouse,
            DeviceType.Catalog,
            CatalogDeviceId: "razer-deathadder-v3-pro"
        );

    private static async Task<IReadOnlyList<IBatterySource>> DiscoverAsync(
        FakeTransport transport,
        params BatterySlot[] slots
    )
    {
        var catalog = new DeviceCatalog();
        catalog.Set(slots);
        var family = new HidFamily([new RazerProtocol()], transport, Serilog.Core.Logger.None);
        return await new DeviceFamilyProvider([family], catalog).DiscoverAsync(
            CancellationToken.None
        );
    }

    [Test]
    public async Task Probes_past_the_boot_interface_to_the_one_that_answers()
    {
        var transport = new FakeTransport(Candidate(0), Candidate(2));
        var sources = await DiscoverAsync(transport, MouseSlot("mouse", "Mouse"));

        Assert.That(sources, Has.Count.EqualTo(1));
        var reading = await sources[0].ReadAsync(CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                reading.Percent,
                Is.EqualTo(RazerProtocol.PercentFromRaw(180))
            );
            Assert.That(transport.Queried, Does.Contain(Candidate(2).Path));
        }
    }

    [Test]
    public async Task No_matching_device_yields_no_source()
    {
        Assert.That(await DiscoverAsync(new FakeTransport(), MouseSlot("mouse", "Mouse")), Is.Empty);
    }

    [Test]
    public async Task Two_entries_for_one_model_bind_to_distinct_units()
    {
        var transport = new FakeTransport(
            Candidate(0, "aaaa"),
            Candidate(2, "aaaa"),
            Candidate(0, "bbbb"),
            Candidate(2, "bbbb")
        );
        var sources = await DiscoverAsync(transport, MouseSlot("mouse-a", "Mouse A"), MouseSlot("mouse-b", "Mouse B"));
        Assert.That(sources.Select(s => s.Id), Is.EquivalentTo(new[] { "mouse-a", "mouse-b" }));

        foreach (var source in sources)
        {
            await source.ReadAsync(CancellationToken.None);
        }

        // Each entry resolved a path from a different physical unit, not both onto the first to answer.
        Assert.That(transport.Queried, Does.Contain(Candidate(2, "aaaa").Path));
        Assert.That(transport.Queried, Does.Contain(Candidate(2, "bbbb").Path));
    }

    [Test]
    public async Task Fewer_units_than_entries_leaves_the_extra_entry_without_a_source()
    {
        var transport = new FakeTransport(Candidate(0, "aaaa"), Candidate(2, "aaaa"));
        var sources = await DiscoverAsync(transport, MouseSlot("mouse-a", "Mouse A"), MouseSlot("mouse-b", "Mouse B"));

        Assert.That(sources.Select(s => s.Id), Is.EqualTo(["mouse-a"]));
    }
}
