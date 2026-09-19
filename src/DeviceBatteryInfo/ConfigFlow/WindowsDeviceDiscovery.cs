using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Bluetooth;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.ConfigFlow;

internal sealed class WindowsDeviceDiscovery(
    IHidTransport hidTransport,
    IPnpBatteryReader pnpReader
) : IDeviceDiscovery
{
    public async Task<IReadOnlyList<BluetoothDeviceCandidate>> ListBluetoothDevicesAsync(
        CancellationToken cancellationToken
    )
    {
        var devices = await pnpReader.ListDevicesAsync(cancellationToken);
        return devices
            .Select(d => new BluetoothDeviceCandidate(
                d.Name,
                BluetoothBatteryParser.ParsePercent(d.RawBattery)
            ))
            .ToArray();
    }

    public Task<IReadOnlyList<DiscoveredHidDevice>> ListHidDevicesAsync(
        CancellationToken cancellationToken
    ) =>
        // HidSharp opens every device to read its report descriptor, which can block - keep it off
        // the caller's thread
        Task.Run<IReadOnlyList<DiscoveredHidDevice>>(
            () =>
                hidTransport
                    .ListFeatureReportDevices()
                    .Select(c => new DiscoveredHidDevice(
                        c.VendorId,
                        c.ProductId,
                        c.InterfaceNumber,
                        c.ProductName
                    ))
                    .ToArray(),
            cancellationToken
        );
}
