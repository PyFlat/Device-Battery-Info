using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.Android;

namespace DeviceBatteryInfo.Sources.Adb;

internal sealed class AdbBatterySource(
    IAndroidDeviceManager android,
    BatterySlot slot,
    SemaphoreSlim gate
) : IBatterySource
{
    private readonly IAndroidDeviceManager _android = android;
    private readonly SemaphoreSlim _gate = gate;
    private readonly string _address = slot.AdbAddress!;

    public string Id { get; } = slot.Id;

    public string DisplayName { get; } = slot.DisplayName;

    public BatterySourceKind Kind { get; } = slot.Kind;

    public async ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken)
    {
        if (_android.Access != AndroidDeviceAccess.Available)
        {
            throw new InvalidOperationException(
                $"Macro Deck's adb connection is not available to this plugin ({_android.Access})."
            );
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var device = await FindOrConnectAsync(cancellationToken);
            var battery = await device.GetBatteryStateAsync(cancellationToken);
            return AdbBatteryMapper.ToReading(battery);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IAndroidDevice> FindOrConnectAsync(CancellationToken cancellationToken)
    {
        var device = _android.FindDevice(_address);
        if (device?.State == AndroidDeviceState.Online)
        {
            return device;
        }

        // A USB serial cannot be connected to, only a wireless host:port can.
        if (_address.Contains(':', StringComparison.Ordinal))
        {
            return await _android.ConnectAsync(_address, cancellationToken);
        }

        throw new InvalidOperationException(
            device is null
                ? $"No Android device '{_address}' is attached."
                : $"Android device '{_address}' is {device.State}."
        );
    }
}

internal sealed class AdbBatterySourceProvider(IAndroidDeviceManager android, DeviceCatalog catalog)
    : IBatterySourceProvider, IDisposable
{
    // The host refuses a fifth concurrent adb call, and adb counts toward the plugin's callback
    // budget, so two phones at a time leave room for the picker and the plugin's other calls.
    private readonly SemaphoreSlim _gate = new(2, 2);
    private readonly IAndroidDeviceManager _android = android;
    private readonly DeviceCatalog _catalog = catalog;

    public ValueTask<IReadOnlyList<IBatterySource>> DiscoverAsync(
        CancellationToken cancellationToken
    )
    {
        var sources = _catalog
            .Devices.Where(d =>
                d.Type == DeviceType.AdbPhone && !string.IsNullOrWhiteSpace(d.AdbAddress)
            )
            .Select(IBatterySource (d) => new AdbBatterySource(_android, d, _gate))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<IBatterySource>>(sources);
    }

    public void Dispose() => _gate.Dispose();
}
