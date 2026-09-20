using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Sources.Bluetooth;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.Logitech;
using DeviceBatteryInfo.Sources.Razer;
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
    ];

    [SetUp]
    public void RequireWindows() => Assume.That(OperatingSystem.IsWindows());

    [Test]
    public async Task Lists_bluetooth_devices()
    {
        var discovery = new WindowsDeviceDiscovery(
            new HidSharpTransport(Serilog.Core.Logger.None),
            new PowerShellPnpBatteryReader()
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
    public void Lists_hid_interfaces_of_every_supported_brand()
    {
        foreach (var protocol in Protocols)
        {
            var interfaces = DeviceList.Local.GetHidDevices(protocol.VendorId);
            foreach (var hid in interfaces.OrderBy(d => d.ProductID).ThenBy(d => d.DevicePath))
            {
                var candidate = HidSharpTransport.Describe(hid, withUsage: true);
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
        var (family, sources) = await DiscoverAsync(Protocols);

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

    // Stands in for Razer Synapse polling the same control interface.
    [Test]
    public async Task Reads_while_a_foreign_poller_hammers_the_same_interface()
    {
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
            var (_, sources) = await DiscoverAsync([new RazerProtocol()]);
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

    private static async Task<(
        HidFamily Family,
        IReadOnlyList<IBatterySource> Sources
    )> DiscoverAsync(HidProtocol[] protocols)
    {
        var family = new HidFamily(
            protocols,
            new HidSharpTransport(Serilog.Core.Logger.None),
            Serilog.Core.Logger.None
        );
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
