using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.Variables;

namespace DeviceBatteryInfo.Variables;

// Field suffixes are a public API baked into every binding a user makes. Do not rename them.
internal static class BatteryVariableCatalog
{
    public enum Field
    {
        Percent,
        Status,
        Charging,
        Online,
        TimeToFull,
        Trend,
        TrendRate,
    }

    private static readonly (Field Field, string Suffix, VariableType Type)[] Fields =
    [
        (Field.Percent, "percent", VariableType.Numeric),
        (Field.Status, "status", VariableType.Text),
        (Field.Charging, "charging", VariableType.Boolean),
        (Field.Online, "online", VariableType.Boolean),
        (Field.TimeToFull, "time-to-full", VariableType.Text),
        (Field.Trend, "trend", VariableType.Text),
        (Field.TrendRate, "trend-rate", VariableType.Numeric),
    ];

    public static int FieldsPerDevice => Fields.Length;

    // Longest suffix first so "time-to-full" is matched before a shorter suffix could.
    private static readonly (Field Field, string Suffix)[] SuffixesByLength =
    [
        .. Fields.Select(f => (f.Field, f.Suffix)).OrderByDescending(f => f.Suffix.Length),
    ];

    public static IReadOnlyList<VariableDefinition> Build(IReadOnlyList<BatterySlot> slots)
    {
        var definitions = new List<VariableDefinition>(slots.Count * Fields.Length);
        foreach (var slot in slots)
        {
            foreach (var (field, suffix, type) in Fields)
            {
                definitions.Add(Define(slot, field, suffix, type));
            }
        }

        return definitions;
    }

    public static IEnumerable<(string LocalId, Field Field)> FieldsFor(string slotId) =>
        Fields.Select(f => ($"{slotId}-{f.Suffix}", f.Field));

    public static bool TryResolve(string localId, out string slotId, out Field field)
    {
        foreach (var (candidate, suffix) in SuffixesByLength)
        {
            if (
                localId.Length > suffix.Length + 1
                && localId.EndsWith('-' + suffix, StringComparison.Ordinal)
            )
            {
                slotId = localId[..^(suffix.Length + 1)];
                field = candidate;
                return true;
            }
        }

        slotId = string.Empty;
        field = default;
        return false;
    }

    private static VariableDefinition Define(
        BatterySlot slot,
        Field field,
        string suffix,
        VariableType type
    )
    {
        var device = slot.DisplayName;
        var (displayName, description) = field switch
        {
            Field.Percent => (
                Strings.Variables.Percent.DisplayName(device),
                Strings.Variables.Percent.Description(device)
            ),
            Field.Status => (
                Strings.Variables.Status.DisplayName(device),
                Strings.Variables.Status.Description(device)
            ),
            Field.Charging => (
                Strings.Variables.Charging.DisplayName(device),
                Strings.Variables.Charging.Description(device)
            ),
            Field.Online => (
                Strings.Variables.Online.DisplayName(device),
                Strings.Variables.Online.Description(device)
            ),
            Field.TimeToFull => (
                Strings.Variables.TimeToFull.DisplayName(device),
                Strings.Variables.TimeToFull.Description(device)
            ),
            Field.Trend => (
                Strings.Variables.Trend.DisplayName(device),
                Strings.Variables.Trend.Description(device)
            ),
            Field.TrendRate => (
                Strings.Variables.TrendRate.DisplayName(device),
                Strings.Variables.TrendRate.Description(device)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        return new VariableDefinition
        {
            Id = $"{slot.Id}-{suffix}",
            Name = $"battery_{slot.Id.Replace('-', '_')}_{suffix.Replace('-', '_')}",
            Type = type,
            Materialization = VariableMaterialization.OnDemand,
            DisplayName = displayName,
            Description = description,
            DecimalPlaces = field switch
            {
                Field.Percent => 0,
                Field.TrendRate => 1,
                _ => null,
            },
            Unit = field switch
            {
                Field.Percent => "%",
                Field.TrendRate => "%/h",
                _ => null,
            },
            SemanticKind = field == Field.Percent ? VariableSemanticKinds.Percentage : null,
        };
    }
}
