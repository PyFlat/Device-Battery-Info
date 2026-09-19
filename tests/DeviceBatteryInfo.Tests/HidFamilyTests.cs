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

        public Task<byte[]> ExchangeAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
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

            // A minimal completed frame carrying value 180 at the response-value index (see
            // RazerProtocol) - the exact command being asked does not matter here,
            // the protocol encoding itself is covered by BatterySourceParsingTests.
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

/// <summary>Talks to a real Razer DeathAdder V3 Pro over hid.dll. Explicit: only runs when asked
/// (<c>dotnet test --filter Category=Hardware</c>) and needs the mouse dongle plugged in.</summary>
[TestFixture]
[Explicit]
[Category("Hardware")]
public sealed class DeathAdderV3ProHardwareTests
{
    [Test]
    public async Task Reads_battery_from_the_connected_dongle()
    {
        Assume.That(OperatingSystem.IsWindows());

        var transport = new HidSharpTransport(Serilog.Core.Logger.None);
        var candidates = transport.FindCandidates(
            0x1532,
            0x00B7,
            interfaceNumber: null,
            new RazerProtocol().ReportLength
        );
        TestContext.Out.WriteLine($"candidates: {candidates.Count}");
        foreach (var candidate in candidates)
        {
            TestContext.Out.WriteLine(
                $"  iface {candidate.InterfaceNumber} len {candidate.FeatureReportLength} {candidate.ProductName} {candidate.Path}"
            );
        }

        Assert.That(
            candidates,
            Is.Not.Empty,
            "No 1532:00B7 HID collection with a >=90 byte feature report."
        );

        byte? level = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var raw = await RazerProtocol.QueryAsync(
                    new HidChannel(transport, candidate.Path),
                    RazerProtocol.CommandBatteryLevel,
                    CancellationToken.None
                );
                var charging = await RazerProtocol.QueryAsync(
                    new HidChannel(transport, candidate.Path),
                    RazerProtocol.CommandChargingStatus,
                    CancellationToken.None
                );
                TestContext.Out.WriteLine(
                    $"  -> iface {candidate.InterfaceNumber} answered: raw {raw} = {RazerProtocol.PercentFromRaw(raw)}% charging={charging == 1}"
                );
                level ??= raw;
            }
            catch (Exception exception)
            {
                TestContext.Out.WriteLine(
                    $"  -> iface {candidate.InterfaceNumber} failed: {exception.Message}"
                );
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(level, Is.Not.Null, "No candidate answered the feature report.");
            Assert.That(
                RazerProtocol.PercentFromRaw(level!.Value),
                Is.InRange(1, 100)
            );
        }
    }
}
