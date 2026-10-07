using System.Text.Json;
using DeviceBatteryInfo.Core;
using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace DeviceBatteryInfo.Ui;

internal static class BatteryWidgetConfigView
{
    public static UiElement Build(
        string widgetLocalId,
        BatteryWidgetOptions current,
        IReadOnlyList<BatterySlot> devices,
        JsonElement data = default
    )
    {
        var flows = BatteryWidgetTypes.StoredFlows(data);
        var isPanel = widgetLocalId != BatteryWidgetTypes.TileId;
        var sourceIds = new UiState<IReadOnlyList<string>>(current.SourceIds);
        var layout = new UiState<string>(BatteryWidgetOptions.LayoutValue(current.Layout));
        var showNames = new UiState<bool>(current.ShowNames);
        var showBar = new UiState<bool>(current.ShowBar);
        var showPercent = new UiState<bool>(current.ShowPercent);
        var showCharging = new UiState<bool>(current.ShowCharging);
        var showTimeToFull = new UiState<bool>(current.ShowTimeToFull);
        var showTrend = new UiState<bool>(current.ShowTrend);
        var showRingTrend = new UiState<bool>(current.ShowRingTrend);
        var lowThreshold = new UiState<double>(current.LowThreshold);
        var sort = new UiState<string>(BatteryWidgetOptions.SortValue(current.Sort));
        var title = new UiState<string>(current.Title);
        var colors = new UiState<string>(BatteryWidgetOptions.ColorsValue(current.Colors));
        var listAlign = new UiState<string>(
            BatteryWidgetOptions.ListAlignValue(current.ListAlign)
        );
        // A default JsonElement cannot be serialized into the tree.
        var flowList = new UiState<JsonElement>(
            flows.ValueKind == JsonValueKind.Array ? flows : EmptyFlows
        );

        // Only the panel has a layout to depend on; the tile shows every field.
        UiValue<UiVisibleWhen> OnlyFor(string layoutValue) =>
            isPanel
                ? UiValue.Of(
                    new UiVisibleWhen
                    {
                        ParameterName = "layout",
                        Values = [layoutValue],
                        SiblingValue = () => layout.Value,
                    }
                )
                : UiValue.None<UiVisibleWhen>();

        var content = new List<UiElement>();
        if (isPanel)
        {
            content.Add(
                new UiStringInput
                {
                    Key = "title",
                    Label = Strings.Widgets.Config.Title.Label(),
                    Description = Strings.Widgets.Config.Title.Description(),
                    Binding = Bind.To(title),
                }
            );
        }

        content.Add(
            new UiMultiSelectInput
            {
                Key = "sourceIds",
                Label = Strings.Widgets.Config.Devices.Label(),
                Options = UiValue.Of<IReadOnlyList<UiOption>>(
                    devices.Select(d => UiOption.Of(d.Id) with { Label = d.DisplayName }).ToArray()
                ),
                Binding = Bind.To(sourceIds),
                Reorderable = UiValue.Of(true),
            }
        );
        content.Add(
            new UiChoiceInput
            {
                Key = "sort",
                Label = Strings.Widgets.Config.Sort.Label(),
                Options = Options(
                    (BatteryWidgetOptions.SortManual, Strings.Widgets.Config.Sort.Manual()),
                    (BatteryWidgetOptions.SortLowestFirst, Strings.Widgets.Config.Sort.LowestFirst()),
                    (BatteryWidgetOptions.SortChargingFirst, Strings.Widgets.Config.Sort.ChargingFirst()),
                    (BatteryWidgetOptions.SortAlphabetical, Strings.Widgets.Config.Sort.Alphabetical())
                ),
                Binding = Bind.To(sort),
            }
        );

        var appearance = new List<UiElement>();
        if (isPanel)
        {
            appearance.Add(
                Row(
                    "layout-row",
                    new UiChoiceInput
                    {
                        Key = "layout",
                        Label = Strings.Widgets.Config.Layout.Label(),
                        Segmented = true,
                        Options = UiValue.Of<IReadOnlyList<UiOption>>(
                            [
                                IconOption(
                                    BatteryWidgetOptions.LayoutRings,
                                    Strings.Widgets.Config.Layout.Rings(),
                                    UiIcons.Disc
                                ),
                                IconOption(
                                    BatteryWidgetOptions.LayoutList,
                                    Strings.Widgets.Config.Layout.List(),
                                    UiIcons.AlignLeft
                                ),
                            ]
                        ),
                        Binding = Bind.To(layout),
                        RowWeight = 1,
                    },
                    new UiChoiceInput
                    {
                        Key = "listAlign",
                        Label = Strings.Widgets.Config.ListAlign.Label(),
                        Segmented = true,
                        Options = UiValue.Of<IReadOnlyList<UiOption>>(
                            [
                                IconOption(
                                    BatteryWidgetOptions.ListAlignTop,
                                    Strings.Widgets.Config.ListAlign.Top(),
                                    UiIcons.AlignTop
                                ),
                                IconOption(
                                    BatteryWidgetOptions.ListAlignCenter,
                                    Strings.Widgets.Config.ListAlign.Center(),
                                    UiIcons.AlignMiddle
                                ),
                                IconOption(
                                    BatteryWidgetOptions.ListAlignBottom,
                                    Strings.Widgets.Config.ListAlign.Bottom(),
                                    UiIcons.AlignBottom
                                ),
                            ]
                        ),
                        Binding = Bind.To(listAlign),
                        VisibleWhen = OnlyFor(BatteryWidgetOptions.LayoutList),
                        RowWeight = 1,
                    }
                )
            );
        }

        appearance.Add(
            new UiChoiceInput
            {
                Key = "colors",
                Label = Strings.Widgets.Config.Colors.Label(),
                Options = Options(
                    (
                        BatteryWidgetOptions.ColorsLevelsCharging,
                        Strings.Widgets.Config.Colors.LevelsCharging()
                    ),
                    (BatteryWidgetOptions.ColorsLevels, Strings.Widgets.Config.Colors.Levels()),
                    (BatteryWidgetOptions.ColorsSimple, Strings.Widgets.Config.Colors.Simple()),
                    (BatteryWidgetOptions.ColorsDevice, Strings.Widgets.Config.Colors.Device()),
                    (BatteryWidgetOptions.ColorsGradient, Strings.Widgets.Config.Colors.Gradient())
                ),
                Binding = Bind.To(colors),
            }
        );
        appearance.Add(
            new UiNumberInput
            {
                Key = "lowThreshold",
                Label = Strings.Widgets.Config.LowThreshold.Label(),
                Min = 1,
                Max = 99,
                Step = 1,
                ShowSlider = true,
                Binding = Bind.To(lowThreshold),
            }
        );

        appearance.AddRange(
            [
                new UiHeading { Key = "show-heading", Text = Strings.Widgets.Config.Show.Heading() },
                Toggle("showBar", Strings.Widgets.Config.ShowBar.Label(), showBar),
                Toggle("showPercent", Strings.Widgets.Config.ShowPercent.Label(), showPercent),
                Toggle("showCharging", Strings.Widgets.Config.ShowCharging.Label(), showCharging),
                // A caption needs the list; the rings layout has room only for its own short trend line.
                Toggle(
                    "showTimeToFull",
                    Strings.Widgets.Config.ShowTimeToFull.Label(),
                    showTimeToFull,
                    OnlyFor(BatteryWidgetOptions.LayoutList)
                ),
                Toggle(
                    "showTrend",
                    Strings.Widgets.Config.ShowTrend.Label(),
                    showTrend,
                    OnlyFor(BatteryWidgetOptions.LayoutList)
                ),
            ]
        );

        if (isPanel)
        {
            appearance.Add(
                Toggle(
                    "showNames",
                    Strings.Widgets.Config.ShowNames.Label(),
                    showNames,
                    OnlyFor(BatteryWidgetOptions.LayoutRings)
                )
            );
            appearance.Add(
                Toggle(
                    "showRingTrend",
                    Strings.Widgets.Config.ShowTrend.Label(),
                    showRingTrend,
                    OnlyFor(BatteryWidgetOptions.LayoutRings)
                )
            );
        }

        // Macro Deck draws the border itself from the stored "border" key.
        appearance.Add(UiWidgetAppearance.Section(data, UiWidgetAppearanceFields.Border));

        return new UiWidgetConfiguration
        {
            Key = "battery-widget-config",
            Properties = new UiWidgetProperties
            {
                Key = "battery-widget-config-properties",
                Children =
                [
                    new UiTabs
                    {
                        Key = "sections",
                        Children =
                        [
                            Tab("content-tab", Strings.Widgets.Config.Tabs.Devices(), content),
                            Tab("appearance-tab", Strings.Widgets.Config.Tabs.Appearance(), appearance),
                        ],
                    },
                ],
            },
            // The host reads flows from the top-level "flows" key, and an editor region is what makes
            // the desktop draw the split layout.
            Editor = new UiWidgetEditor
            {
                Key = "battery-widget-config-editor",
                Children =
                [
                    new UiActionsListEditor
                    {
                        Key = "flows",
                        Binding = Bind.To(flowList),
                        CanRun = true,
                    },
                ],
            },
        };
    }

    private static readonly JsonElement EmptyFlows = JsonDocument.Parse("[]").RootElement.Clone();

    private static UiTab Tab(string key, UiText label, IReadOnlyList<UiElement> children) =>
        new()
        {
            Key = key,
            Label = label,
            Children = children,
        };

    private static UiConfigStack Row(string key, params UiElement[] children) =>
        new()
        {
            Key = key,
            Direction = UiComponentDirections.Horizontal,
            Wrap = false,
            Children = children,
        };

    private static UiBooleanInput Toggle(
        string key,
        UiText label,
        UiState<bool> state,
        UiValue<UiVisibleWhen> visibleWhen = default
    ) =>
        new()
        {
            Key = key,
            Label = label,
            Binding = Bind.To(state),
            VisibleWhen = visibleWhen,
        };

    private static UiValue<IReadOnlyList<UiOption>> Options(
        params (string Value, LocalizedText Label)[] options
    ) =>
        UiValue.Of<IReadOnlyList<UiOption>>(
            options.Select(o => UiOption.Of(o.Value) with { Label = o.Label }).ToArray()
        );

    private static UiOption IconOption(
        string value,
        LocalizedText label,
        string icon
    ) => UiOption.Of(value) with { Label = label, Icon = icon };
}
