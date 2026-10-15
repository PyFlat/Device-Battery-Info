using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.Batteries;

namespace DeviceBatteryInfo.HostBatteries;

internal static class HostBatteryDevices
{
    // The config entry id, not the slot id: the slot id follows the device name, so a rename would
    // leave the old device behind in the host's catalog.
    public static string IdOf(Guid entryId) => entryId.ToString("N");

    public static IReadOnlyList<BatteryDevice> Build(
        IReadOnlyList<BatterySlot> slots,
        BatteryRegistry registry,
        IReadOnlyDictionary<string, BatteryDevice> previous
    )
    {
        var devices = new List<BatteryDevice>(slots.Count);
        foreach (var slot in slots)
        {
            if (slot.EntryId is not { } entryId)
            {
                continue;
            }

            var id = IdOf(entryId);
            var snapshot = registry.TryGet(slot.Id, out var found) ? found : null;
            devices.Add(Describe(id, slot, snapshot, previous.GetValueOrDefault(id)));
            if (devices.Count == BatteryLimits.MaxDevicesPerProvider)
            {
                break;
            }
        }

        return devices;
    }

    private static BatteryDevice Describe(
        string id,
        BatterySlot slot,
        BatterySnapshot? snapshot,
        BatteryDevice? previous
    )
    {
        var name = Truncate(slot.DisplayName, BatteryLimits.MaxNameLength);
        var kind = KindOf(slot.Kind);

        if (snapshot is not { Reading.HasValue: true })
        {
            // Out of reach: the host keeps showing the last level we sent, so keep sending it.
            return (previous ?? new BatteryDevice { Id = id, Name = name }) with
            {
                Name = name,
                Kind = kind,
                IsConnected = false,
                TimeRemaining = null,
                TimeToFull = null,
            };
        }

        var reading = snapshot.Reading;
        return new BatteryDevice
        {
            Id = id,
            Name = name,
            Kind = kind,
            LevelPercent = reading.Percent,
            ChargeState = ChargeStateOf(reading.Status),
            IsConnected = !snapshot.IsStale,
            TimeRemaining = Positive(reading.TimeToEmpty),
            TimeToFull = Positive(reading.TimeToFull),
        };
    }

    public static string KindOf(BatterySourceKind kind) =>
        kind switch
        {
            BatterySourceKind.System => BatteryDeviceKinds.Laptop,
            BatterySourceKind.Mouse => BatteryDeviceKinds.Mouse,
            BatterySourceKind.Keyboard => BatteryDeviceKinds.Keyboard,
            BatterySourceKind.Headset or BatterySourceKind.Earbuds => BatteryDeviceKinds.Headphones,
            BatterySourceKind.Phone => BatteryDeviceKinds.Phone,
            BatterySourceKind.Tablet => BatteryDeviceKinds.Tablet,
            BatterySourceKind.Controller => BatteryDeviceKinds.Controller,
            BatterySourceKind.Speaker => BatteryDeviceKinds.Speaker,
            _ => BatteryDeviceKinds.Other,
        };

    private static BatteryChargeState ChargeStateOf(BatteryStatus status) =>
        status switch
        {
            BatteryStatus.Discharging => BatteryChargeState.Discharging,
            BatteryStatus.Charging => BatteryChargeState.Charging,
            BatteryStatus.Full => BatteryChargeState.Full,
            _ => BatteryChargeState.Unknown,
        };

    private static TimeSpan? Positive(TimeSpan? value) =>
        value is { } span && span > TimeSpan.Zero ? span : null;

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
