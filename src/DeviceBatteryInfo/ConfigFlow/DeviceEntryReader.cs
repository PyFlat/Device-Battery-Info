using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.ConfigFlow;

namespace DeviceBatteryInfo.ConfigFlow;

internal static class DeviceEntryReader
{
    private static readonly TimeSpan BetweenReads = TimeSpan.FromMilliseconds(40);

    public static async Task<IReadOnlyList<BatterySlot>> ReadAsync(
        IIntegrationConfig config,
        DeviceModelCatalog models,
        CancellationToken cancellationToken
    )
    {
        var entries = await HostCallRetry.RunAsync(
            () => config.GetEntriesAsync(cancellationToken),
            cancellationToken
        );
        if (entries.Count == 0)
        {
            return [];
        }

        var ordered = entries.OrderBy(e => e.Id).ToArray();
        var slots = new List<BatterySlot>(ordered.Length);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in ordered)
        {
            async Task<string?> Read(string key)
            {
                await Task.Delay(BetweenReads, cancellationToken).ConfigureAwait(false);
                return await HostCallRetry.RunAsync(
                        () => config.GetStringAsync(entry.Id, key, cancellationToken),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            var name = await Read(DeviceConfigKeys.Name) is { Length: > 0 } n ? n : entry.Title;
            var typeValue = await Read(DeviceConfigKeys.Type);
            // Entries written before the catalog was generic stored the model id as the type itself.
            var model = models.ById(await Read(DeviceConfigKeys.CatalogDevice) ?? typeValue);
            var type = model is null ? DeviceConfigKeys.ParseType(typeValue) : DeviceType.Catalog;
            var id = BatterySlots.Reserve(BatterySlots.Slug(name), used);

            slots.Add(
                type switch
                {
                    DeviceType.System => new BatterySlot(
                        id,
                        name,
                        BatterySourceKind.System,
                        DeviceType.System
                    ),
                    DeviceType.Bluetooth => new BatterySlot(
                        id,
                        name,
                        DeviceConfigKeys.ParseKind(
                            await Read(DeviceConfigKeys.Kind)
                                ?? await Read(DeviceConfigKeys.LegacyBluetoothKind)
                        ),
                        DeviceType.Bluetooth,
                        BluetoothFriendlyName: await Read(DeviceConfigKeys.BluetoothName)
                    ),
                    DeviceType.Catalog => new BatterySlot(
                        id,
                        name,
                        model!.Kind,
                        DeviceType.Catalog,
                        CatalogDeviceId: model.Id
                    ),
                    _ => new BatterySlot(
                        id,
                        name,
                        await Read(DeviceConfigKeys.Kind) is { } kind
                            ? DeviceConfigKeys.ParseKind(kind)
                            : BatterySourceKind.Phone,
                        DeviceType.AdbPhone,
                        AdbAddress: (await Read(DeviceConfigKeys.AdbAddress))?.Trim()
                    ),
                }
            );
        }

        return slots;
    }
}
