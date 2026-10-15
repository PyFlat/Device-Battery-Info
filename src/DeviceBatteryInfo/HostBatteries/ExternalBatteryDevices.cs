using System.Security.Cryptography;
using System.Text;
using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.Batteries;

namespace DeviceBatteryInfo.HostBatteries;

// Devices other providers published to Macro Deck's battery catalog. A widget stores one by its
// qualified catalog id, which contains a colon and so never collides with a slot id.
internal static class ExternalBatteryDevices
{
    public static IReadOnlyList<BatteryDeviceSnapshot> List(
        IBatteryApi batteries,
        IReadOnlyList<BatterySlot> ownSlots
    )
    {
        // Our own devices come back through the catalog too; they are matched by the id we sent.
        var own = ownSlots
            .Where(s => s.EntryId is not null)
            .Select(s => HostBatteryDevices.IdOf(s.EntryId!.Value))
            .ToHashSet(StringComparer.Ordinal);

        return batteries
            .Devices.Where(d => !own.Contains(d.LocalId))
            .OrderBy(d => d.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // A catalog id may hold characters a UiElement key rejects (a colon at least), so a widget row
    // is keyed by a hash of it instead.
    public static string RowKey(string catalogId) =>
        "ext-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(catalogId)))[..16];

    public static BatterySourceKind KindOf(string kind) =>
        BatteryDeviceKinds.Normalize(kind) switch
        {
            BatteryDeviceKinds.Phone => BatterySourceKind.Phone,
            BatteryDeviceKinds.Tablet => BatterySourceKind.Tablet,
            BatteryDeviceKinds.Laptop => BatterySourceKind.System,
            BatteryDeviceKinds.Headphones => BatterySourceKind.Headset,
            BatteryDeviceKinds.Controller => BatterySourceKind.Controller,
            BatteryDeviceKinds.Mouse => BatterySourceKind.Mouse,
            BatteryDeviceKinds.Keyboard => BatterySourceKind.Keyboard,
            BatteryDeviceKinds.Speaker => BatterySourceKind.Speaker,
            _ => BatterySourceKind.Other,
        };

    // NotCharging (a charge limit) reads like the macOS and Linux sources map it: Full at 100.
    public static BatteryStatus StatusOf(BatteryChargeState state, int? level) =>
        state switch
        {
            BatteryChargeState.Discharging => BatteryStatus.Discharging,
            BatteryChargeState.Charging => BatteryStatus.Charging,
            BatteryChargeState.Full => BatteryStatus.Full,
            BatteryChargeState.NotCharging when level >= 100 => BatteryStatus.Full,
            _ => BatteryStatus.Unknown,
        };
}
