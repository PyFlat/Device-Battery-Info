using DeviceBatteryInfo.Core;

namespace DeviceBatteryInfo.Sources;

/// <summary>A product listed under "Other devices". Its id is derived from brand and name and is stored
/// in user data, so renaming a shipped model breaks existing entries.</summary>
public record DeviceModel(
    string Brand,
    string Name,
    BatterySourceKind Kind = BatterySourceKind.Other
)
{
    public string Id { get; } = BatterySlots.Slug($"{Brand} {Name}");

    public string BrandId { get; } = BatterySlots.Slug(Brand);
}

/// <summary>A protocol and the models that speak it. Every implementation in this assembly is registered
/// automatically.</summary>
public interface IDeviceFamily
{
    IReadOnlyList<DeviceModel> Models { get; }

    /// <summary>Called every poll cycle with the configured devices whose model is in <see cref="Models"/>.
    /// Returns one source per device that is connected, and nothing for the rest.</summary>
    ValueTask<IReadOnlyList<IBatterySource>> DiscoverAsync(
        IReadOnlyList<(BatterySlot Slot, DeviceModel Model)> entries,
        CancellationToken cancellationToken
    );
}

/// <summary>A family for devices that need no discovery: list the models and read one.</summary>
internal abstract class SimpleDeviceFamily : IDeviceFamily
{
    public abstract IReadOnlyList<DeviceModel> Models { get; }

    /// <summary>Throw when the read fails; the poll loop then keeps the last value and marks it stale.</summary>
    protected abstract ValueTask<BatteryReading> ReadAsync(
        DeviceModel model,
        BatterySlot slot,
        CancellationToken cancellationToken
    );

    /// <summary>A device that is not present is left out of the poll and hidden from widgets.</summary>
    protected virtual ValueTask<bool> IsPresentAsync(
        DeviceModel model,
        BatterySlot slot,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(true);

    public async ValueTask<IReadOnlyList<IBatterySource>> DiscoverAsync(
        IReadOnlyList<(BatterySlot Slot, DeviceModel Model)> entries,
        CancellationToken cancellationToken
    )
    {
        var sources = new List<IBatterySource>(entries.Count);
        foreach (var (slot, model) in entries)
        {
            if (await IsPresentAsync(model, slot, cancellationToken))
            {
                sources.Add(new DelegateBatterySource(slot, ct => ReadAsync(model, slot, ct)));
            }
        }

        return sources;
    }
}

internal sealed class DelegateBatterySource(
    BatterySlot slot,
    Func<CancellationToken, ValueTask<BatteryReading>> read
) : IBatterySource
{
    public string Id { get; } = slot.Id;

    public string DisplayName { get; } = slot.DisplayName;

    public BatterySourceKind Kind { get; } = slot.Kind;

    public ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken) =>
        read(cancellationToken);
}

internal sealed class DeviceFamilyProvider(
    IEnumerable<IDeviceFamily> families,
    DeviceCatalog devices
) : IBatterySourceProvider
{
    private readonly IDeviceFamily[] _families = [.. families];

    public async ValueTask<IReadOnlyList<IBatterySource>> DiscoverAsync(
        CancellationToken cancellationToken
    )
    {
        var configured = devices.Devices.Where(d => d.Type == DeviceType.Catalog).ToArray();
        var sources = new List<IBatterySource>();
        if (configured.Length == 0)
        {
            return sources;
        }

        foreach (var family in _families)
        {
            var entries = configured
                .Select(slot =>
                    (
                        Slot: slot,
                        Model: family.Models.FirstOrDefault(m => m.Id == slot.CatalogDeviceId)
                    )
                )
                .Where(e => e.Model is not null)
                .Select(e => (e.Slot, Model: e.Model!))
                .ToArray();
            if (entries.Length > 0)
            {
                sources.AddRange(await family.DiscoverAsync(entries, cancellationToken));
            }
        }

        return sources;
    }
}
