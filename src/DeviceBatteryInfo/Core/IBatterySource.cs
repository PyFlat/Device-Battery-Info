namespace DeviceBatteryInfo.Core;

/// <summary>One battery whose level can be polled. Devices are normally added as a device family
/// (<c>IDeviceFamily</c>), which produces these - see <c>docs/adding-a-device.md</c>.</summary>
public interface IBatterySource
{
    // Lowercase kebab-case, unique, and a public API: it prefixes every battery_<id>_* variable, so
    // changing it breaks bindings users already made.
    string Id { get; }

    string DisplayName { get; }

    BatterySourceKind Kind { get; }

    ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken);
}
