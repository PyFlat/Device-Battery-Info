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

    // Only switches the reader to choosing the columns: the panel's grid always has a definite height.
    private const double MinCell = 0.25;

    // How much of the text colour the ring's track takes over the background.
    private const double TrackStrength = 0.2;

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

    // One stored text colour. Secondary and muted text take it at 80 % and 60 % alpha, so they stay fainter
    // over any backdrop; a reader older than beta.16 draws their role colour instead.
    private static UiValue<string> TextColor(BatteryWidgetOptions options, string role)
    {
        if (options.TextColor is not { } color)
        {
            return UiValue.None<string>();
        }

        if (role == UiComponentTextRoles.Primary || !BatteryWidgetRow.IsHexColor(color))
        {
            return UiValue.Of(color);
        }

        return UiValue.Of(color + (role == UiComponentTextRoles.Secondary ? "CC" : "99"));
    }

    // A gauge track takes #rrggbb only, so it is the text colour mixed toward a known opaque background;
    // over the theme's face or a transparent one the theme's track stays.
    private static UiValue<string> TrackColor(BatteryWidgetOptions options) =>
        options.TextColor is { } color
        && BatteryWidgetRow.IsHexColor(color)
        && options.BackgroundColor is { } background
        && BatteryWidgetRow.IsHexColor(background)
            ? UiValue.Of(Mix(color, background, TrackStrength))
            : UiValue.None<string>();

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

    // One grid for every widget shape: the reader picks the columns (MinCellSize) and each ring sizes its
    // parts from its own cell. Columns, Rows and the fallback lengths are the square arrangement, which is
    // what a reader older than beta.16 draws at every shape.
    private static UiGrid RingBody(UiState<BatteryWidgetModel> state, bool hasTitle)
    {
        RingArrangement Square() =>
            Arrange(
                state.Value.Rows.Count,
                1,
                hasTitle,
                state.Value.Options.ShowNames && !state.Value.Options.NameInRing,
                state.Value.Options.ShowRingTrend
            );

        return new UiGrid
        {
            Key = "rings",
            Fill = true,
            Columns = UiValue.From(() => Square().Columns),
            Rows = UiValue.From(() => Square().Rows),
            MinCellSize = UiSize.FromBasis(MinCell),
            Gap = UiSize.FromBasis(GridGap),
            Children =
            [
                new UiRepeat<BatteryWidgetRow>
                {
                    Key = "cells",
                    Items = UiValue.From(() => state.Value.Rows),
                    KeySelector = row => row.Id,
                    Template = (row, rowKey) =>
                        RingCell(row, rowKey, state.Value.Options, () => Square().Diameter),
                },
            ],
        };
    }

    internal readonly record struct RingArrangement(int Columns, int Rows, double Diameter);

    // The fallback arrangement for readers that cannot choose the columns. A tie goes to more columns, so
    // two rings sit side by side.
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

    // The labels below take part of the cell, so the ring is assumed to be that much smaller than the
    // cell's shorter side.
    private static UiStack RingCell(
        BatteryWidgetRow row,
        string key,
        BatteryWidgetOptions options,
        Func<double> diameter
    )
    {
        var nameInside = options.ShowNames && options.NameInRing;
        var share =
            1
            / (
                1
                + (options.ShowNames && !nameInside ? NameShare : 0)
                + (options.ShowRingTrend ? TrendShare : 0)
            );
        var children = new List<UiElement>
        {
            Ring(
                row,
                options,
                diameter,
                share,
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
                    Size = OfRing(diameter, 0.15, share),
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
                    Size = OfRing(diameter, 0.12, share),
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
            Gap = OfRing(diameter, 0.05, share),
            Children = children,
        };
    }

    // `fraction` of the ring's diameter, where the ring is `share` of the containing box's shorter side.
    // A reader that cannot resolve the box (older than beta.16, or an open extent) takes `fraction` of the
    // estimated diameter, a fraction of the widget basis.
    private static Func<UiLength> RingPart(Func<double> diameter, double fraction, double share = 1) =>
        () => UiLength.OfParent(fraction * share, fraction * diameter());

    private static UiSize OfRing(Func<double> diameter, double fraction, double share = 1) =>
        UiSize.From(RingPart(diameter, fraction, share));

    // The ring fills its slot, so a smaller ring caps its frame at `ringSize` of the full ring, the slot's
    // `share`. Its parts size from the ring's own box; `room` only estimates the full diameter for readers
    // that cannot.
    private static UiModifier Ring(
        BatteryWidgetRow row,
        BatteryWidgetOptions options,
        Func<double> room,
        double share,
        bool showPercent,
        string? innerName = null
    )
    {
        var scale = options.RingScale;
        Func<double> diameter = scale < 1 ? () => room() * scale : room;
        var color = row.Color(options);
        var charging = options.ShowCharging && row.Charging;
        var gap = charging ? ChargingGapDegrees : 0;

        var layers = new List<UiElement>();
        if (options.ShowBar)
        {
            // The gauge's box is the ring minus the inset on both sides.
            layers.Add(
                new UiStack
                {
                    Key = "gauge-inset",
                    Padding = OfRing(diameter, GaugeInset),
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
                            TrackColor = TrackColor(options),
                            Thickness = OfRing(diameter, RingThickness, 1 / (1 - (2 * GaugeInset))),
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
                    Children = [Glyph("bolt", DeviceGlyphs.Bolt, color, RingPart(diameter, BoltSize))],
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
                RingPart(
                    diameter,
                    (showPercent, nameInside) switch
                    {
                        (true, true) => FaceGlyphNamed,
                        (false, true) => FaceGlyphNamedAlone,
                        (true, false) => FaceGlyph,
                        _ => FaceGlyphAlone,
                    }
                )
            ),
        };
        if (showPercent)
        {
            face.Add(
                new UiTextRun
                {
                    Key = "pct",
                    Text = row.PercentText(),
                    Size = OfRing(diameter, FacePercent),
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
        // and shrinks before it truncates. The slot is one name line tall, a definite box the name
        // sizes from: its shorter side is FaceName of the ring.
        if (innerName is not null)
        {
            face.Add(
                new UiModifier
                {
                    Key = "name-slot",
                    MainSize = OfRing(diameter, FaceName),
                    Frame = UiValue.From(() => new UiFrame
                    {
                        Width = RingPart(diameter, FaceNameWidth)(),
                    }),
                    Child = new UiTextRun
                    {
                        Key = "name",
                        Text = innerName,
                        Size = OfRing(diameter, FaceName, 1 / FaceName),
                        MinSize = OfRing(diameter, FaceNameMin, 1 / FaceName),
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
                Gap = OfRing(diameter, 0.02),
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
                        var edge = RingPart(room, scale, share)();
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

    // A square glyph `edge` long.
    private static UiModifier Glyph(string key, string path, string color, Func<UiLength> edge) =>
        new()
        {
            Key = key,
            MainSize = UiSize.From(edge),
            Frame = UiValue.From(() =>
            {
                var length = edge();
                return new UiFrame { Width = length, Height = length };
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
        var color = row.Color(options);
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
            Glyph("glyph", DeviceGlyphs.For(row.Kind), color, () => UiLength.OfBasis(ListGlyph)),
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
            line.Add(Glyph("bolt", DeviceGlyphs.Bolt, color, () => UiLength.OfBasis(ListBolt)));
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
                diameter / (1 - (2 * EdgeInset)),
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
                    // The slot is already the scaled ring wide, so the full ring is 1 / scale of it.
                    Children =
                    [
                        Ring(row, options, () => diameter, 1 / options.RingScale, showPercent: false),
                    ],
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
