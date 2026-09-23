namespace DeviceBatteryInfo.Core;

public interface IDeviceDiscovery
{
    Task<IReadOnlyList<BluetoothDeviceCandidate>> ListBluetoothDevicesAsync(
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<AndroidDeviceCandidate>> ListAndroidDevicesAsync(
        CancellationToken cancellationToken
    );

    // Not used by any picker today; kept for a future "scan for supported devices" step.
    Task<IReadOnlyList<DiscoveredHidDevice>> ListHidDevicesAsync(
        CancellationToken cancellationToken
    );
}

public sealed record DiscoveredHidDevice(
    int VendorId,
    int ProductId,
    int? InterfaceNumber,
    string ProductName
);

public sealed record BluetoothDeviceCandidate(string Name, int? Percent);

public sealed record AndroidDeviceCandidate(
    string Serial,
    string Model,
    int? Percent,
    bool NeedsAuthorization
);
