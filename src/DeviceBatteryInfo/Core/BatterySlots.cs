using System.Text;

namespace DeviceBatteryInfo.Core;

// Id is a public API: it's the prefix of every battery_<id>_* variable a user binds to.
public sealed record BatterySlot(
    string Id,
    string DisplayName,
    BatterySourceKind Kind,
    DeviceType Type,
    string? BluetoothFriendlyName = null,
    string? AdbAddress = null,
    string? CatalogDeviceId = null
);

public static class BatterySlots
{
    public static string Reserve(string desiredId, HashSet<string> used)
    {
        var id = string.IsNullOrEmpty(desiredId) ? "device" : desiredId;
        var candidate = id;
        for (var suffix = 2; !used.Add(candidate); suffix++)
        {
            candidate = $"{id}-{suffix}";
        }

        return candidate;
    }

    public static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length > 0 ? slug : "device";
    }
}
