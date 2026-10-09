using System.Text.Json;
using DeviceBatteryInfo.Actions;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class BatteryWidgetViewTests
{
    private static UiSurface WidgetSurface() =>
        new()
        {
            Kind = UiSurfaceKinds.Widget,
            SessionMode = UiSessionModes.Shared,
            Attributes = new Dictionary<string, JsonElement>(),
        };

    private static BatteryWidgetRow Row(
        int percent,
        BatteryStatus status = BatteryStatus.Discharging
    ) =>
        new(
            "mouse",
            "Mouse",
            percent,
            status,
            status == BatteryStatus.Charging,
            Stale: false,
            TimeToFull: null
        );

    [TestCase(BatteryWidgetTypes.PanelId)]
    [TestCase(BatteryWidgetTypes.TileId)]
    public void View_builds_a_tree(string widgetId)
    {
        var state = new UiState<BatteryWidgetModel>(
            new BatteryWidgetModel([Row(72)], BatteryWidgetOptions.Default)
        );

        var view = new UiView(WidgetSurface(), BatteryWidgetView.Build(widgetId, state, 16));

        Assert.That(view.Tree, Is.Not.Null);
        Assert.That(view.Tree.Root, Is.Not.Null);
    }

    [TestCase(BatteryWidgetTypes.PanelId)]
    [TestCase(BatteryWidgetTypes.TileId)]
    public void The_tree_declares_no_events_so_presses_reach_the_host(string widgetId)
    {
        var state = new UiState<BatteryWidgetModel>(
            new BatteryWidgetModel([Row(72)], BatteryWidgetOptions.Default)
        );
        var view = new UiView(WidgetSurface(), BatteryWidgetView.Build(widgetId, state, 16));

        Assert.That(JsonSerializer.Serialize(view.Tree), Does.Not.Contain("\"events\""));
    }

    [Test]
    public void Every_widget_type_refreshes_on_a_short_press_by_default()
    {
        Assert.That(
            BatteryWidgetTypes.All.Select(t => t.DefaultShortPressAction?.ActionId),
            Is.All.EqualTo(RefreshBatteryAction.ActionId)
        );
    }

    [Test]
    public void Every_widget_type_runs_the_users_flows()
    {
        Assert.That(BatteryWidgetTypes.All.Select(t => t.SupportsFlows), Is.All.True);
    }

    [TestCase(BatteryWidgetTypes.PanelId)]
    [TestCase(BatteryWidgetTypes.TileId)]
    public async Task Pushing_a_new_model_emits_patches(string widgetId)
    {
        var state = new UiState<BatteryWidgetModel>(
            new BatteryWidgetModel([Row(72)], BatteryWidgetOptions.Default)
        );
        var view = new UiView(WidgetSurface(), BatteryWidgetView.Build(widgetId, state, 16));
        _ = view.Tree;
        view.DrainPatches();

        state.Set(new BatteryWidgetModel([Row(9)], BatteryWidgetOptions.Default));
        await view.WhenIdleAsync();

        Assert.That(view.DrainPatches(), Is.Not.Empty);
    }

    [Test]
    public void Empty_model_still_builds()
    {
        var state = new UiState<BatteryWidgetModel>(
            new BatteryWidgetModel([], BatteryWidgetOptions.Default)
        );

        Assert.DoesNotThrow(
            () =>
                _ = new UiView(
                    WidgetSurface(),
                    BatteryWidgetView.Build(BatteryWidgetTypes.PanelId, state, 16)
                ).Tree
        );
    }

    [TestCase(BatteryWidgetTypes.PanelId)]
    [TestCase(BatteryWidgetTypes.TileId)]
    public void Config_view_builds(string widgetId)
    {
        var devices = new[]
        {
            new BatterySlot("system", "This PC", BatterySourceKind.System, DeviceType.System),
        };

        Assert.DoesNotThrow(
            () =>
                _ = new UiView(
                    new UiSurface
                    {
                        Kind = UiSurfaceKinds.Config,
                        SessionMode = UiSessionModes.Exclusive,
                        Attributes = new Dictionary<string, JsonElement>(),
                    },
                    BatteryWidgetConfigView.Build(widgetId, BatteryWidgetOptions.Default, devices)
                ).Tree
        );
    }

    // The host resolves a condition by the bare field id, so wrapping tabs and rows must not prefix it.
    // Only an object input opens a scope (the border's "style" is border.style).
    [TestCase(BatteryWidgetTypes.PanelId)]
    [TestCase(BatteryWidgetTypes.TileId)]
    public void Every_visibility_condition_names_a_field_the_renderer_can_find(string widgetId)
    {
        var view = new UiView(
            new UiSurface
            {
                Kind = UiSurfaceKinds.Config,
                SessionMode = UiSessionModes.Exclusive,
                Attributes = new Dictionary<string, JsonElement>(),
            },
            BatteryWidgetConfigView.Build(widgetId, BatteryWidgetOptions.Default, [])
        );
        var root = System.Text.Json.Nodes.JsonNode.Parse(
            MacroDeck.Ui.Model.Serialization.UiCanonicalJson.Serialize(view.Tree.Root)
        )!;

        var ids = new HashSet<string>();
        var conditions = new List<string>();
        void Walk(System.Text.Json.Nodes.JsonNode node)
        {
            var id = node["id"]!.GetValue<string>();
            ids.Add(id);
            if (node["properties"]?["visibleWhen"]?["parameterName"] is { } name)
            {
                var scope = id.Contains('.', StringComparison.Ordinal)
                    ? id[..(id.LastIndexOf('.') + 1)]
                    : "";
                conditions.Add(scope + name.GetValue<string>());
            }

            foreach (var child in node["children"]?.AsArray() ?? [])
            {
                Walk(child!);
            }
        }

        Walk(root);

        Assert.That(
            ids,
            Does.Contain("sourceIds").And.Contain("colors").And.Contain("flows").And.Contain("border")
        );
        Assert.That(conditions, Is.All.Matches<string>(ids.Contains));
        if (widgetId == BatteryWidgetTypes.PanelId)
        {
            Assert.That(conditions, Is.Not.Empty);
        }
    }

    [Test]
    public async Task Developer_previews_are_discovered_and_build_a_tree()
    {
        var scan = UiPreviewCatalog.Scan(typeof(BatteryWidgetPreviews).Assembly);

        Assert.That(
            scan.Diagnostics,
            Is.Empty,
            "a malformed [UiPreview] method would be reported here"
        );

        var ours = scan
            .Registrations.Where(r =>
                r.Declaration.Id.Contains(nameof(BatteryWidgetPreviews), StringComparison.Ordinal)
            )
            .ToArray();
        Assert.That(ours, Has.Length.EqualTo(22));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ours.Select(r => r.Declaration.View),
                Is.All.EqualTo(nameof(BatteryWidgetView))
            );
            Assert.That(
                ours.Select(r => r.Declaration.Profile),
                Is.All.EqualTo(UiPreviewProfiles.Widget)
            );
        }

        var surface = new UiSurface
        {
            Kind = UiSurfaceKinds.DeveloperPreview,
            SessionMode = UiSessionModes.Exclusive,
            Attributes = new Dictionary<string, JsonElement>(),
        };

        foreach (var registration in ours)
        {
            var instance = registration.Create(surface);
            Assert.That(instance.View.Tree.Root, Is.Not.Null);
            await instance.DisposeAsync();
        }
    }

    [Test]
    public void Custom_colours_reach_the_background_every_text_role_and_the_track()
    {
        var model = BatteryWidgetSamples.TileCustomColors();
        var colors = TextColors(model);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(colors["background"], Is.EqualTo("#F2F2F7"));
            Assert.That(colors["pct"], Is.EqualTo("#1C1C1E"));
            Assert.That(colors["name"], Is.EqualTo("#1C1C1ECC"));
            Assert.That(colors["caption"], Is.EqualTo("#1C1C1E99"));
            Assert.That(colors["gauge"], Is.EqualTo("#C7C7CC"));
        }

        var unknownBackdrop = TextColors(
            model with
            {
                Options = model.Options with { BackgroundColor = "transparent" },
            }
        );
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unknownBackdrop["name"], Is.EqualTo("#1C1C1ECC"));
            Assert.That(unknownBackdrop["caption"], Is.EqualTo("#1C1C1E99"));
            Assert.That(unknownBackdrop, Does.Not.ContainKey("gauge"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void The_ring_panel_stays_under_the_tree_size_limit_with_thirty_devices(bool named)
    {
        var rows = Enumerable
            .Range(0, 30)
            .Select(i => new BatteryWidgetRow(
                $"device-{i}",
                $"Wireless Device {i}",
                (i * 7) % 101,
                i % 3 == 0 ? BatteryStatus.Charging : BatteryStatus.Discharging,
                i % 3 == 0,
                Stale: false,
                TimeToFull: null,
                Trend: "-3%/1h"
            ))
            .ToArray();
        var options = BatteryWidgetOptions.Default with
        {
            Title = "Batteries",
            ShowNames = named,
            ShowRingTrend = named,
        };

        var json = MacroDeck.Ui.Model.Serialization.UiCanonicalJson.Serialize(
            new UiView(
                WidgetSurface(),
                BatteryWidgetView.Build(
                    BatteryWidgetTypes.PanelId,
                    new UiState<BatteryWidgetModel>(new BatteryWidgetModel(rows, options)),
                    16
                )
            ).Tree.Root
        );

        Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.LessThan(196608));
    }

    [Test]
    public void The_ring_panel_lets_the_reader_choose_its_columns()
    {
        var root = TreeJson(BatteryWidgetTypes.PanelId, BatteryWidgetSamples.PanelNamed());
        var grids = new List<System.Text.Json.Nodes.JsonNode>();
        void Walk(System.Text.Json.Nodes.JsonNode node)
        {
            if (node["type"]?.GetValue<string>() == "ui.grid")
            {
                grids.Add(node);
            }

            foreach (var child in node["children"]?.AsArray() ?? [])
            {
                Walk(child!);
            }
        }

        Walk(root);

        Assert.That(grids, Has.Count.EqualTo(1));
        Assert.That(grids[0]["properties"]?["minCellSize"], Is.Not.Null);
    }

    // The ring fills its slot, so only a capped frame makes it smaller; full size keeps the old tree.
    [TestCase(BatteryWidgetTypes.TileId)]
    [TestCase(BatteryWidgetTypes.PanelId)]
    public void A_smaller_ring_caps_its_frame_and_full_size_does_not(string widgetId)
    {
        var model =
            widgetId == BatteryWidgetTypes.TileId
                ? BatteryWidgetSamples.TileDischarging()
                : BatteryWidgetSamples.PanelNamed();

        System.Text.Json.Nodes.JsonNode? RingFrame(int ringSize)
        {
            var root = TreeJson(
                widgetId,
                model with
                {
                    Options = model.Options with { RingSize = ringSize },
                }
            );
            System.Text.Json.Nodes.JsonNode? Find(System.Text.Json.Nodes.JsonNode node)
            {
                if (node["id"]!.GetValue<string>().EndsWith(".ring", StringComparison.Ordinal))
                {
                    return node["properties"]?["frame"];
                }

                return (node["children"]?.AsArray() ?? [])
                    .Select(child => Find(child!))
                    .FirstOrDefault(frame => frame is not null);
            }

            return Find(root);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(RingFrame(100)?["maxWidth"], Is.Null);
            Assert.That(RingFrame(60)?["maxWidth"], Is.Not.Null);
            Assert.That(RingFrame(60)?["maxHeight"], Is.Not.Null);
        }
    }

    [TestCase(BatteryWidgetTypes.TileId)]
    [TestCase(BatteryWidgetTypes.PanelId)]
    public void The_name_moves_into_the_ring_and_leaves_no_line_below(string widgetId)
    {
        var model =
            widgetId == BatteryWidgetTypes.TileId
                ? BatteryWidgetSamples.TileDischarging()
                : BatteryWidgetSamples.PanelNamed();

        List<string> NameIds(BatteryNamePosition position)
        {
            var ids = new List<string>();
            void Walk(System.Text.Json.Nodes.JsonNode node)
            {
                var id = node["id"]!.GetValue<string>();
                // The wide tile names the device beside its ring, in either position.
                if (
                    id.EndsWith(".name", StringComparison.Ordinal)
                    && !id.Contains(".wide.", StringComparison.Ordinal)
                )
                {
                    ids.Add(id);
                }

                foreach (var child in node["children"]?.AsArray() ?? [])
                {
                    Walk(child!);
                }
            }

            Walk(
                TreeJson(
                    widgetId,
                    model with
                    {
                        Options = model.Options with { NamePosition = position },
                    }
                )
            );
            return ids;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(NameIds(BatteryNamePosition.Below), Is.Not.Empty);
            Assert.That(NameIds(BatteryNamePosition.Below), Has.None.Contains(".ring."));
            Assert.That(NameIds(BatteryNamePosition.Inside), Is.Not.Empty);
            Assert.That(NameIds(BatteryNamePosition.Inside), Is.All.Contains(".ring."));
        }
    }

    private static System.Text.Json.Nodes.JsonNode TreeJson(
        string widgetId,
        BatteryWidgetModel model
    )
    {
        var view = new UiView(
            WidgetSurface(),
            BatteryWidgetView.Build(widgetId, new UiState<BatteryWidgetModel>(model), 16)
        );
        return System.Text.Json.Nodes.JsonNode.Parse(
            MacroDeck.Ui.Model.Serialization.UiCanonicalJson.Serialize(view.Tree.Root)
        )!;
    }

    private static Dictionary<string, string?> TextColors(BatteryWidgetModel model)
    {
        var root = TreeJson(BatteryWidgetTypes.TileId, model);

        var colors = new Dictionary<string, string?>
        {
            ["background"] = root["properties"]?["background"]?.GetValue<string>(),
        };
        void Walk(System.Text.Json.Nodes.JsonNode node)
        {
            var id = node["id"]!.GetValue<string>();
            var leaf = id[(id.LastIndexOf('.') + 1)..];
            if (node["properties"]?["color"] is { } color && leaf is "pct" or "name" or "caption")
            {
                colors[leaf] = color.GetValue<string>();
            }

            if (node["properties"]?["trackColor"] is { } track && leaf is "gauge")
            {
                colors[leaf] = track.GetValue<string>();
            }

            foreach (var child in node["children"]?.AsArray() ?? [])
            {
                Walk(child!);
            }
        }

        Walk(root);
        return colors;
    }

    [Test]
    public void Every_widget_type_declares_valid_json_schema()
    {
        foreach (var descriptor in BatteryWidgetTypes.All)
        {
            Assert.That(descriptor.DataSchema, Is.Not.Null);
            Assert.DoesNotThrow(() => JsonDocument.Parse(descriptor.DataSchema!));
            Assert.DoesNotThrow(() => JsonDocument.Parse(descriptor.DefaultData!));
        }
    }

    [TestCase(BatteryWidgetTypes.PanelId, BatteryWidgetOptions.LayoutRings)]
    [TestCase(BatteryWidgetTypes.PanelId, BatteryWidgetOptions.LayoutList)]
    [TestCase(BatteryWidgetTypes.TileId, BatteryWidgetOptions.LayoutRings)]
    public void Every_layout_builds_with_charging_stale_and_unknown_rows(
        string widgetId,
        string layout
    )
    {
        var rows = new BatteryWidgetRow[]
        {
            new("a", "A", 40, BatteryStatus.Charging, true, false, "0:20", "+28%/30m", BatterySourceKind.Earbuds),
            new("b", "B", 70, BatteryStatus.Discharging, false, true, null, Kind: BatterySourceKind.Pen),
            new("c", "C", null, BatteryStatus.Unknown, false, false, null),
        };
        var options = BatteryWidgetOptions.Default with
        {
            Layout = BatteryWidgetOptions.ParseLayout(layout),
            ShowNames = true,
            ShowRingTrend = true,
        };
        var state = new UiState<BatteryWidgetModel>(new BatteryWidgetModel(rows, options));

        Assert.DoesNotThrow(
            () => _ = new UiView(WidgetSurface(), BatteryWidgetView.Build(widgetId, state, 16)).Tree
        );
    }

    [TestCase(1, 1.0, 1, 1)]
    [TestCase(2, 1.0, 2, 1)]
    [TestCase(4, 1.0, 2, 2)]
    [TestCase(4, 4.4, 4, 1)]
    [TestCase(3, 0.5, 1, 3)]
    [TestCase(6, 1.5, 3, 2)]
    public void Rings_are_arranged_to_be_as_large_as_the_box_allows(
        int count,
        double aspect,
        int columns,
        int rows
    )
    {
        var arrangement = BatteryWidgetView.Arrange(count, aspect, hasTitle: false, showNames: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(arrangement.Columns, Is.EqualTo(columns));
            Assert.That(arrangement.Rows, Is.EqualTo(rows));
            Assert.That(arrangement.Diameter, Is.GreaterThan(0).And.LessThanOrEqualTo(1));
        }
    }

    // The host takes the first variant that matches, so an overlap hides the later one.
    [TestCase(BatteryWidgetTypes.PanelId, BatteryWidgetOptions.LayoutRings)]
    [TestCase(BatteryWidgetTypes.PanelId, BatteryWidgetOptions.LayoutList)]
    [TestCase(BatteryWidgetTypes.TileId, BatteryWidgetOptions.LayoutRings)]
    public void Responsive_variants_never_overlap(string widgetId, string layout)
    {
        var model = BatteryWidgetSamples.PanelList() with
        {
            Options = BatteryWidgetSamples.PanelList().Options with
            {
                Layout = BatteryWidgetOptions.ParseLayout(layout),
            },
        };
        var tree = new UiView(
            WidgetSurface(),
            BatteryWidgetView.Build(widgetId, new UiState<BatteryWidgetModel>(model), 16)
        ).Tree;
        var json = JsonSerializer.SerializeToElement(tree.Root);

        foreach (var responsive in Descendants(json).Where(n => n.GetProperty("Type").GetString() == "ui.responsive"))
        {
            var ranges = responsive
                .GetProperty("Properties")
                .GetProperty("variants")
                .EnumerateArray()
                .Select(v =>
                    (
                        Min: v.TryGetProperty("minAspect", out var min) ? min.GetDouble() : double.NegativeInfinity,
                        Max: v.TryGetProperty("maxAspect", out var max) ? max.GetDouble() : double.PositiveInfinity
                    )
                )
                .OrderBy(r => r.Min)
                .ToArray();

            for (var i = 1; i < ranges.Length; i++)
            {
                Assert.That(ranges[i].Min, Is.GreaterThanOrEqualTo(ranges[i - 1].Max), responsive.GetProperty("Id").GetString());
            }
        }
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement node) =>
        node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array
            ? children.EnumerateArray().SelectMany(Descendants).Prepend(node)
            : [node];

    [Test]
    public void The_list_offers_inline_rows_before_stacked_ones()
    {
        var model = BatteryWidgetSamples.PanelList();
        var tree = new UiView(
            WidgetSurface(),
            BatteryWidgetView.Build(BatteryWidgetTypes.PanelId, new UiState<BatteryWidgetModel>(model), 16)
        ).Tree;
        var json = JsonSerializer.SerializeToElement(tree.Root);

        var firstFit = Descendants(json).Single(n => n.GetProperty("Type").GetString() == "ui.first-fit");
        var layouts = firstFit.GetProperty("Children").EnumerateArray().Select(c => c.GetProperty("Id").GetString()).ToArray();

        Assert.That(layouts, Has.Length.EqualTo(2));
        Assert.That(layouts[0], Does.EndWith("inline"));
        Assert.That(layouts[1], Does.EndWith("stacked"));
    }

    [Test]
    public void Labels_under_the_rings_shrink_them()
    {
        var bare = BatteryWidgetView.Arrange(3, 1.0, hasTitle: false, showNames: false);
        var named = BatteryWidgetView.Arrange(3, 1.0, hasTitle: false, showNames: true);
        var both = BatteryWidgetView.Arrange(3, 1.0, hasTitle: false, showNames: true, showTrend: true);

        Assert.That(both.Diameter, Is.LessThan(named.Diameter));
        Assert.That(named.Diameter, Is.LessThan(bare.Diameter));
    }

    [Test]
    public void Every_device_glyph_is_a_path_the_renderer_accepts()
    {
        var paths = Enum.GetValues<BatterySourceKind>()
            .Select(DeviceGlyphs.For)
            .Append(DeviceGlyphs.Bolt);

        foreach (var path in paths)
        {
            Assert.That(IsShapePathData(path), Is.True, path);
        }
    }

    // Mirrors the renderer's isShapePathData.
    private static bool IsShapePathData(string value)
    {
        var arity = new Dictionary<char, int>
        {
            ['M'] = 2,
            ['L'] = 2,
            ['H'] = 1,
            ['V'] = 1,
            ['C'] = 6,
            ['Q'] = 4,
            ['A'] = 7,
            ['Z'] = 0,
        };
        var tokens = System.Text.RegularExpressions.Regex.Matches(
            value,
            @"[A-Za-z]|[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?"
        );
        if (tokens.Count == 0 || tokens[0].Value != "M")
        {
            return false;
        }

        var index = 0;
        while (index < tokens.Count)
        {
            if (tokens[index].Value.Length != 1 || !arity.TryGetValue(tokens[index].Value[0], out var count))
            {
                return false;
            }

            var numbers = 0;
            index++;
            while (index < tokens.Count && !char.IsLetter(tokens[index].Value[0]))
            {
                numbers++;
                index++;
            }

            if (count == 0 ? numbers != 0 : numbers == 0 || numbers % count != 0)
            {
                return false;
            }
        }

        return true;
    }

    [Test]
    public void Every_scheme_shows_a_low_level_in_red_and_a_stale_one_in_grey()
    {
        foreach (var scheme in Enum.GetValues<BatteryColorScheme>())
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(R("a", 15, charging: true).Color(20, scheme), Is.EqualTo(BatteryWidgetRow.Red));
                Assert.That(R("a", 20).Color(20, scheme), Is.EqualTo(BatteryWidgetRow.Red));
                Assert.That(R("a", 60, stale: true).Color(20, scheme), Is.EqualTo(BatteryWidgetRow.Grey));
                Assert.That(R("a", null).Color(20, scheme), Is.EqualTo(BatteryWidgetRow.Grey));
                Assert.That(R("a", 80).Color(20, scheme), Does.Match("^#[0-9A-F]{6}$"));
            }
        }
    }

    [Test]
    public void The_custom_scheme_uses_its_colour_and_can_leave_a_low_level_uncoloured()
    {
        const BatteryColorScheme custom = BatteryColorScheme.Custom;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(R("a", 80).Color(20, custom, "#ffffff"), Is.EqualTo("#FFFFFF"));
            Assert.That(R("a", 80, charging: true).Color(20, custom, "#12AB34"), Is.EqualTo("#12AB34"));
            Assert.That(R("a", 80).Color(20, custom, "white"), Is.EqualTo(BatteryWidgetRow.White));
            Assert.That(R("a", 12).Color(20, custom, "#FFFFFF"), Is.EqualTo(BatteryWidgetRow.Red));
            Assert.That(R("a", 12).Color(20, custom, "#FFFFFF", lowInRed: false), Is.EqualTo("#FFFFFF"));
            Assert.That(
                R("a", 60, stale: true).Color(20, custom, "#FFFFFF", lowInRed: false),
                Is.EqualTo(BatteryWidgetRow.Grey)
            );
            // The switch belongs to the custom scheme only.
            Assert.That(R("a", 12).Color(20, BatteryColorScheme.Simple, lowInRed: false), Is.EqualTo(BatteryWidgetRow.Red));
        }
    }

    [Test]
    public void The_default_scheme_steps_with_the_level_and_is_cyan_while_charging()
    {
        Assert.That(BatteryWidgetOptions.Default.Colors, Is.EqualTo(BatteryColorScheme.LevelsCharging));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(R("a", 60, charging: true).Color(20), Is.EqualTo(BatteryWidgetRow.Cyan));
            Assert.That(R("a", 35).Color(20), Is.EqualTo(BatteryWidgetRow.Orange));
            Assert.That(R("a", 55).Color(20), Is.EqualTo(BatteryWidgetRow.Yellow));
            Assert.That(R("a", 61).Color(20), Is.EqualTo(BatteryWidgetRow.Green));
        }
    }

    [Test]
    public void List_alignment_round_trips_through_the_string_form_and_defaults_to_top()
    {
        foreach (var align in Enum.GetValues<BatteryListAlignment>())
        {
            Assert.That(
                BatteryWidgetOptions.ParseListAlign(BatteryWidgetOptions.ListAlignValue(align)),
                Is.EqualTo(align)
            );
        }

        Assert.That(BatteryWidgetOptions.ParseListAlign(null), Is.EqualTo(BatteryListAlignment.Top));
    }

    [Test]
    public void Colors_round_trip_through_the_string_form_and_default_to_levels_with_charging()
    {
        foreach (var scheme in Enum.GetValues<BatteryColorScheme>())
        {
            Assert.That(
                BatteryWidgetOptions.ParseColors(BatteryWidgetOptions.ColorsValue(scheme)),
                Is.EqualTo(scheme)
            );
        }

        Assert.That(
            BatteryWidgetOptions.ParseColors("unknown"),
            Is.EqualTo(BatteryColorScheme.LevelsCharging)
        );
    }

    [Test]
    public void Layout_round_trips_through_the_string_form_and_defaults_to_rings()
    {
        foreach (var layout in Enum.GetValues<BatteryWidgetLayout>())
        {
            Assert.That(
                BatteryWidgetOptions.ParseLayout(BatteryWidgetOptions.LayoutValue(layout)),
                Is.EqualTo(layout)
            );
        }

        Assert.That(BatteryWidgetOptions.ParseLayout("unknown"), Is.EqualTo(BatteryWidgetLayout.Rings));
    }

    private static BatteryWidgetRow R(
        string id,
        int? percent,
        bool charging = false,
        bool stale = false
    ) => new(id, id, percent, BatteryStatus.Discharging, charging, stale, TimeToFull: null);

    [Test]
    public void Manual_order_keeps_the_rows_as_given()
    {
        var rows = new[] { R("a", 30), R("b", 80), R("c", 10) };
        var ordered = BatteryWidgetOptions.Default with { Sort = BatterySortMode.Manual };

        Assert.That(ordered.Order(rows).Select(r => r.Id), Is.EqualTo(["a", "b", "c"]));
    }

    [Test]
    public void Lowest_first_sorts_by_percent_with_unknowns_last()
    {
        var rows = new[]
        {
            R("a", 30),
            R("b", 80),
            R("dead", null),
            R("c", 10),
            R("gone", 50, stale: true),
        };
        var options = BatteryWidgetOptions.Default with { Sort = BatterySortMode.LowestFirst };

        Assert.That(
            options.Order(rows).Select(r => r.Id),
            Is.EqualTo(["c", "a", "b", "dead", "gone"])
        );
    }

    [Test]
    public void Charging_first_is_stable_within_each_group()
    {
        var rows = new[]
        {
            R("a", 30),
            R("b", 80, charging: true),
            R("c", 10),
            R("d", 55, charging: true),
        };
        var options = BatteryWidgetOptions.Default with { Sort = BatterySortMode.ChargingFirst };

        Assert.That(options.Order(rows).Select(r => r.Id), Is.EqualTo(["b", "d", "a", "c"]));
    }

    [Test]
    public void Sort_round_trips_through_the_string_form()
    {
        foreach (var mode in Enum.GetValues<BatterySortMode>())
        {
            Assert.That(
                BatteryWidgetOptions.ParseSort(BatteryWidgetOptions.SortValue(mode)),
                Is.EqualTo(mode)
            );
        }
    }

    [Test]
    public void The_default_bands_keep_the_fixed_level_colours_for_every_low_threshold()
    {
        static string Before(int percent, int low, bool charging, BatteryColorScheme scheme)
        {
            if (percent <= low)
            {
                return BatteryWidgetRow.Red;
            }

            if (scheme == BatteryColorScheme.LevelsCharging && charging)
            {
                return BatteryWidgetRow.Cyan;
            }

            return percent switch
            {
                <= 40 => BatteryWidgetRow.Orange,
                <= 60 => BatteryWidgetRow.Yellow,
                _ => BatteryWidgetRow.Green,
            };
        }

        foreach (var scheme in new[] { BatteryColorScheme.Levels, BatteryColorScheme.LevelsCharging })
        {
            foreach (var charging in new[] { false, true })
            {
                for (var low = 1; low <= 99; low++)
                {
                    for (var percent = 0; percent <= 100; percent++)
                    {
                        Assert.That(
                            R("a", percent, charging).Color(low, scheme),
                            Is.EqualTo(Before(percent, low, charging, scheme)),
                            $"{scheme} low {low} at {percent}% charging {charging}"
                        );
                    }
                }
            }
        }
    }

    [Test]
    public void Edited_bands_decide_the_level_colours()
    {
        var bands = new MacroDeck.Ui.Config.UiThresholds(
            [
                new("low", "#5856d6"),
                new("yellow", BatteryWidgetRow.Yellow, 15),
                new("green", BatteryWidgetRow.Green, 30),
            ]
        );
        const BatteryColorScheme levels = BatteryColorScheme.Levels;
        const BatteryColorScheme charging = BatteryColorScheme.LevelsCharging;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(R("a", 57).Color(20, levels, thresholds: bands), Is.EqualTo(BatteryWidgetRow.Green));
            Assert.That(R("a", 20).Color(20, levels, thresholds: bands), Is.EqualTo(BatteryWidgetRow.Yellow));
            Assert.That(R("a", 14).Color(20, levels, thresholds: bands), Is.EqualTo("#5856D6"));
            Assert.That(R("a", 57, charging: true).Color(20, charging, thresholds: bands), Is.EqualTo(BatteryWidgetRow.Cyan));
            Assert.That(R("a", 14, charging: true).Color(20, charging, thresholds: bands), Is.EqualTo("#5856D6"));
            Assert.That(R("a", 57, stale: true).Color(20, levels, thresholds: bands), Is.EqualTo(BatteryWidgetRow.Grey));
            Assert.That(R("a", 20).Color(20, BatteryColorScheme.Simple, thresholds: bands), Is.EqualTo(BatteryWidgetRow.Red));
        }
    }

    [Test]
    public void Stored_thresholds_reach_the_options_and_anything_invalid_means_the_defaults()
    {
        BatteryWidgetOptions Parse(string json) =>
            BatteryIntegration.ParseOptions(JsonDocument.Parse(json).RootElement);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Parse("""{"thresholds":{"bands":[{"id":"red","color":"#ff3b30"},{"id":"green","color":"#34c759","from":50}]}}""")
                    .Thresholds?.Bands.Select(b => b.From),
                Is.EqualTo(new double?[] { null, 50 })
            );
            Assert.That(Parse("""{"thresholds":null}""").Thresholds, Is.Null);
            Assert.That(Parse("""{"thresholds":{"bands":[]}}""").Thresholds, Is.Null);
            Assert.That(Parse("""{}""").Thresholds, Is.Null);
        }
    }

    [TestCase(BatteryWidgetTypes.PanelId)]
    [TestCase(BatteryWidgetTypes.TileId)]
    public void The_threshold_bar_shows_for_the_level_schemes_and_the_low_slider_for_the_others(
        string widgetId
    )
    {
        var view = new UiView(
            new UiSurface
            {
                Kind = UiSurfaceKinds.Config,
                SessionMode = UiSessionModes.Exclusive,
                Attributes = new Dictionary<string, JsonElement>(),
            },
            BatteryWidgetConfigView.Build(widgetId, BatteryWidgetOptions.Default, [])
        );
        var root = System.Text.Json.Nodes.JsonNode.Parse(
            MacroDeck.Ui.Model.Serialization.UiCanonicalJson.Serialize(view.Tree.Root)
        )!;

        string[] ShownFor(string id)
        {
            System.Text.Json.Nodes.JsonNode? Find(System.Text.Json.Nodes.JsonNode node) =>
                node["id"]!.GetValue<string>() == id
                    ? node
                    : (node["children"]?.AsArray() ?? [])
                        .Select(child => Find(child!))
                        .FirstOrDefault(found => found is not null);

            return Find(root)!["properties"]!["visibleWhen"]!["values"]!
                .AsArray()
                .Select(v => v!.GetValue<string>())
                .ToArray();
        }

        Assert.That(
            ShownFor("thresholds"),
            Is.EquivalentTo(new[] { BatteryWidgetOptions.ColorsLevelsCharging, BatteryWidgetOptions.ColorsLevels })
        );
        Assert.That(
            ShownFor("lowThreshold").Concat(ShownFor("thresholds")),
            Is.EquivalentTo(
                Enum.GetValues<BatteryColorScheme>().Select(BatteryWidgetOptions.ColorsValue)
            )
        );
    }
}
