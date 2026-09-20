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
    private static readonly HidProtocol[] Protocols = [new RazerProtocol(), new LogitechProtocol()];

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
        var family = new HidFamily(
            Protocols,
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

        var sources = await new DeviceFamilyProvider([family], devices).DiscoverAsync(
            CancellationToken.None
        );

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
}
