using System.Text.Json;
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

    [TestCase(BatteryWidgetTypes.PanelId, "battery-panel")]
    [TestCase(BatteryWidgetTypes.TileId, "battery-tile")]
    public void Pressing_the_widget_runs_the_press_callback(string widgetId, string rootId)
    {
        var presses = 0;
        var state = new UiState<BatteryWidgetModel>(
            new BatteryWidgetModel([Row(72)], BatteryWidgetOptions.Default)
        );
        var view = new UiView(
            WidgetSurface(),
            BatteryWidgetView.Build(widgetId, state, 16, () => presses++)
        );

        var result = view.Dispatch(new UiEvent { NodeId = rootId, Name = "press" });

        Assert.That(result.IsAccepted, Is.True);
        Assert.That(presses, Is.EqualTo(1));
    }

    [TestCase(BatteryWidgetTypes.PanelId)]
    [TestCase(BatteryWidgetTypes.TileId)]
    public void Without_a_press_callback_the_tree_declares_no_events(string widgetId)
    {
        var state = new UiState<BatteryWidgetModel>(
            new BatteryWidgetModel([Row(72)], BatteryWidgetOptions.Default)
        );
        var view = new UiView(WidgetSurface(), BatteryWidgetView.Build(widgetId, state, 16));

        Assert.That(JsonSerializer.Serialize(view.Tree), Does.Not.Contain("\"events\""));
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

    [Test]
    public void Config_view_builds()
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
                    BatteryWidgetConfigView.Build(BatteryWidgetOptions.Default, devices)
                ).Tree
        );
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
        Assert.That(ours, Has.Length.EqualTo(7));
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
    public void Every_widget_type_declares_valid_json_schema()
    {
        foreach (var descriptor in BatteryWidgetTypes.All)
        {
            Assert.That(descriptor.DataSchema, Is.Not.Null);
            Assert.DoesNotThrow(() => JsonDocument.Parse(descriptor.DataSchema!));
            Assert.DoesNotThrow(() => JsonDocument.Parse(descriptor.DefaultData!));
        }
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
}
