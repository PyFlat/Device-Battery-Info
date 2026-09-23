using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.Android;

namespace DeviceBatteryInfo.Sources.Adb;

internal sealed class AdbBatterySource(IAndroidDeviceManager android, BatterySlot slot)
    : IBatterySource
{
    private readonly IAndroidDeviceManager _android = android;
    private readonly string _address = slot.AdbAddress!;

    public string Id { get; } = slot.Id;

    public string DisplayName { get; } = slot.DisplayName;

    public BatterySourceKind Kind => BatterySourceKind.Phone;

    public async ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken)
    {
        if (_android.Access != AndroidDeviceAccess.Available)
        {
            throw new InvalidOperationException(
                $"Macro Deck's adb connection is not available to this plugin ({_android.Access})."
            );
        }

        var device = await FindOrConnectAsync(cancellationToken);
        var battery = await device.GetBatteryStateAsync(cancellationToken);
        return AdbBatteryMapper.ToReading(battery);
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
    : IBatterySourceProvider
{
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
            .Select(IBatterySource (d) => new AdbBatterySource(_android, d))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<IBatterySource>>(sources);
    }
}
