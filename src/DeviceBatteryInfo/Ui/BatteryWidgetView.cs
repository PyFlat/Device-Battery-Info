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

    // Fractions of the view basis.
    private const double EdgeInset = 0.06;
    private const double TitleHeight = 0.12;
    private const double GridGap = 0.06;

    // Fractions of the ring's diameter. The inset centres the stroke on the bolt, so the bolt fills
    // the charging gap.
    private const double RingThickness = 0.085;
    private const double BoltSize = 0.22;
    private const double GaugeInset = (BoltSize - RingThickness) / 2;
    private const double ChargingGapDegrees = 20;

    // Small enough that "100%" clears the ring's inner circle.
    private const double FaceGlyph = 0.28;
    private const double FaceGlyphAlone = 0.44;

    // With the name inside, the glyph gives up height; the name line stays within the inner circle's
    // chord at the bottom of the face.
    private const double FaceGlyphNamed = 0.19;
    private const double FaceGlyphNamedAlone = 0.3;
    private const double FaceName = 0.11;
    private const double FaceNameMin = 0.075;
    private const double FaceNameWidth = 0.46;
    private const double FacePercent = 0.16;
    private const double NameShare = 0.24;
    private const double TrendShare = 0.19;

    // An aspect range of the rings' box and the aspect the ring sizes assume for it.
    private static readonly (double? Min, double? Max, double Aspect)[] AspectBuckets =
    [
        (null, 0.7, 0.5),
        (1.3, 1.8, 1.5),
        (1.8, 2.6, 2.2),
        (2.6, 3.6, 3.1),
        (3.6, null, 4.4),
    ];

    // The tree declares no press event: one would claim the gesture, and the host would then skip the
    // widget's flows and its default refresh action.
    public static UiElement Build(
        string widgetLocalId,
        UiState<BatteryWidgetModel> state,
        int cornerRadius
    ) =>
        widgetLocalId == BatteryWidgetTypes.TileId
            ? Tile(state, cornerRadius)
            : Panel(state, cornerRadius);

    private static UiSize SafeArea(int cornerRadius)
    {
        var inset = Math.Max(BreathingRoomPx, CornerClearance * Math.Max(0, cornerRadius));
        return UiSize.Of(UiLength.Capped(inset / UiLength.Cell, inset));
    }

    // Passed to the root as stored: "transparent" there also drops the deck's tile face.
    private static UiValue<string> Background(BatteryWidgetOptions options) =>
        options.BackgroundColor is { } color ? UiValue.Of(color) : UiValue.None<string>();

    // One stored text colour. The renderer takes #rrggbb only, so secondary and muted text are mixed
    // toward a known opaque background; over the theme's face or a transparent one they use it as is.
    private static UiValue<string> TextColor(BatteryWidgetOptions options, string role)
    {
        if (options.TextColor is not { } color)
        {
            return UiValue.None<string>();
        }

        if (
            role == UiComponentTextRoles.Primary
            || !IsOpaqueHex(color)
            || options.BackgroundColor is not { } background
            || !IsOpaqueHex(background)
        )
        {
            return UiValue.Of(color);
        }

        return UiValue.Of(Mix(color, background, role == UiComponentTextRoles.Secondary ? 0.8 : 0.6));
    }

    private static string Mix(string color, string background, double weight)
    {
        int Channel(string hex, int index) =>
            int.Parse(
                hex.AsSpan(1 + (2 * index), 2),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture
            );

        return "#"
            + string.Concat(
                Enumerable
                    .Range(0, 3)
                    .Select(i =>
                        ((int)Math.Round(
                            (Channel(color, i) * weight) + (Channel(background, i) * (1 - weight))
                        )).ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
                    )
            );
    }

    private static string PercentRole(BatteryWidgetRow row) =>
        row.Stale ? UiComponentTextRoles.Muted : UiComponentTextRoles.Primary;

    private static bool IsOpaqueHex(string color) =>
        color.Length == 7 && color[0] == '#' && color.Skip(1).All(char.IsAsciiHexDigit);

    private static UiStack Panel(UiState<BatteryWidgetModel> state, int cornerRadius)
    {
        var options = state.Value.Options;
        var title = options.Title.Trim();
        var hasTitle = title.Length > 0;

        var children = new List<UiElement>();
        if (hasTitle)
        {
            children.Add(
                new UiTextRun
                {
                    Key = "title",
                    Text = title,
                    Size = UiSize.FromBasis(0.072, 0.9),
                    MinSize = 0.045,
                    Weight = UiComponentTextWeights.SemiBold,
                    Role = UiComponentTextRoles.Muted,
                    Color = TextColor(options, UiComponentTextRoles.Muted),
                    MaxLines = 1,
                    Wrap = false,
                }
            );
        }

        children.Add(
            new UiWhen
            {
                Key = "filled",
                Condition = () => state.Value.Rows.Count > 0,
                Content = () =>
                    options.Layout == BatteryWidgetLayout.List
                        ? ListBody(state)
                        : RingBody(state, hasTitle),
            }
        );
        children.Add(
            new UiWhen
            {
                Key = "empty",
                Condition = () => state.Value.Rows.Count == 0,
                Content = () => EmptyText("empty-text", 0.085, options),
            }
        );

        return new UiStack
        {
            Key = "battery-panel",
            Direction = UiComponentDirections.Vertical,
            Justify = UiComponentJustify.Start,
            Fill = true,
            Padding = SafeArea(cornerRadius),
            Background = Background(options),
            Gap = 0.035,
            Children = children,
        };
    }

    private static UiStack EmptyText(string key, double size, BatteryWidgetOptions options) =>
        new()
        {
            Key = key + "-wrap",
            Direction = UiComponentDirections.Vertical,
            Justify = UiComponentJustify.Center,
            Fill = true,
            Children =
            [
                new UiTextRun
                {
                    Key = key,
                    Text = Strings.Widgets.Empty(),
                    Size = UiSize.FromBasis(size, 0.4),
                    MinSize = 0.05,
                    Role = UiComponentTextRoles.Muted,
                    Color = TextColor(options, UiComponentTextRoles.Muted),
                    Align = UiComponentAlignments.Center,
                    Wrap = true,
                    MaxLines = 3,
                },
            ],
        };

    private static UiResponsive RingBody(UiState<BatteryWidgetModel> state, bool hasTitle) =>
        new()
        {
            Key = "rings",
            Fill = true,
            Default = RingGrid(state, "rings-1", 1, hasTitle),
            Variants = AspectBuckets
                .Select(
                    (bucket, index) =>
                        new UiResponsiveVariant
                        {
                            MinAspect = bucket.Min,
                            MaxAspect = bucket.Max,
                            Content = RingGrid(state, $"rings-{index + 2}", bucket.Aspect, hasTitle),
                        }
                )
                .ToArray(),
        };

    private static UiGrid RingGrid(
        UiState<BatteryWidgetModel> state,
        string key,
        double aspect,
        bool hasTitle
    )
    {
        RingArrangement Arrangement() =>
            Arrange(
                state.Value.Rows.Count,
                aspect,
                hasTitle,
                state.Value.Options.ShowNames && !state.Value.Options.NameInRing,
                state.Value.Options.ShowRingTrend
            );

        return new UiGrid
        {
            Key = key,
            Columns = UiValue.From(() => Arrangement().Columns),
            Rows = UiValue.From(() => Arrangement().Rows),
            Gap = UiSize.FromBasis(GridGap),
            Children =
            [
                new UiRepeat<BatteryWidgetRow>
                {
                    Key = "cells",
                    Items = UiValue.From(() => state.Value.Rows),
                    KeySelector = row => row.Id,
                    Template = (row, rowKey) =>
                        RingCell(row, rowKey, state.Value.Options, () => Arrangement().Diameter),
                },
            ],
        };
    }

    internal readonly record struct RingArrangement(int Columns, int Rows, double Diameter);

    // A tie goes to more columns, so two rings sit side by side.
    internal static RingArrangement Arrange(
        int count,
        double aspect,
        bool hasTitle,
        bool showNames,
        bool showTrend = false
    )
    {
        var shortSide =
            1 - (2 * EdgeInset) - (hasTitle && aspect >= 1 ? TitleHeight : 0);
        var width = Math.Max(aspect, 1) * shortSide;
        var height = Math.Max(1 / aspect, 1) * shortSide;
        var labelFactor = 1 + (showNames ? NameShare : 0) + (showTrend ? TrendShare : 0);

        var devices = Math.Max(count, 1);
        var best = new RingArrangement(1, 1, 0);
        for (var columns = 1; columns <= devices; columns++)
        {
            var rows = (devices + columns - 1) / columns;
            var cellWidth = (width - ((columns - 1) * GridGap)) / columns;
            var cellHeight = (height - ((rows - 1) * GridGap)) / rows;
            var diameter = Math.Min(cellWidth, cellHeight / labelFactor);
            if (diameter >= best.Diameter - 1e-6)
            {
                best = new RingArrangement(columns, rows, diameter);
            }
        }

        return best;
    }

    private static UiStack RingCell(
        BatteryWidgetRow row,
        string key,
        BatteryWidgetOptions options,
        Func<double> diameter
    )
    {
        var nameInside = options.ShowNames && options.NameInRing;
        var children = new List<UiElement>
        {
            Ring(
                row,
                options,
                diameter,
                showPercent: options.ShowPercent,
                innerName: nameInside ? row.Name : null
            ),
        };

        if (options.ShowNames && !nameInside)
        {
            children.Add(
                new UiTextRun
                {
                    Key = "name",
                    Text = row.Name,
                    Size = OfDiameter(diameter, 0.15),
                    Role = UiComponentTextRoles.Secondary,
                    Color = TextColor(options, UiComponentTextRoles.Secondary),
                    Weight = UiComponentTextWeights.Medium,
                    Align = UiComponentAlignments.Center,
                    MaxLines = 1,
                    Wrap = false,
                }
            );
        }

        // A placeholder keeps every ring in a grid row at the same height while a trend is withheld.
        if (options.ShowRingTrend)
        {
            children.Add(
                new UiTextRun
                {
                    Key = "trend",
                    Text = row.Stale || string.IsNullOrEmpty(row.Trend) ? "–" : row.Trend,
                    Size = OfDiameter(diameter, 0.12),
                    Role = UiComponentTextRoles.Muted,
                    Color = TextColor(options, UiComponentTextRoles.Muted),
                    Align = UiComponentAlignments.Center,
                    MaxLines = 1,
                    Wrap = false,
                }
            );
        }

        return new UiStack
        {
            Key = key,
            Direction = UiComponentDirections.Vertical,
            Justify = UiComponentJustify.Center,
            Align = UiComponentAlignments.Center,
            Gap = OfDiameter(diameter, 0.05),
            Children = children,
        };
    }

    private static UiSize OfDiameter(Func<double> diameter, double fraction) =>
        UiSize.From(() => UiLength.OfBasis(fraction * diameter()));

    // The ring fills its slot and `room` only estimates that slot, so a smaller ring caps its frame
    // and sizes its parts from the capped diameter.
    private static UiModifier Ring(
        BatteryWidgetRow row,
        BatteryWidgetOptions options,
        Func<double> room,
        bool showPercent,
        string? innerName = null
    )
    {
        var scale = options.RingScale;
        Func<double> diameter = scale < 1 ? () => room() * scale : room;
        var color = row.Color(options.LowThreshold, options.Colors);
        var charging = options.ShowCharging && row.Charging;
        var gap = charging ? ChargingGapDegrees : 0;

        var layers = new List<UiElement>();
        if (options.ShowBar)
        {
            layers.Add(
                new UiStack
                {
                    Key = "gauge-inset",
                    Padding = OfDiameter(diameter, GaugeInset),
                    Children =
                    [
                        new UiGauge
                        {
                            Key = "gauge",
                            Fill = true,
                            Level = row.Level,
                            StartAngle = gap,
                            EndAngle = 360 - gap,
                            LevelColor = color,
                            Thickness = OfDiameter(diameter, RingThickness),
                        },
                    ],
                }
            );
        }

        if (charging)
        {
            layers.Add(
                new UiStack
                {
                    Key = "bolt-lane",
                    Direction = UiComponentDirections.Vertical,
                    Justify = UiComponentJustify.Start,
                    Align = UiComponentAlignments.Center,
                    Children = [Glyph("bolt", DeviceGlyphs.Bolt, color, diameter, BoltSize)],
                }
            );
        }

        var nameInside = innerName is not null;
        var face = new List<UiElement>
        {
            Glyph(
                "glyph",
                DeviceGlyphs.For(row.Kind),
                color,
                diameter,
                (showPercent, nameInside) switch
                {
                    (true, true) => FaceGlyphNamed,
                    (false, true) => FaceGlyphNamedAlone,
                    (true, false) => FaceGlyph,
                    _ => FaceGlyphAlone,
                }
            ),
        };
        if (showPercent)
        {
            face.Add(
                new UiTextRun
                {
                    Key = "pct",
                    Text = row.PercentText(),
                    Size = OfDiameter(diameter, FacePercent),
                    Weight = UiComponentTextWeights.SemiBold,
                    Role = PercentRole(row),
                    Color = TextColor(options, PercentRole(row)),
                    Align = UiComponentAlignments.Center,
                    MaxLines = 1,
                    Wrap = false,
                }
            );
        }

        // The bottom line is where the inner circle is narrowest, so the name gets a capped width
        // and shrinks before it truncates.
        if (innerName is not null)
        {
            face.Add(
                new UiModifier
                {
                    Key = "name-slot",
                    Frame = UiValue.From(() => new UiFrame
                    {
                        MaxWidth = UiLength.OfBasis(FaceNameWidth * diameter()),
                    }),
                    Child = new UiTextRun
                    {
                        Key = "name",
                        Text = innerName,
                        Size = OfDiameter(diameter, FaceName),
                        MinSize = OfDiameter(diameter, FaceNameMin),
                        Weight = UiComponentTextWeights.Medium,
                        Role = UiComponentTextRoles.Secondary,
                        Color = TextColor(options, UiComponentTextRoles.Secondary),
                        Align = UiComponentAlignments.Center,
                        MaxLines = 1,
                        Wrap = false,
                    },
                }
            );
        }

        layers.Add(
            new UiStack
            {
                Key = "face",
                Direction = UiComponentDirections.Vertical,
                Justify = UiComponentJustify.Center,
                Align = UiComponentAlignments.Center,
                Gap = OfDiameter(diameter, 0.02),
                Children = face,
            }
        );

        return new UiModifier
        {
            Key = "ring",
            Fill = true,
            Frame =
                scale < 1
                    ? UiValue.From(() =>
                    {
                        var edge = UiLength.OfBasis(diameter());
                        return new UiFrame
                        {
                            AspectRatio = 1,
                            MaxWidth = edge,
                            MaxHeight = edge,
                        };
                    })
                    : new UiFrame { AspectRatio = 1 },
            Child = new UiLayer { Key = "ring-layers", Children = layers },
        };
    }

    private static UiModifier Glyph(
        string key,
        string path,
        string color,
        Func<double> diameter,
        double fraction
    ) =>
        new()
        {
            Key = key,
            MainSize = OfDiameter(diameter, fraction),
            Frame = UiValue.From(() =>
            {
                var edge = UiLength.OfBasis(fraction * diameter());
                return new UiFrame { Width = edge, Height = edge };
            }),
            Child = new UiShape
            {
                Key = key + "-shape",
                Shape = UiComponentShapes.Path,
                Path = path,
                Color = color,
            },
        };

    // Fractions of the view basis.
    private const double ListGlyph = 0.1;
    private const double ListBolt = 0.075;
    private const double ListGap = 0.03;
    private const double NameSize = 0.082;
    private const double CaptionSize = 0.058;
    private const double PercentSize = 0.09;

    // The reader draws the inline rows unless a name or caption would be cut off in the viewer's font. Texts
    // have no shrink priority and an unsized first-fit takes the stacked height, so the whole list switches.
    private static UiFirstFit ListBody(UiState<BatteryWidgetModel> state) =>
        new()
        {
            Key = "body",
            Fill = true,
            Children = [ListRows(state, "inline", inline: true), ListRows(state, "stacked", inline: false)],
        };

    // Hugs the text, so the bolt sits next to the number.
    private static double PercentWidth(BatteryWidgetRow row) =>
        TextWidth.Of(row.PercentText(), PercentSize) + 0.01;

    private static UiStack ListRows(
        UiState<BatteryWidgetModel> state,
        string key,
        bool inline
    ) =>
        new()
        {
            Key = key,
            Direction = UiComponentDirections.Vertical,
            Justify = state.Value.Options.ListAlign switch
            {
                BatteryListAlignment.Center => UiComponentJustify.Center,
                BatteryListAlignment.Bottom => UiComponentJustify.End,
                _ => UiComponentJustify.Start,
            },
            Gap = 0.045,
            Children =
            [
                new UiRepeat<BatteryWidgetRow>
                {
                    Key = "rows",
                    Items = UiValue.From(() => state.Value.Rows),
                    KeySelector = row => row.Id,
                    Template = (row, rowKey) =>
                        ListRow(row, rowKey, state.Value.Options, inline),
                },
            ],
        };

    private static UiStack ListRow(
        BatteryWidgetRow row,
        string key,
        BatteryWidgetOptions options,
        bool inline
    )
    {
        var color = row.Color(options.LowThreshold, options.Colors);
        var caption = Caption(row, options);

        // Shrinking would count as fitting, so the inline texts keep their size and truncate instead.
        var name = NameText(row.Name, options);
        var nameGroup = new List<UiElement> { inline ? name : name with { MinSize = 0.048 } };
        if (caption is { } captionText)
        {
            var captionRun = new UiTextRun
            {
                Key = "state",
                Text = captionText,
                Size = UiSize.FromBasis(CaptionSize, 0.34),
                Role = UiComponentTextRoles.Muted,
                Color = TextColor(options, UiComponentTextRoles.Muted),
                MaxLines = 1,
                Wrap = false,
            };
            nameGroup.Add(inline ? captionRun : captionRun with { MinSize = 0.04 });
        }

        var line = new List<UiElement>
        {
            Glyph("glyph", DeviceGlyphs.For(row.Kind), color, () => 1, ListGlyph),
            new UiStack
            {
                Key = "namegroup",
                Direction = inline
                    ? UiComponentDirections.Horizontal
                    : UiComponentDirections.Vertical,
                Align = inline ? UiComponentAlignments.Baseline : UiComponentAlignments.Start,
                Fill = true,
                Gap = inline ? 0.02 : 0.004,
                Children = nameGroup,
            },
        };
        if (options.ShowCharging && row.Charging)
        {
            line.Add(Glyph("bolt", DeviceGlyphs.Bolt, color, () => 1, ListBolt));
        }

        if (options.ShowPercent)
        {
            line.Add(
                new UiTextRun
                {
                    Key = "pct",
                    Text = row.PercentText(),
                    MainSize = PercentWidth(row),
                    Size = UiSize.FromBasis(PercentSize, 0.62),
                    MinSize = 0.055,
                    Digits = 4,
                    Weight = UiComponentTextWeights.SemiBold,
                    Role = PercentRole(row),
                    Color = TextColor(options, PercentRole(row)),
                    Align = UiComponentAlignments.End,
                }
            );
        }

        var headline = new UiStack
        {
            Key = "line",
            Direction = UiComponentDirections.Horizontal,
            Align = UiComponentAlignments.Center,
            Gap = ListGap,
            Children = line,
        };

        var children = new List<UiElement> { headline };
        if (options.ShowBar && row.Percent is not null)
        {
            children.Add(
                new UiProgressBar
                {
                    Key = "bar",
                    Fill = true,
                    MainSize = 0.03,
                    Thickness = 0.018,
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
            Gap = 0.018,
            Children = children,
        };
    }

    private static UiTextRun NameText(string name, BatteryWidgetOptions options) =>
        new()
        {
            Key = "name",
            Text = name,
            Size = UiSize.FromBasis(NameSize, 0.44),
            Weight = UiComponentTextWeights.Medium,
            Role = UiComponentTextRoles.Secondary,
            Color = TextColor(options, UiComponentTextRoles.Secondary),
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
            Background = Background(state.Value.Options),
            Children =
            [
                new UiRepeat<BatteryWidgetRow>
                {
                    Key = "tile-row",
                    Items = UiValue.From(() => FirstRow(state.Value.Rows)),
                    KeySelector = row => row.Id,
                    Template = (row, key) =>
                        new UiResponsive
                        {
                            Key = key,
                            Fill = true,
                            Default = TileStacked(row, state.Value.Options),
                            Variants =
                            [
                                new UiResponsiveVariant
                                {
                                    MinAspect = 1.6,
                                    Content = TileWide(row, state.Value.Options),
                                },
                            ],
                        },
                },
                new UiWhen
                {
                    Key = "tile-empty",
                    Condition = () => state.Value.Rows.Count == 0,
                    Content = () => EmptyText("tile-empty-text", 0.09, state.Value.Options),
                },
            ],
        };

    private static IReadOnlyList<BatteryWidgetRow> FirstRow(IReadOnlyList<BatteryWidgetRow> rows) =>
        rows.Count == 0 ? [] : [rows[0]];

    private static UiStack TileStacked(BatteryWidgetRow row, BatteryWidgetOptions options)
    {
        var caption = Caption(row, options);
        var nameInside = options.NameInRing;
        // Leaves room below the ring for the name line, and for the caption line when there is one.
        var diameter =
            1 - (2 * EdgeInset) - (nameInside ? 0 : 0.13) - (caption is null ? 0 : 0.1);

        var children = new List<UiElement>
        {
            Ring(
                row,
                options,
                () => diameter,
                options.ShowPercent,
                innerName: nameInside ? row.Name : null
            ),
        };
        if (!nameInside)
        {
            children.Add(
                new UiTextRun
                {
                    Key = "name",
                    Text = row.Name,
                    Size = UiSize.FromBasis(0.1, 0.9),
                    MinSize = 0.055,
                    Role = UiComponentTextRoles.Secondary,
                    Color = TextColor(options, UiComponentTextRoles.Secondary),
                    Weight = UiComponentTextWeights.Medium,
                    MaxLines = 1,
                    Wrap = false,
                    Align = UiComponentAlignments.Center,
                }
            );
        }

        if (caption is { } captionText)
        {
            children.Add(CaptionText(captionText, UiComponentAlignments.Center, options));
        }

        return new UiStack
        {
            Key = "stacked",
            Direction = UiComponentDirections.Vertical,
            Align = UiComponentAlignments.Center,
            Justify = UiComponentJustify.Center,
            Gap = 0.02,
            Children = children,
        };
    }

    private static UiStack TileWide(BatteryWidgetRow row, BatteryWidgetOptions options)
    {
        const double diameter = 1 - (2 * EdgeInset);
        var caption = Caption(row, options);

        var details = new List<UiElement>
        {
            new UiTextRun
            {
                Key = "name",
                Text = row.Name,
                Size = UiSize.FromBasis(0.12, 0.3),
                MinSize = 0.055,
                Role = UiComponentTextRoles.Secondary,
                Color = TextColor(options, UiComponentTextRoles.Secondary),
                Weight = UiComponentTextWeights.Medium,
                MaxLines = 1,
                Wrap = false,
            },
        };

        if (options.ShowPercent)
        {
            details.Add(
                new UiTextRun
                {
                    Key = "pct",
                    Text = row.PercentText(),
                    Size = UiSize.FromBasis(0.3, 0.6),
                    MinSize = 0.12,
                    Weight = UiComponentTextWeights.Bold,
                    Role = PercentRole(row),
                    Color = TextColor(options, PercentRole(row)),
                    MaxLines = 1,
                    Wrap = false,
                }
            );
        }

        if (caption is { } captionText)
        {
            details.Add(
                CaptionText(captionText, UiComponentAlignments.Start, options) with
                {
                    Size = UiSize.FromBasis(0.1, 0.9),
                }
            );
        }

        return new UiStack
        {
            Key = "wide",
            Direction = UiComponentDirections.Horizontal,
            Align = UiComponentAlignments.Center,
            Gap = 0.1,
            Children =
            [
                new UiStack
                {
                    Key = "ring-slot",
                    MainSize = diameter * options.RingScale,
                    Direction = UiComponentDirections.Vertical,
                    Justify = UiComponentJustify.Center,
                    Children = [Ring(row, options, () => diameter, showPercent: false)],
                },
                new UiStack
                {
                    Key = "details",
                    Direction = UiComponentDirections.Vertical,
                    Justify = UiComponentJustify.Center,
                    Fill = true,
                    Gap = 0.01,
                    Children = details,
                },
            ],
        };
    }

    private static UiTextRun CaptionText(
        LocalizedText text,
        string align,
        BatteryWidgetOptions options
    ) =>
        new()
        {
            Key = "caption",
            Text = text,
            Size = UiSize.FromBasis(0.075, 0.9),
            MinSize = 0.045,
            Role = UiComponentTextRoles.Muted,
            Color = TextColor(options, UiComponentTextRoles.Muted),
            MaxLines = 1,
            Wrap = false,
            Align = align,
        };

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

            if (options.ShowTrend && !string.IsNullOrEmpty(row.Trend))
            {
                return row.Trend!;
            }

            return options.ShowCharging ? Strings.Widgets.Caption.Charging() : null;
        }

        if (row.Status == BatteryStatus.Full)
        {
            return Strings.Widgets.Caption.Full();
        }

        // A null string converts to a non-null LocalizedText, so test the string before returning it.
        if (options.ShowTrend && !string.IsNullOrEmpty(row.Trend))
        {
            return row.Trend;
        }

        return null;
    }
}
