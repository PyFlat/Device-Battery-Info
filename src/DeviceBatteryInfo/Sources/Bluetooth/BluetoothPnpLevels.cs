using System.Text.RegularExpressions;

namespace DeviceBatteryInfo.Sources.Bluetooth;

// The level sits on a sibling node sharing the device's address, never on the root the user picks.
internal static partial class BluetoothPnpLevels
{
    // Anchored, because every SDP GUID ends in the Base UUID, which is also 12 hex digits.
    [GeneratedRegex("(?:DEV_|&0&)([0-9A-Fa-f]{12})")]
    private static partial Regex AddressPattern();

    [GeneratedRegex(@"^(BTHENUM|BTHLE)\\DEV_([0-9A-Fa-f]{12})", RegexOptions.IgnoreCase)]
    private static partial Regex RootPattern();

    // Without one of these profiles a device never gets a level.
    private static readonly string[] BatteryProfiles = ["0000111E", "0000111F", "0000180F"];

    // Every named node, so a hand-typed sibling name such as "<device> Hands-Free AG" resolves too.
    public static IReadOnlyList<(string Name, string? RawBattery)> ByName(IReadOnlyList<PnpNode> nodes)
    {
        var addresses = nodes
            .SelectMany(n => AddressPattern().Matches(n.InstanceId))
            .Select(m => m.Groups[1].Value.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var levelByAddress = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var address in addresses)
        {
            var level = Siblings(nodes, address)
                .OrderBy(n => IsBle(n) ? 0 : 1)
                .Select(n => n.RawBattery)
                .FirstOrDefault(raw => !string.IsNullOrEmpty(raw));
            if (level is not null)
            {
                levelByAddress[address] = level;
            }
        }

        var levelByName = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.Name))
            {
                continue;
            }

            var level = addresses
                .Where(a => Contains(node.InstanceId, a))
                .Select(levelByAddress.GetValueOrDefault)
                .FirstOrDefault(raw => raw is not null);
            if (level is not null || !levelByName.ContainsKey(node.Name))
            {
                levelByName[node.Name] = level;
            }
        }

        return [.. levelByName.Select(p => (p.Key, p.Value))];
    }

    public static IReadOnlyList<(string Name, string? RawBattery)> Pickable(IReadOnlyList<PnpNode> nodes)
    {
        var byName = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in nodes)
        {
            if (string.IsNullOrEmpty(root.Name) || RootPattern().Match(root.InstanceId) is not { Success: true } match)
            {
                continue;
            }

            var siblings = Siblings(nodes, match.Groups[2].Value).ToArray();
            if (!siblings.Any(s => BatteryProfiles.Any(p => Contains(s.InstanceId, p))))
            {
                continue;
            }

            var level = siblings.Select(s => s.RawBattery).FirstOrDefault(raw => !string.IsNullOrEmpty(raw));
            var known = byName.TryGetValue(root.Name, out var existing);
            if (!known || existing is null || (IsBle(root) && level is not null))
            {
                byName[root.Name] = level;
            }
        }

        return
        [
            .. byName
                .OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => (p.Key, p.Value)),
        ];
    }

    private static IEnumerable<PnpNode> Siblings(IReadOnlyList<PnpNode> nodes, string address) =>
        nodes.Where(n => Contains(n.InstanceId, address));

    private static bool IsBle(PnpNode node) =>
        node.InstanceId.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string instanceId, string fragment) =>
        instanceId.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
