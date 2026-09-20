namespace DeviceBatteryInfo.Core;

// Devices are normally added as a device family (IDeviceFamily), which produces these.
public interface IBatterySource
{
    // Lowercase kebab-case, unique, and a public API: it prefixes every battery_<id>_* variable, so
    // changing it breaks bindings users already made.
    string Id { get; }

    string DisplayName { get; }

    BatterySourceKind Kind { get; }

    ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken);
}
