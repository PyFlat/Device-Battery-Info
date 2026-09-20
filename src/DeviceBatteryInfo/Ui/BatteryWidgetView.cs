using DeviceBatteryInfo.Core;
using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.References;
using MacroDeck.Ui.Runtime;

namespace DeviceBatteryInfo.Ui;

internal static class BatteryWidgetView
{
    private const double BreathingRoomPx = 8;
    private static readonly double CornerClearance = 1 - (1 / Math.Sqrt(2));

    public static UiElement Build(
        string widgetLocalId,
        UiState<BatteryWidgetModel> state,
        int cornerRadius,
        Action? onPress = null
    )
    {
        UiStack root =
            widgetLocalId == BatteryWidgetTypes.TileId
                ? Tile(state, cornerRadius)
                : Panel(state, cornerRadius);

        return onPress is null
            ? root
            : root with
            {
                Events = [UiEventHandler.On(UiComponentEvents.Press, onPress)],
            };
    }

    private static UiSize SafeArea(int cornerRadius)
    {
        var inset = Math.Max(BreathingRoomPx, CornerClearance * Math.Max(0, cornerRadius));
        return UiSize.Of(UiLength.Capped(inset / UiLength.Cell, inset));
    }

    private static UiStack Panel(UiState<BatteryWidgetModel> state, int cornerRadius)
    {
        var body = new UiStack
        {
            Key = "body",
            Direction = UiComponentDirections.Vertical,
            Justify = UiComponentJustify.Center,
            Fill = true,
            Gap = 0.035,
            Children =
            [
                new UiRepeat<BatteryWidgetRow>
                {
                    Key = "rows",
                    Items = UiValue.From(() => state.Value.Rows),
                    KeySelector = row => row.Id,
                    Template = (row, key) => Row(row, key, state.Value.Options),
                },
                new UiWhen
                {
                    Key = "empty",
                    Condition = () => state.Value.Rows.Count == 0,
                    Content = () =>
                        new UiStack
                        {
                            Key = "empty-wrap",
                            Direction = UiComponentDirections.Vertical,
                            Justify = UiComponentJustify.Center,
                            Fill = true,
                            Children =
                            [
                                new UiTextRun
                                {
                                    Key = "empty-text",
                                    Text = Strings.Widgets.Empty(),
                                    Size = UiSize.FromBasis(0.085, 0.4),
                                    MinSize = 0.05,
                                    Role = UiComponentTextRoles.Muted,
                                    Align = UiComponentAlignments.Center,
                                },
                            ],
                        },
                },
            ],
        };

        var children = new List<UiElement>();
        var title = state.Value.Options.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            children.Add(
                new UiTextRun
                {
                    Key = "title",
                    Text = title.Trim(),
                    Size = UiSize.FromBasis(0.072, 0.9),
                    MinSize = 0.045,
                    Weight = UiComponentTextWeights.SemiBold,
                    Role = UiComponentTextRoles.Muted,
                    MaxLines = 1,
                    Wrap = false,
                }
            );
        }

        children.Add(body);

        return new UiStack
        {
            Key = "battery-panel",
            Direction = UiComponentDirections.Vertical,
            Justify = UiComponentJustify.Start,
            Fill = true,
            Padding = SafeArea(cornerRadius),
            Gap = 0.035,
            Children = children,
        };
    }

    private static UiStack Row(BatteryWidgetRow row, string key, BatteryWidgetOptions options)
    {
        var color = row.Color(options.LowThreshold);
        var caption =
            options.ShowCharging || options.ShowTimeToFull || options.ShowTrend
                ? Caption(row, options)
                : null;

        var nameGroup = new UiStack
        {
            Key = "namegroup",
            Direction = UiComponentDirections.Horizontal,
            Align = UiComponentAlignments.Baseline,
            Gap = 0.02,
            Children = caption is { } captionText
                ?
                [
                    NameText(row.Name),
                    new UiTextRun
                    {
                        Key = "state",
                        Text = captionText,
                        Size = UiSize.FromBasis(0.058, 0.34),
                        MinSize = 0.04,
                        Role = UiComponentTextRoles.Muted,
                        MaxLines = 1,
                        Wrap = false,
                    },
                ]
                : [NameText(row.Name)],
        };

        var headline = new UiStack
        {
            Key = "line",
            Direction = UiComponentDirections.Horizontal,
            Align = UiComponentAlignments.Baseline,
            Justify = UiComponentJustify.SpaceBetween,
            Gap = 0.03,
            Children =
            [
                nameGroup,
                new UiTextRun
                {
                    Key = "pct",
                    Text = options.ShowPercent ? row.PercentText() : string.Empty,
                    Size = UiSize.FromBasis(0.1, 0.62),
                    MinSize = 0.055,
                    Digits = 4,
                    Weight = UiComponentTextWeights.SemiBold,
                    Color = color,
                    Align = UiComponentAlignments.End,
                },
            ],
        };

        var children = new List<UiElement> { headline };

        if (options.ShowBar && row.Percent is not null)
        {
            children.Add(
                new UiProgressBar
                {
                    Key = "bar",
                    Fill = true,
                    MainSize = 0.045,
                    Thickness = 0.024,
                    Value = Progress(row.Percent.Value),
                    StartColor = color,
                    EndColor = color,
                }
            );
        }

        return new UiStack
        {
            Key = key,
            Direction = UiComponentDirections.Vertical,
            Gap = 0.016,
            Children = children,
        };
    }

    private static UiTextRun NameText(string name) =>
        new()
        {
            Key = "name",
            Text = name,
            Size = UiSize.FromBasis(0.082, 0.44),
            MinSize = 0.048,
            Weight = UiComponentTextWeights.Medium,
            Role = UiComponentTextRoles.Secondary,
            MaxLines = 1,
            Wrap = false,
        };

    private static UiStack Tile(UiState<BatteryWidgetModel> state, int cornerRadius) =>
        new()
        {
            Key = "battery-tile",
            Direction = UiComponentDirections.Vertical,
            Align = UiComponentAlignments.Stretch,
            Justify = UiComponentJustify.Center,
            Fill = true,
            Padding = SafeArea(cornerRadius),
            Gap = 0.028,
            Children =
            [
                new UiRepeat<BatteryWidgetRow>
                {
                    Key = "tile-row",
                    Items = UiValue.From(() => FirstRow(state.Value.Rows)),
                    KeySelector = row => row.Id,
                    Template = (row, key) => TileBody(row, key, state.Value.Options),
                },
                new UiWhen
                {
                    Key = "tile-empty",
                    Condition = () => state.Value.Rows.Count == 0,
                    Content = () =>
                        new UiTextRun
                        {
                            Key = "tile-empty-text",
                            Text = Strings.Widgets.Empty(),
                            Size = UiSize.FromBasis(0.09, 0.5),
                            MinSize = 0.05,
                            Role = UiComponentTextRoles.Muted,
                            Align = UiComponentAlignments.Center,
                        },
                },
            ],
        };

    private static IReadOnlyList<BatteryWidgetRow> FirstRow(IReadOnlyList<BatteryWidgetRow> rows) =>
        rows.Count == 0 ? [] : [rows[0]];

    private static UiStack TileBody(BatteryWidgetRow row, string key, BatteryWidgetOptions options)
    {
        var color = row.Color(options.LowThreshold);
        var children = new List<UiElement>
        {
            new UiTextRun
            {
                Key = "name",
                Text = row.Name,
                Size = UiSize.FromBasis(0.095, 0.9),
                MinSize = 0.055,
                Role = UiComponentTextRoles.Secondary,
                Weight = UiComponentTextWeights.Medium,
                MaxLines = 1,
                Align = UiComponentAlignments.Center,
            },
            new UiTextRun
            {
                Key = "pct",
                Text = row.PercentText(),
                Size = UiSize.FromBasis(0.3, 0.66),
                MinSize = 0.14,
                Digits = 4,
                Weight = UiComponentTextWeights.Bold,
                Color = color,
                Align = UiComponentAlignments.Center,
            },
        };

        if (options.ShowBar && row.Percent is not null)
        {
            children.Add(
                new UiProgressBar
                {
                    Key = "bar",
                    MainSize = 0.07,
                    Thickness = 0.04,
                    Value = Progress(row.Percent.Value),
                    StartColor = color,
                    EndColor = color,
                }
            );
        }

        var caption =
            options.ShowCharging || options.ShowTimeToFull || options.ShowTrend
                ? Caption(row, options)
                : null;
        if (caption is { } captionText)
        {
            children.Add(
                new UiTextRun
                {
                    Key = "caption",
                    Text = captionText,
                    Size = UiSize.FromBasis(0.072, 0.9),
                    MinSize = 0.045,
                    Role = UiComponentTextRoles.Muted,
                    MaxLines = 1,
                    Align = UiComponentAlignments.Center,
                }
            );
        }

        return new UiStack
        {
            Key = key,
            Direction = UiComponentDirections.Vertical,
            Align = UiComponentAlignments.Stretch,
            Justify = UiComponentJustify.Center,
            Gap = 0.028,
            Fill = true,
            Children = children,
        };
    }

    private static UiProgressReference Progress(int percent) =>
        UiProgressReference.Halted(Math.Clamp(percent, 0, 100), DateTimeOffset.UtcNow, 100);

    private static LocalizedText? Caption(BatteryWidgetRow row, BatteryWidgetOptions options)
    {
        if (row.Stale)
        {
            return Strings.Widgets.Caption.NoSignal();
        }

        if (row.Charging)
        {
            if (options.ShowTimeToFull && !string.IsNullOrEmpty(row.TimeToFull))
            {
                return Strings.Widgets.Caption.ChargingEta(row.TimeToFull!);
            }

            return options.ShowTrend && !string.IsNullOrEmpty(row.Trend)
                ? row.Trend!
                : Strings.Widgets.Caption.Charging();
        }

        if (row.Status == BatteryStatus.Full)
        {
            return Strings.Widgets.Caption.Full();
        }

        return options.ShowTrend && !string.IsNullOrEmpty(row.Trend) ? row.Trend! : null;
    }
}
