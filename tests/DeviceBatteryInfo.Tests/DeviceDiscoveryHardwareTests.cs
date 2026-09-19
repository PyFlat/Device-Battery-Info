using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Sources.Bluetooth;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.Razer;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

/// <summary>Explicit: runs the real Windows discovery used by the config flow and prints what it finds.</summary>
[TestFixture]
[Explicit]
[Category("Hardware")]
public sealed class DeviceDiscoveryHardwareTests
{
    [Test]
    public async Task Enumerates_bluetooth_and_hid()
    {
        Assume.That(OperatingSystem.IsWindows());

        var discovery = new WindowsDeviceDiscovery(
            new HidSharpTransport(Serilog.Core.Logger.None),
            new PowerShellPnpBatteryReader()
        );

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bt = await discovery.ListBluetoothDevicesAsync(CancellationToken.None);
        TestContext.Out.WriteLine(
            $"bluetooth ({sw.ElapsedMilliseconds} ms): "
                + string.Join(" | ", bt.Select(d => $"{d.Name} ({(d.Percent is { } p ? $"{p}%" : "?")})"))
        );

        sw.Restart();
        var hid = await discovery.ListHidDevicesAsync(CancellationToken.None);
        TestContext.Out.WriteLine($"hid ({sw.ElapsedMilliseconds} ms): {hid.Count}");
        foreach (var d in hid)
        {
            TestContext.Out.WriteLine(
                $"  VID_{d.VendorId:X4} PID_{d.ProductId:X4} iface {d.InterfaceNumber} {d.ProductName}"
            );
        }

        Assert.Pass();
    }
}
