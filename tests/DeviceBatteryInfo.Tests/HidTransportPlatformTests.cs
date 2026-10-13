using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.Logitech;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class HidTransportPlatformTests
{
    private const string MacPath = "IORegistryEntryID:100769C19";

    // ReportDescriptor of a G Pro X Wireless (046D:0ABA) interface 3, read from the IORegistry on macOS 26.
    private const string HeadsetDescriptor =
        "050c0901a101850109e909ea7501950215002501810275069501810375188103c00643ff0a0202a10185119513750826ff00"
        + "0902810009029100c00600ff0901a101090aa10285ff95017520090b170100008027ffffff7f8102c00922a102859009337508"
        + "150026ff008102093481020501093b751027ffff000081020600ff090aa102050a1901290a950a750826ff008102c0c00600ff"
        + "0922a1028590093395019102093491020501093b751027ffff000091020600ff090aa102050a1901290a950a750826ff009102"
        + "c0c00600ff0922a10285c4093395018102093481020501093b751027ffff000081020600ff090aa102050a1901293b953b7508"
        + "26ff008102c0c00600ff0922a10285c4093395019102093491020501093b751027ffff000091020600ff090aa102050a190129"
        + "3b953b750826ff009102c0c00600ff090ca10285ff0501093b9501b102093aa102050a1901293e953eb102c0c00600ff090ca1"
        + "0285e20501093b95018102093aa102050a1901293e953e8102c0c0c0";

    private sealed class FakeChannel(byte[]? response = null) : IFeatureChannel
    {
        private byte[]? _sent;

        public TaskCompletionSource Disposed { get; } = new();

        public byte[]? Sent => _sent;

        public void Set(byte[] report) => _sent = [.. report];

        public void Get(byte[] report) => (response ?? new byte[report.Length]).CopyTo(report, 0);

        public void Dispose() => Disposed.TrySetResult();
    }

    private static HidSharpTransport Transport(Func<string, IFeatureChannel> open, bool bound) =>
        new(Serilog.Core.Logger.None, open, bound);

    private static Task<byte[]> Exchange(
        HidSharpTransport transport,
        TimeSpan budget,
        CancellationToken cancellationToken = default
    ) => transport.ExchangeAsync("path", new byte[4], _ => true, budget, cancellationToken);

    [Test]
    public void A_macos_path_is_the_registry_entry_id_and_round_trips()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(MacHid.PathOf(0x100769C19), Is.EqualTo(MacPath));
            Assert.That(MacHid.TryParseEntryId(MacPath, out var entryId), Is.True);
            Assert.That(entryId, Is.EqualTo(0x100769C19UL));
            Assert.That(MacHid.TryParseEntryId("IOService:/AppleARMPE/arm-io", out _), Is.False);
            Assert.That(HidSharpTransport.ParseInterfaceNumber(MacPath), Is.Null);
        }
    }

    [Test]
    public void A_macos_entry_reads_its_collections_and_lengths_from_the_report_descriptor()
    {
        var candidate = HidSharpTransport.Describe(
            new MacHidEntry(
                MacPath,
                0x046D,
                0x0ABA,
                " PRO X Wireless Gaming Headset ",
                SerialNumber: null,
                InterfaceNumber: 3,
                LocationId: 0x01134420,
                Convert.FromHexString(HeadsetDescriptor)
            )
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate.ProductName, Is.EqualTo("PRO X Wireless Gaming Headset"));
            Assert.That(candidate.InterfaceNumber, Is.EqualTo(3));
            Assert.That(candidate.HasUsage(0xFF43, 0x0202), Is.True);
            Assert.That(candidate.OutputReportLength, Is.EqualTo(64));
            Assert.That(candidate.InputReportLength, Is.EqualTo(64));
            Assert.That(candidate.FeatureReportLength, Is.EqualTo(64));
            Assert.That(candidate.CouldNotOpen, Is.False);
            Assert.That(HidFamily.PhysicalUnitKey(candidate), Is.EqualTo("m:01134420"));
        }
    }

    [Test]
    public void A_macos_entry_without_a_report_descriptor_counts_as_unopenable()
    {
        var candidate = HidSharpTransport.Describe(
            new MacHidEntry(MacPath, 0x046D, 0x0ABA, null, null, null, null, ReportDescriptor: null)
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate.ProductName, Is.EqualTo("HID device"));
            Assert.That(candidate.CouldNotOpen, Is.True);
        }
    }

    [Test]
    public void Reads_the_interface_number_from_a_windows_path_and_ignores_others()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                HidSharpTransport.ParseInterfaceNumber(@"\\?\hid#vid_1532&pid_00b7&mi_02#8&1abcd&0&0000#{guid}"),
                Is.EqualTo(2)
            );
            Assert.That(HidSharpTransport.ParseInterfaceNumber("/dev/hidraw3"), Is.Null);
        }
    }

    private static HidCandidate Candidate(string path, string? serial, long? locationId = null) =>
        new(path, 0x1532, 0x00AA, null, "Mouse", 91, serial, LocationId: locationId);

    [Test]
    public void Interfaces_of_one_macos_unit_share_a_key_and_identical_mice_do_not()
    {
        var unitA0 = HidFamily.PhysicalUnitKey(Candidate(MacHid.PathOf(0x10), "000000000000", 0x02100000));
        var unitA1 = HidFamily.PhysicalUnitKey(Candidate(MacHid.PathOf(0x11), "000000000000", 0x02100000));
        var unitB0 = HidFamily.PhysicalUnitKey(Candidate(MacHid.PathOf(0x12), "000000000000", 0x02200000));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unitA0, Is.EqualTo(unitA1));
            Assert.That(unitA0, Is.Not.EqualTo(unitB0));
        }
    }

    [Test]
    public void An_all_zero_serial_on_windows_no_longer_merges_two_units()
    {
        var a = HidFamily.PhysicalUnitKey(
            Candidate(@"\\?\hid#vid_1532&pid_00b7&mi_00#8&1abcd&0&0000#{guid}", "000000000000")
        );
        var b = HidFamily.PhysicalUnitKey(
            Candidate(@"\\?\hid#vid_1532&pid_00b7&mi_00#8&2ffff&0&0000#{guid}", "000000000000")
        );

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void A_real_serial_identifies_the_unit_before_any_path()
    {
        Assert.That(
            HidFamily.PhysicalUnitKey(Candidate(MacPath, " AB12 ", 0x02100000)),
            Is.EqualTo("s:ab12")
        );
    }

    [Test]
    public async Task An_unbounded_exchange_lays_the_request_after_the_report_id()
    {
        var channel = new FakeChannel([9, 8, 7, 6, 5]);
        var transport = Transport(_ => channel, bound: false);

        var response = await Exchange(transport, TimeSpan.FromSeconds(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.Sent, Has.Length.EqualTo(5));
            Assert.That(response, Is.EqualTo(new byte[] { 9, 8, 7, 6, 5 }));
            Assert.That(channel.Disposed.Task.IsCompleted, Is.True);
        }
    }

    [Test]
    public async Task A_blocked_open_ends_at_the_budget_with_a_non_cancellation_error_and_is_cleaned_up()
    {
        using var release = new ManualResetEventSlim();
        var channel = new FakeChannel();
        var transport = Transport(
            _ =>
            {
                release.Wait();
                return channel;
            },
            bound: true
        );

        var exception = Assert.CatchAsync(async () =>
            await Exchange(transport, TimeSpan.FromMilliseconds(200))
        );

        Assert.That(exception, Is.TypeOf<InvalidOperationException>());
        Assert.That(channel.Disposed.Task.IsCompleted, Is.False);

        release.Set();
        await channel.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void A_refused_open_surfaces_its_own_error()
    {
        var transport = Transport(_ => throw new InvalidOperationException("refused"), bound: true);

        var exception = Assert.CatchAsync(async () => await Exchange(transport, TimeSpan.FromSeconds(2)));

        Assert.That(exception, Is.TypeOf<InvalidOperationException>().And.Message.EqualTo("refused"));
    }

    [Test]
    public void A_cancelled_caller_gets_a_cancellation_while_the_open_is_pending()
    {
        using var release = new ManualResetEventSlim();
        var transport = Transport(
            _ =>
            {
                release.Wait();
                return new FakeChannel();
            },
            bound: true
        );
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await Exchange(transport, TimeSpan.FromSeconds(10), cancel.Token)
        );
        release.Set();
    }

    [Test]
    public void A_bounded_report_exchange_ends_at_the_budget_without_a_cancellation()
    {
        var transport = Transport(_ => new FakeChannel(), bound: true);

        var exception = Assert.CatchAsync(async () =>
            await transport.BoundAsync(
                "HID report exchange",
                TimeSpan.FromMilliseconds(150),
                async ct =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    return [];
                },
                CancellationToken.None
            )
        );

        Assert.That(exception, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void A_bounded_report_exchange_honours_a_cancelled_caller()
    {
        var transport = Transport(_ => new FakeChannel(), bound: true);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await transport.BoundAsync(
                "HID report exchange",
                TimeSpan.FromSeconds(10),
                async ct =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    return [];
                },
                cancel.Token
            )
        );
    }
}

[TestFixture]
public sealed class HidCollectionMatchingTests
{
    private static readonly (byte, int)[] HeadsetOutputReports = [(0x11, 20), (0x90, 15), (0xC4, 64)];

    [Test]
    public void The_write_is_as_long_as_the_report_with_the_requests_id()
    {
        Assert.That(HidSharpTransport.OutputReportLength(HeadsetOutputReports, 0x11, 65), Is.EqualTo(20));
    }

    [Test]
    public void An_unknown_report_id_falls_back_to_the_longest_report()
    {
        Assert.That(HidSharpTransport.OutputReportLength(HeadsetOutputReports, 0x42, 65), Is.EqualTo(65));
    }

    private static readonly (int, int)[] MacHeadsetCollections = [(0x0C, 0x01), (0xFF43, 0x0202), (0xFF00, 0x0001)];

    private sealed class Transport(params HidCandidate[] candidates) : IHidTransport
    {
        public IReadOnlyList<HidCandidate> FindCandidates(
            int vendorId,
            int productId,
            int? interfaceNumber,
            int minFeatureReportLength
        ) => productId == 0x0ABA ? candidates : [];

        public IReadOnlyList<HidCandidate> ListFeatureReportDevices() => [];

        public Task<byte[]> ExchangeAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<byte[]> ExchangeReportsAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("no answer");
    }

    private static HidCandidate Headset(string path, params (int, int)[] usages) =>
        new(path, 0x046D, 0x0ABA, null, "PRO X", 0, null, 64, 64, usages[0].Item1, usages[0].Item2, usages);

    private static async Task<int> SourceCountAsync(HidCandidate candidate)
    {
        var devices = new DeviceCatalog();
        devices.Set(
            [
                new BatterySlot(
                    "headset",
                    "Headset",
                    BatterySourceKind.Headset,
                    DeviceType.Catalog,
                    CatalogDeviceId: "logitech-g-pro-x-wireless"
                ),
            ]
        );
        var family = new HidFamily(
            [new LogitechHeadsetProtocol()],
            new Transport(candidate),
            Serilog.Core.Logger.None
        );

        return (await new DeviceFamilyProvider([family], devices).DiscoverAsync(CancellationToken.None)).Count;
    }

    [Test]
    public async Task A_vendor_collection_after_the_first_one_is_found_on_one_macos_interface()
    {
        Assert.That(await SourceCountAsync(Headset("mac", MacHeadsetCollections)), Is.EqualTo(1));
    }

    [Test]
    public async Task An_interface_without_the_vendor_collection_is_not_used()
    {
        Assert.That(await SourceCountAsync(Headset("mac", (0x0C, 0x01), (0xFF00, 0x0001))), Is.Zero);
    }

    [Test]
    public void Without_a_collection_list_only_the_first_usage_counts()
    {
        var candidate = new HidCandidate("p", 1, 2, null, "n", 0, null, 20, 20, 0xFF43, 0x0202);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate.HasUsage(0xFF43, 0x0202), Is.True);
            Assert.That(candidate.HasUsage(0xFF00, 0x0001), Is.False);
        }
    }
}
