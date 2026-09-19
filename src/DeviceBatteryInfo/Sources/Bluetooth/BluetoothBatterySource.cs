using DeviceBatteryInfo.Core;

namespace DeviceBatteryInfo.Sources.Bluetooth;

internal sealed class BluetoothBatterySource(IPnpBatteryReader reader, BatterySlot slot)
    : IBatterySource
{
    private readonly IPnpBatteryReader _reader = reader;
    private readonly string _friendlyName = slot.BluetoothFriendlyName!;

    public string Id { get; } = slot.Id;

    public string DisplayName { get; } = slot.DisplayName;

    public BatterySourceKind Kind { get; } = slot.Kind;

    public async ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken)
    {
        var raw = await _reader.ReadRawAsync(_friendlyName, cancellationToken);
        var percent = BluetoothBatteryParser.ParsePercent(raw);

        return percent is null
            ? throw new InvalidOperationException(
                $"Windows has no cached battery reading for '{_friendlyName}'."
            )
            : new BatteryReading { Percent = percent, Status = BatteryStatus.Discharging };
    }
}

internal sealed class BluetoothBatterySourceProvider(
    IPnpBatteryReader reader,
    DeviceCatalog catalog
) : IBatterySourceProvider
{
    private readonly IPnpBatteryReader _reader = reader;
    private readonly DeviceCatalog _catalog = catalog;

    public ValueTask<IReadOnlyList<IBatterySource>> DiscoverAsync(
        CancellationToken cancellationToken
    )
    {
        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult<IReadOnlyList<IBatterySource>>(
                Array.Empty<IBatterySource>()
            );
        }

        var sources = _catalog
            .Devices.Where(d =>
                d.Type == DeviceType.Bluetooth
                && !string.IsNullOrWhiteSpace(d.BluetoothFriendlyName)
            )
            .Select(IBatterySource (d) => new BluetoothBatterySource(_reader, d))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<IBatterySource>>(sources);
    }
}
