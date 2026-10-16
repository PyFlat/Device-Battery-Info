using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Sources.Bluetooth;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.Aula;
using DeviceBatteryInfo.Sources.Corsair;
using DeviceBatteryInfo.Sources.Logitech;
using DeviceBatteryInfo.Sources.Mchose;
using DeviceBatteryInfo.Sources.Rapoo;
using DeviceBatteryInfo.Sources.Razer;
using DeviceBatteryInfo.Sources.SystemBattery;
using DeviceBatteryInfo.Sources.Sony;
using HidSharp;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
[Explicit]
[Category("Hardware")]
public sealed class HardwareTests
{
    private static readonly HidProtocol[] Protocols =
    [
        new RazerProtocol(),
        new LogitechProtocol(),
        new LogitechHeadsetProtocol(),
        new LogitechCenturionProtocol(),
        new LogitechVoltageBatteryProtocol(),
        new CorsairProtocol(),
        new RapooProtocol(),
        new AulaProtocol(),
        new SonyProtocol(),
        new MchoseProtocol(),
    ];

    [SetUp]
    public void RequireSupportedPlatform() =>
        Assume.That(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux());

    private static IBluetoothBatteryReader PlatformBluetoothReader() =>
        OperatingSystem.IsMacOS() ? new MacBluetoothBatteryReader(Serilog.Core.Logger.None)
        : OperatingSystem.IsLinux() ? new BlueZBatteryReader()
        : OperatingSystem.IsWindows() ? new WindowsBluetoothBatteryReader()
        : throw new PlatformNotSupportedException();

    [Test]
    public async Task Reads_the_system_battery()
    {
        ISystemPowerReader reader = OperatingSystem.IsMacOS() ? new MacSystemPowerReader()
            : OperatingSystem.IsLinux() ? new LinuxSystemPowerReader()
            : new WindowsSystemPowerReader();

        var reading = await reader.ReadAsync(CancellationToken.None);

        HardwareReport.Line(
            "System battery",
            reading is null
                ? "no battery"
                : $"{reading.Percent}% {reading.Status} empty {reading.TimeToEmpty} full {reading.TimeToFull}"
        );
    }

    [Test]
    public async Task Lists_bluetooth_devices()
    {
        var discovery = new SystemDeviceDiscovery(
            new HidSharpTransport(Serilog.Core.Logger.None),
            PlatformBluetoothReader(),
            new MacroDeck.Plugin.Testing.Fakes.FakeAndroidDeviceManager()
        );

        var devices = await discovery.ListBluetoothDevicesAsync(CancellationToken.None);

        foreach (var device in devices)
        {
            HardwareReport.Line(
                $"Bluetooth {device.Name}",
                device.Percent is { } percent ? $"{percent}%" : "no battery value"
            );
        }
    }

    [Test]
    public async Task Reads_every_bluetooth_device_in_one_poll()
    {
        var reader = PlatformBluetoothReader();
        var names = (await reader.ListDevicesAsync(CancellationToken.None)).Select(d => d.Name).ToArray();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var levels = await Task.WhenAll(names.Select(n => reader.ReadRawAsync(n, CancellationToken.None)));
        watch.Stop();

        foreach (var (name, raw) in names.Zip(levels))
        {
            HardwareReport.Line($"Bluetooth {name}", raw is null ? "no battery value" : $"{raw}%");
        }

        HardwareReport.Line("Bluetooth poll", $"{names.Length} device(s) in {watch.ElapsedMilliseconds} ms");
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void Lists_hid_interfaces_of_every_supported_brand()
    {
        foreach (var protocol in Protocols)
        {
            IEnumerable<HidCandidate> interfaces = OperatingSystem.IsMacOS()
                ? MacHid.Enumerate(protocol.VendorId).Select(HidSharpTransport.Describe)
                : DeviceList
                    .Local.GetHidDevices(protocol.VendorId)
                    .Select(d => HidSharpTransport.Describe(d, withUsage: true));
            foreach (var candidate in interfaces.OrderBy(c => c.ProductId).ThenBy(c => c.InterfaceNumber))
            {
                HardwareReport.Line(
                    $"{candidate.ProductName} ({candidate.ProductId:X4})",
                    $"interface {candidate.InterfaceNumber} usage {candidate.UsagePage:X4}:{candidate.Usage:X4} "
                        + $"in {candidate.InputReportLength} out {candidate.OutputReportLength} feature {candidate.FeatureReportLength}"
                );
            }
        }
    }

    [Test]
    public async Task Reads_every_supported_hid_device_through_the_plugin()
    {
        var probes = new ProbeLog();
        var (family, sources) = await DiscoverAsync(
            Protocols,
            new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(probes).CreateLogger()
        );
        foreach (var line in probes.Lines)
        {
            HardwareReport.Line("probe", line);
        }

        var read = 0;
        foreach (var model in family.Models)
        {
            var subject = $"{model.Brand} {model.Name}";
            var source = sources.FirstOrDefault(s => s.Id == model.Id);
            if (source is null)
            {
                HardwareReport.Line(subject, "not connected");
                continue;
            }

            try
            {
                var reading = await source.ReadAsync(CancellationToken.None);
                HardwareReport.Line(subject, $"{reading.Percent}% {reading.Status}");
                read++;
            }
            catch (Exception exception)
            {
                HardwareReport.Line(subject, $"read failed: {exception.Message}");
            }
        }

        Assert.That(read, Is.GreaterThan(0), "No supported HID device answered.");
    }

    [Test]
    public async Task Reads_the_raw_mchose_battery_report()
    {
        var protocol = new MchoseProtocol();
        var transport = new HidSharpTransport(Serilog.Core.Logger.None);
        var candidates = protocol
            .Devices.SelectMany(d => d.ProductIds)
            .SelectMany(productId => transport.FindCandidates(protocol.VendorId, productId, interfaceNumber: null, 0))
            .Where(c =>
                !c.CouldNotOpen
                && c.OutputReportLength >= protocol.ReportLength
                && c.HasUsage(protocol.UsagePage, protocol.Usage)
            )
            .ToArray();
        Assume.That(candidates, Is.Not.Empty, "No MCHOSE dongle is connected.");

        foreach (var candidate in candidates)
        {
            var subject = $"MCHOSE {DateTime.Now:HH:mm:ss}";
            var channel = new HidChannel(transport, candidate.Path, candidate.ProductId, protocol.ReportKind);
            try
            {
                var report = await channel.ExchangeAsync(
                    MchoseProtocol.BatteryRequest,
                    MchoseProtocol.IsBatteryReport,
                    CancellationToken.None
                );
                string parsed;
                try
                {
                    var reading = MchoseProtocol.ParseBattery(report);
                    parsed = $"{reading.Percent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}% {reading.Status}";
                }
                catch (InvalidOperationException exception)
                {
                    parsed = exception.Message;
                }

                HardwareReport.Line(
                    subject,
                    $"{string.Join(" ", report.Take(16).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)))} = {parsed}"
                );
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                HardwareReport.Line(subject, "no answer: " + exception.Message);
            }
        }
    }

    // Shows which battery feature (0x1000, 0x1001, 0x1004) a Logitech device has before it gets a protocol.
    [Test]
    public async Task Lists_the_hidpp_features_of_every_logitech_device()
    {
        var transport = new HidSharpTransport(Serilog.Core.Logger.None);
        const int vendorId = 0x046D;
        IEnumerable<HidCandidate> interfaces = OperatingSystem.IsMacOS()
            ? MacHid.Enumerate(vendorId).Select(HidSharpTransport.Describe)
            : DeviceList.Local.GetHidDevices(vendorId).Select(d => HidSharpTransport.Describe(d, withUsage: true));
        var candidates = interfaces
            .Select(c => c.ProductId)
            .Distinct()
            .Order()
            .SelectMany(productId => transport.FindCandidates(vendorId, productId, interfaceNumber: null, 0))
            .Where(c => !c.CouldNotOpen && c.OutputReportLength >= 20 && c.HasUsage(0xFF00, 0x0002))
            .ToArray();
        Assume.That(candidates, Is.Not.Empty, "No Logitech HID++ interface present.");

        foreach (var candidate in candidates)
        {
            var subject = $"{candidate.ProductName} ({candidate.ProductId:X4}) interface {candidate.InterfaceNumber}";
            var channel = new HidChannel(transport, candidate.Path, candidate.ProductId, HidReportKind.InputOutput);
            foreach (var deviceIndex in new byte[] { 0x01, 0xFF })
            {
                try
                {
                    HardwareReport.Line(
                        $"{subject} index {deviceIndex:X2}",
                        await DescribeHidppDeviceAsync(channel, deviceIndex)
                    );
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    HardwareReport.Line($"{subject} index {deviceIndex:X2}", "no answer: " + exception.Message);
                }
            }
        }
    }

    private static async Task<string> DescribeHidppDeviceAsync(HidChannel channel, byte deviceIndex)
    {
        var featureSet = await FeatureIndexAsync(channel, deviceIndex, 0x0001);
        var count = (await CallHidppAsync(channel, deviceIndex, featureSet, 0x00))[4];
        var features = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var reply = await CallHidppAsync(channel, deviceIndex, featureSet, 0x01, (byte)i);
            features.Add($"{reply[4] << 8 | reply[5]:X4}");
        }

        string name;
        try
        {
            var nameFeature = await FeatureIndexAsync(channel, deviceIndex, 0x0005);
            var length = (await CallHidppAsync(channel, deviceIndex, nameFeature, 0x00))[4];
            var text = new System.Text.StringBuilder();
            while (text.Length < length)
            {
                var reply = await CallHidppAsync(channel, deviceIndex, nameFeature, 0x01, (byte)text.Length);
                var chunk = reply.AsSpan(4, Math.Min(length - text.Length, reply.Length - 4));
                text.Append(System.Text.Encoding.ASCII.GetString(chunk));
            }

            name = text.ToString();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            name = "(no name)";
        }

        return $"{name}: features {string.Join(" ", features)}";
    }

    private static async Task<byte> FeatureIndexAsync(HidChannel channel, byte deviceIndex, ushort featureId) =>
        LogitechHidppProtocol.ParseFeatureIndex(
            await CallHidppAsync(channel, deviceIndex, 0x00, 0x00, (byte)(featureId >> 8), (byte)featureId)
        );

    private static async Task<byte[]> CallHidppAsync(
        HidChannel channel,
        byte deviceIndex,
        byte featureIndex,
        byte functionId,
        params byte[] parameters
    )
    {
        var function = LogitechHidppProtocol.Function(functionId);
        var reply = await channel.ExchangeAsync(
            [0x11, deviceIndex, featureIndex, function, .. parameters],
            r => LogitechHidppProtocol.IsReplyTo(r, deviceIndex, featureIndex, function),
            CancellationToken.None
        );
        return reply[2] == 0xFF
            ? throw new InvalidOperationException($"HID++ error 0x{reply[5]:X2}.")
            : reply;
    }

    [Test]
    public async Task Lists_the_centurion_features_of_the_logitech_headset()
    {
        var protocol = new LogitechCenturionProtocol();
        var transport = new HidSharpTransport(Serilog.Core.Logger.None);
        var candidates = protocol
            .Devices.SelectMany(d => d.ProductIds)
            .SelectMany(productId => transport.FindCandidates(protocol.VendorId, productId, interfaceNumber: null, 0))
            .Where(c =>
                !c.CouldNotOpen
                && c.OutputReportLength >= protocol.ReportLength
                && c.HasUsage(protocol.UsagePage, protocol.Usage)
            )
            .ToArray();
        Assume.That(candidates, Is.Not.Empty, "No Logitech Centurion receiver is connected.");

        foreach (var candidate in candidates)
        {
            var subject = $"{candidate.ProductName} ({candidate.ProductId:X4})";
            var channel = new HidChannel(transport, candidate.Path, candidate.ProductId, protocol.ReportKind);
            var step = "receiver features";
            try
            {
                var receiver = await CenturionFeaturesAsync(
                    (index, function, parameters) =>
                        LogitechCenturionProtocol.DirectAsync(channel, index, function, parameters, CancellationToken.None)
                );
                HardwareReport.Line(subject, "receiver: " + string.Join(" ", receiver.Select(f => $"{f:X4}")));

                step = "headset features";
                var bridge = (byte)Array.IndexOf(receiver, LogitechCenturionProtocol.BridgeFeature);
                var headset = await CenturionFeaturesAsync(
                    (index, function, parameters) =>
                        LogitechCenturionProtocol.BridgeAsync(
                            channel,
                            bridge,
                            index,
                            function,
                            parameters,
                            CancellationToken.None
                        )
                );
                HardwareReport.Line(subject, $"headset via bridge {bridge}: " + string.Join(" ", headset.Select(f => $"{f:X4}")));

                step = "battery";
                var battery = (byte)Array.IndexOf(headset, LogitechCenturionProtocol.BatteryFeature);
                var data = await LogitechCenturionProtocol.BridgeAsync(
                    channel,
                    bridge,
                    battery,
                    0x00,
                    [],
                    CancellationToken.None
                );
                var reading = LogitechCenturionProtocol.ParseBattery(data);
                HardwareReport.Line(
                    $"{subject} {DateTime.Now:HH:mm:ss}",
                    $"battery {string.Join(" ", data.Take(8).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)))}"
                        + $" = {reading.Percent}% {reading.Status}"
                );
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                HardwareReport.Line(subject, $"stopped at {step}: {exception.Message}");
            }
        }
    }

    private static async Task<ushort[]> CenturionFeaturesAsync(Func<byte, byte, byte[], Task<byte[]>> call)
    {
        var featureSet = LogitechCenturionProtocol.ParseFeatureIndex(await call(0x00, 0x00, [0x00, 0x01]));
        var count = (await call(featureSet, 0x00, []))[0];
        var features = new ushort[count];
        for (var n = 0; n < count; n++)
        {
            var data = await call(featureSet, 0x10, [(byte)n]);
            features[n] = (ushort)(data[1] << 8 | data[2]);
        }

        return features;
    }

    // For calibrating the 0x1001 curve against G Hub: the raw voltage behind the percentage.
    [Test]
    public async Task Reads_the_raw_logitech_battery_voltage()
    {
        var probe = new LogitechVoltageProbe();
        var (_, sources) = await DiscoverAsync([probe]);
        Assume.That(sources, Is.Not.Empty, "No Logitech mouse with a battery voltage is connected.");

        foreach (var source in sources)
        {
            var reading = await source.ReadAsync(CancellationToken.None);
            HardwareReport.Line(
                $"{source.Id} {DateTime.Now:HH:mm:ss}",
                $"{probe.Millivolts} mV flags 0x{probe.Flags:X2} = {reading.Percent}% {reading.Status}"
            );
        }
    }

    // Stands in for Razer Synapse polling the same control interface.
    [Test]
    public async Task Reads_while_a_foreign_poller_hammers_the_same_interface()
    {
        Assume.That(OperatingSystem.IsWindows(), "The competing poller talks to NativeHid.");
        var interference = new CancellationTokenSource();
        var pollers = DeviceList
            .Local.GetHidDevices(0x1532)
            .Select(d => HidSharpTransport.Describe(d))
            .Where(c => c.FeatureReportLength >= 90)
            .Select(candidate => Task.Run(() => Hammer(candidate, interference.Token)))
            .ToArray();

        Assume.That(pollers, Is.Not.Empty, "No Razer control interface present.");

        try
        {
            var read = 0;
            var retries = new RetryCounter();
            var (_, sources) = await DiscoverAsync(
                [new RazerProtocol()],
                new Serilog.LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Sink(retries)
                    .CreateLogger()
            );
            foreach (var source in sources)
            {
                try
                {
                    var reading = await source.ReadAsync(CancellationToken.None);
                    HardwareReport.Line(source.Id, $"{reading.Percent}% {reading.Status}");
                    read++;
                }
                catch (Exception exception)
                {
                    HardwareReport.Line(source.Id, "read failed: " + exception.Message);
                }
            }

            // Zero retries means the poller never collided, so the run proved nothing about the timing.
            HardwareReport.Line("retries", $"{retries.Count} not-ready responses retried past");
            Assert.That(read, Is.GreaterThan(0), "No Razer device answered while another poller ran.");
        }
        finally
        {
            await interference.CancelAsync();
            await Task.WhenAll(pollers);
        }
    }

    // A different command, so its answer is one our own exchange must reject and retry past.
    private static void Hammer(HidCandidate candidate, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var report = new byte[90];
        report[1] = 0x1F;
        report[5] = 0x16;
        report[6] = 0x00;
        report[7] = 0x82;
        byte checksum = 0;
        for (var i = 3; i < 88; i++)
        {
            checksum ^= report[i];
        }

        report[88] = checksum;

        var buffer = new byte[candidate.FeatureReportLength];
        report.CopyTo(buffer, Math.Clamp(candidate.FeatureReportLength - report.Length, 0, 1));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var handle = NativeHid.Open(candidate.Path);
                NativeHid.SetFeature(handle, buffer);
                Thread.Sleep(20);
                NativeHid.GetFeature(handle, new byte[candidate.FeatureReportLength]);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Thread.Sleep(20);
            }
        }
    }

    private sealed class ProbeLog : Serilog.Core.ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

        public IEnumerable<string> Lines => _lines;

        public void Emit(Serilog.Events.LogEvent logEvent) =>
            _lines.Enqueue(
                logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)
                    + (logEvent.Exception is { } exception ? $" ({exception.Message})" : "")
            );
    }

    private sealed class LogitechVoltageProbe() : LogitechHidppProtocol(0xFF00, 0x0002, 0x01)
    {
        private readonly LogitechVoltageBatteryProtocol _protocol = new();

        public int Millivolts { get; private set; }

        public byte Flags { get; private set; }

        public override IReadOnlyList<HidDeviceInfo> Devices => _protocol.Devices;

        protected override ushort BatteryFeature => 0x1001;

        protected override byte BatteryFunction => 0x00;

        protected override BatteryReading ParseBattery(ReadOnlySpan<byte> report)
        {
            var reading = LogitechVoltageBatteryProtocol.ParseVoltage(report);
            Millivolts = report[4] << 8 | report[5];
            Flags = report[6];
            return reading;
        }
    }

    private sealed class RetryCounter : Serilog.Core.ILogEventSink
    {
        private int _count;

        public int Count => _count;

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            if (logEvent.MessageTemplate.Text.Contains("not ready", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }
        }
    }

    private static async Task<(
        HidFamily Family,
        IReadOnlyList<IBatterySource> Sources
    )> DiscoverAsync(HidProtocol[] protocols, Serilog.ILogger? logger = null)
    {
        logger ??= Serilog.Core.Logger.None;
        var family = new HidFamily(protocols, new HidSharpTransport(logger), logger);
        var devices = new DeviceCatalog();
        devices.Set(
            [
                .. family.Models.Select(model => new BatterySlot(
                    model.Id,
                    model.Name,
                    model.Kind,
                    DeviceType.Catalog,
                    CatalogDeviceId: model.Id
                )),
            ]
        );

        return (
            family,
            await new DeviceFamilyProvider([family], devices).DiscoverAsync(CancellationToken.None)
        );
    }
}
