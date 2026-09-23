using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Bluetooth;
using DeviceBatteryInfo.Sources.Hid;
using MacroDeck.Sdk.Android;

namespace DeviceBatteryInfo.ConfigFlow;

internal sealed class WindowsDeviceDiscovery(
    IHidTransport hidTransport,
    IPnpBatteryReader pnpReader,
    IAndroidDeviceManager android
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

    public async Task<IReadOnlyList<AndroidDeviceCandidate>> ListAndroidDevicesAsync(
        CancellationToken cancellationToken
    )
    {
        if (android.Access != AndroidDeviceAccess.Available)
        {
            return [];
        }

        var candidates = new List<AndroidDeviceCandidate>();
        foreach (var device in android.Devices)
        {
            candidates.Add(
                new AndroidDeviceCandidate(
                    device.Serial,
                    string.IsNullOrWhiteSpace(device.Info.Model)
                        ? device.Serial
                        : device.Info.Model,
                    await ReadPercentAsync(device, cancellationToken),
                    device.State == AndroidDeviceState.Unauthorized
                )
            );
        }

        return candidates;
    }

    private static async Task<int?> ReadPercentAsync(
        IAndroidDevice device,
        CancellationToken cancellationToken
    )
    {
        if (device.State != AndroidDeviceState.Online)
        {
            return null;
        }

        try
        {
            var battery = await device.GetBatteryStateAsync(cancellationToken);
            return Math.Clamp(battery.Level, 0, 100);
        }
        catch (AndroidDeviceException)
        {
            return null;
        }
    }

    public Task<IReadOnlyList<DiscoveredHidDevice>> ListHidDevicesAsync(
        CancellationToken cancellationToken
    ) =>
        // HidSharp opens every device to read its report descriptor, which can block.
        // Keep it off the caller's thread.
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
