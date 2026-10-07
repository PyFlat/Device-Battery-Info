using System.Runtime.Versioning;

namespace DeviceBatteryInfo.Sources.Bluetooth;

// One PnP walk per poll serves every Bluetooth source; a walk per device outlasted the read timeout.
[SupportedOSPlatform("windows")]
internal sealed class WindowsBluetoothBatteryReader : IBluetoothBatteryReader, IDisposable
{
    private readonly Func<IReadOnlyList<PnpNode>> _listNodes;
    private readonly BluetoothSnapshot _snapshot;

    public WindowsBluetoothBatteryReader()
        : this(NativeDeviceProperties.ListBluetoothNodes) { }

    internal WindowsBluetoothBatteryReader(
        Func<IReadOnlyList<PnpNode>> listNodes,
        TimeProvider? timeProvider = null
    )
    {
        _listNodes = listNodes;
        _snapshot = new BluetoothSnapshot(
            cancellationToken =>
                Task.Run(() => BluetoothPnpLevels.ByName(_listNodes()), cancellationToken),
            timeProvider,
            StringComparer.OrdinalIgnoreCase
        );
    }

    public Task<string?> ReadRawAsync(string friendlyName, CancellationToken cancellationToken) =>
        _snapshot.ReadRawAsync(friendlyName, cancellationToken);

    public Task<IReadOnlyList<(string Name, string? RawBattery)>> ListDevicesAsync(
        CancellationToken cancellationToken
    ) => Task.Run(() => BluetoothPnpLevels.Pickable(_listNodes()), cancellationToken);

    public void Dispose() => _snapshot.Dispose();
}
