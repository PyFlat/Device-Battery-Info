using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using MacroDeck.Localization;

namespace DeviceBatteryInfo.ConfigFlow;

/// <summary>Every model listed under "Other devices", collected from the registered device families.</summary>
public sealed class DeviceModelCatalog(IEnumerable<IDeviceFamily> families)
{
    private readonly IReadOnlyList<DeviceModel> _entries =
    [
        .. families.SelectMany(f => f.Models),
    ];

    public IReadOnlyList<(string Id, LocalizedText Label)> Brands =>
        _entries
            .GroupBy(e => e.BrandId)
            .Select(g => (g.Key, (LocalizedText)g.First().Brand))
            .ToArray();

    public IReadOnlyList<DeviceModel> ModelsFor(string? brandId) =>
        _entries.Where(e => e.BrandId == brandId).ToArray();

    public DeviceModel? ById(string? id) =>
        id is { Length: > 0 } ? _entries.FirstOrDefault(e => e.Id == id) : null;

    public DeviceModel? For(BatterySlot slot) =>
        slot.Type == DeviceType.Catalog ? ById(slot.CatalogDeviceId) : null;
}
