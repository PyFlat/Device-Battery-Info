using System.Text.Json;
using System.Text.Json.Nodes;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.HostBatteries;
using DeviceBatteryInfo.Ui;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.Versioning;
using MacroDeck.Ui.Runtime;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class WidgetIconTests
{
    private static readonly Guid IconId = Guid.Parse("0f8a3c52-6d1e-4b7a-9c20-5e4f7a1b2c3d");

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="
    );

    private static readonly UiResource Resource = new()
    {
        ResourceId = $"app.macro-deck.plugin-icon.{IconId:D}",
        ContentHash = "sha256-test",
        MediaType = "image/png",
    };

    private static BatteryWidgetOptions Parse(string json) =>
        BatteryIntegration.ParseOptions(JsonDocument.Parse(json).RootElement);

    [Test]
    public void Stored_icons_are_read_as_references_or_bare_ids_and_anything_else_is_ignored()
    {
        var options = Parse(
            $$$"""
            {"icons":{
              "mouse":{"type":"icon-pack","reference":"{{{IconId}}}"},
              "phone":"{{{IconId}}}",
              "tablet":{"type":"plugin-icon","reference":"pack/name"},
              "pen":{"type":"icon-pack","reference":"not-a-guid"},
              "watch":null
            }}
            """
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.IconFor("mouse"), Is.EqualTo(IconId));
            Assert.That(options.IconFor("phone"), Is.EqualTo(IconId));
            Assert.That(options.IconFor("tablet"), Is.Null);
            Assert.That(options.IconFor("pen"), Is.Null);
            Assert.That(options.IconFor("watch"), Is.Null);
            Assert.That(Parse("{}").IconFor("mouse"), Is.Null);
        }
    }

    [TestCase(BatteryWidgetTypes.PanelId, BatteryWidgetOptions.LayoutRings)]
    [TestCase(BatteryWidgetTypes.PanelId, BatteryWidgetOptions.LayoutList)]
    [TestCase(BatteryWidgetTypes.TileId, BatteryWidgetOptions.LayoutRings)]
    public void A_row_with_an_icon_draws_the_image_in_its_own_colours_instead_of_the_glyph(
        string widgetId,
        string layout
    )
    {
        var options = BatteryWidgetOptions.Default with { Layout = BatteryWidgetOptions.ParseLayout(layout) };
        var withIcon = Nodes(Tree(widgetId, Row() with { Icon = Resource }, options)).ToArray();
        var withoutIcon = Nodes(Tree(widgetId, Row(), options)).ToArray();

        var images = withIcon.Where(n => Type(n) == "ui.image").ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(images, Is.Not.Empty);
            Assert.That(
                images.Select(n => n["properties"]?["source"]?["resourceId"]?.GetValue<string>()),
                Is.All.EqualTo(Resource.ResourceId)
            );
            Assert.That(images.Select(n => n["properties"]?["tint"]), Is.All.Null);
            Assert.That(images.Select(n => n["properties"]?["size"]), Is.All.Not.Null);
            Assert.That(withIcon.Where(IsGlyphShape), Is.Empty);
            Assert.That(withoutIcon.Where(n => Type(n) == "ui.image"), Is.Empty);
            Assert.That(withoutIcon.Where(IsGlyphShape), Is.Not.Empty);
        }
    }

    [Test]
    public void The_accent_colour_is_the_ring_colour_and_the_old_key_is_still_read()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Parse("""{"accentColor":"#12ab34","ringColor":"#FF0000"}""").RingColor,
                Is.EqualTo("#12AB34")
            );
            Assert.That(Parse("""{"ringColor":"#ff0000"}""").RingColor, Is.EqualTo("#FF0000"));
            Assert.That(
                Parse("""{"accentColor":"red","ringColor":"#FF0000"}""").RingColor,
                Is.EqualTo("#FF0000")
            );
            Assert.That(Parse("{}").RingColor, Is.EqualTo(BatteryWidgetRow.White));
            Assert.That(
                BatteryWidgetTypes.All.Select(t => t.AppearanceProperties),
                Is.All.Contains(WidgetAppearanceProperty.AccentColor)
            );
        }
    }

    [Test]
    public void The_icon_colour_is_optional_and_must_be_a_hex()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Parse("""{"iconColor":"#00aaff"}""").IconColor, Is.EqualTo("#00AAFF"));
            Assert.That(Parse("""{"iconColor":"blue"}""").IconColor, Is.Null);
            Assert.That(Parse("{}").IconColor, Is.Null);
        }
    }

    [TestCase(BatteryWidgetTypes.PanelId, BatteryWidgetOptions.LayoutRings)]
    [TestCase(BatteryWidgetTypes.PanelId, BatteryWidgetOptions.LayoutList)]
    [TestCase(BatteryWidgetTypes.TileId, BatteryWidgetOptions.LayoutRings)]
    public void The_icon_colour_tints_picked_icons_and_colours_the_built_in_glyph(
        string widgetId,
        string layout
    )
    {
        var options = BatteryWidgetOptions.Default with
        {
            Layout = BatteryWidgetOptions.ParseLayout(layout),
            IconColor = "#00AAFF",
        };
        var withIcon = Nodes(Tree(widgetId, Row() with { Icon = Resource }, options)).ToArray();
        var glyphs = Nodes(Tree(widgetId, Row(), options)).Where(IsGlyphShape).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                withIcon.Where(n => Type(n) == "ui.image").Select(n => n["properties"]?["tint"]?.GetValue<string>()),
                Is.Not.Empty.And.All.EqualTo("#00AAFF")
            );
            Assert.That(
                glyphs.Select(n => n["properties"]?["color"]?.GetValue<string>()),
                Is.Not.Empty.And.All.EqualTo("#00AAFF")
            );
        }
    }

    [Test]
    public void The_form_stores_the_ring_colour_as_the_accent_colour_and_offers_an_icon_colour()
    {
        var ids = Nodes(ConfigTree(BatteryWidgetOptions.Default)).Select(Id).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain("accentColor"));
            Assert.That(ids, Does.Contain("iconColor"));
            Assert.That(ids, Does.Not.Contain("ringColor"));
        }
    }

    private static readonly WidgetDevice[] FormDevices =
    [
        new("mouse", "mouse", "Mouse", IsOwn: true),
        new("headset", "headset", "Headset", IsOwn: true),
        new(ExternalBatteryDevicesTests.PhoneId, "ext-0123456789abcdef", "Pixel 9 - Phones", IsOwn: false),
    ];

    [Test]
    public void The_icons_tab_offers_a_picker_for_each_shown_device_in_the_icons_object()
    {
        var options = BatteryWidgetOptions.Default with
        {
            Icons = new Dictionary<string, Guid> { ["mouse"] = IconId },
        };

        var pickers = Pickers(options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pickers.Select(Id), Is.EqualTo(new[] { "icons.mouse", "icons.headset" }));
            Assert.That(pickers[0].ToJsonString(), Does.Contain(IconId.ToString()));
            Assert.That(pickers[1].ToJsonString(), Does.Not.Contain(IconId.ToString()));
        }
    }

    [Test]
    public void The_icons_tab_follows_the_picked_devices_and_their_order()
    {
        var options = BatteryWidgetOptions.Default with
        {
            SourceIds = [ExternalBatteryDevicesTests.PhoneId, "mouse"],
        };

        Assert.That(
            Pickers(options).Select(Id),
            Is.EqualTo(new[] { "icons.ext-0123456789abcdef", "icons.mouse" })
        );
    }

    [Test]
    public async Task Picked_icons_reach_this_plugins_and_other_providers_rows()
    {
        await using var harness = await ExternalBatteryDevicesTests.InitializedWithCatalogAsync();
        var phoneKey = ExternalBatteryDevices.RowKey(ExternalBatteryDevicesTests.PhoneId);
        var integration = ExternalBatteryDevicesTests.Integration(harness);
        var options = BatteryWidgetOptions.Default with
        {
            SourceIds = ["mouse", ExternalBatteryDevicesTests.PhoneId],
            Icons = new Dictionary<string, Guid> { [phoneKey] = IconId },
        };

        var model = integration.BuildModel(
            options,
            new Dictionary<Guid, UiResource> { [IconId] = Resource }
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                integration.WidgetDevices().Select(d => d.IconKey),
                Is.EqualTo(new[] { "mouse", phoneKey })
            );
            Assert.That(model.Rows.Select(r => r.Icon), Is.EqualTo(new[] { null, Resource }));
        }
    }

    [Test]
    public async Task A_widget_session_loads_its_icons_from_the_users_icon_packs()
    {
        await using var harness = await ExternalBatteryDevicesTests.InitializedWithCatalogAsync();
        var resource = harness.Context.UiResources.AddIcon(IconId, OnePixelPng, "image/png");

        await using var session = await OpenWidgetAsync(harness, IconId);

        Assert.That(await EventuallyContainsAsync(session, resource.ResourceId), Is.True);
    }

    [Test]
    public async Task An_icon_that_is_gone_leaves_the_built_in_glyph()
    {
        await using var harness = await ExternalBatteryDevicesTests.InitializedWithCatalogAsync();

        await using var session = await OpenWidgetAsync(harness, IconId);
        await Task.Delay(100);

        var nodes = Nodes(Parse(session)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nodes.Where(n => Type(n) == "ui.image"), Is.Empty);
            Assert.That(nodes.Where(IsGlyphShape), Is.Not.Empty);
        }
    }

    [Test]
    public async Task A_rate_limited_icon_lookup_is_retried()
    {
        var calls = 0;

        var result = await HostCallRetry.RunAsync(
            () =>
                ++calls < 3
                    ? Task.FromException<int>(
                        new UiResourceException(UiResourceErrorCode.RateLimited, "slow down")
                    )
                    : Task.FromResult(calls),
            CancellationToken.None
        );

        Assert.That(result, Is.EqualTo(3));
    }

    private static async Task<IUiSession> OpenWidgetAsync(PluginTestHarness harness, Guid iconId)
    {
        var data = JsonSerializer.SerializeToElement(
            new
            {
                icons = new Dictionary<string, object>
                {
                    ["mouse"] = new { type = "icon-pack", reference = iconId.ToString() },
                },
            }
        );
        var session = await ExternalBatteryDevicesTests
            .Integration(harness)
            .CreateSessionAsync(
                new UiSessionRequest
                {
                    UiModelVersion = UiModelVersions.Current,
                    Surface = new UiSurface
                    {
                        Kind = UiSurfaceKinds.Widget,
                        SessionMode = UiSessionModes.Shared,
                        Attributes = new Dictionary<string, JsonElement>
                        {
                            [UiWidgetSurfaceAttributes.WidgetType] =
                                JsonSerializer.SerializeToElement(BatteryWidgetTypes.PanelId),
                            [UiWidgetSurfaceAttributes.Data] = data,
                        },
                    },
                },
                CancellationToken.None
            );
        Assert.That(session, Is.Not.Null);
        return session!;
    }

    private static async Task<bool> EventuallyContainsAsync(IUiSession session, string text)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (Parse(session).ToJsonString().Contains(text, StringComparison.Ordinal))
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    private static BatteryWidgetRow Row() =>
        new(
            "mouse",
            "Mouse",
            72,
            BatteryStatus.Discharging,
            false,
            Stale: false,
            TimeToFull: null,
            Kind: BatterySourceKind.Mouse
        );

    private static JsonNode Parse(IUiSession session) =>
        JsonNode.Parse(UiCanonicalJson.Serialize(session.BuildTree().Root))!;

    private static JsonNode Tree(string widgetId, BatteryWidgetRow row, BatteryWidgetOptions options)
    {
        var state = new UiState<BatteryWidgetModel>(new BatteryWidgetModel([row], options));
        var view = new UiView(
            new UiSurface
            {
                Kind = UiSurfaceKinds.Widget,
                SessionMode = UiSessionModes.Shared,
                Attributes = new Dictionary<string, JsonElement>(),
            },
            BatteryWidgetView.Build(widgetId, state, 16)
        );
        return JsonNode.Parse(UiCanonicalJson.Serialize(view.Tree.Root))!;
    }

    [Test]
    public async Task Ticking_a_device_updates_the_icons_tab_live()
    {
        var view = ConfigView(BatteryWidgetOptions.Default);
        _ = view.Tree;

        view.Dispatch(
            new UiEvent
            {
                NodeId = "sourceIds",
                Name = UiConfigEvents.Change,
                Data = JsonSerializer.SerializeToElement(new[] { "headset" }),
            }
        );
        await view.WhenIdleAsync();

        Assert.That(
            Nodes(JsonNode.Parse(UiCanonicalJson.Serialize(view.Tree.Root))!)
                .Where(n => Type(n) == "icon")
                .Select(Id),
            Is.EqualTo(new[] { "icons.headset" })
        );
    }

    [Test]
    public async Task Removing_an_icon_after_crossing_the_plugin_protocol_clears_it()
    {
        var view = ConfigView(
            BatteryWidgetOptions.Default with
            {
                Icons = new Dictionary<string, Guid> { ["mouse"] = IconId },
            }
        );
        _ = view.Tree;

        var arguments = JsonSerializer.Deserialize<UiSessionEventArguments>(
            """{"sessionId":"s","nodeId":"icons.mouse","name":"change","data":null}""",
            PluginProtocolJson.Options
        )!;
        view.Dispatch(
            new UiEvent
            {
                NodeId = arguments.NodeId,
                Name = arguments.Name,
                Data = arguments.Data,
            }
        );
        await view.WhenIdleAsync();

        Assert.That(
            Nodes(JsonNode.Parse(UiCanonicalJson.Serialize(view.Tree.Root))!)
                .Single(n => Id(n) == "icons.mouse")
                .ToJsonString(),
            Does.Not.Contain(IconId.ToString())
        );
    }

    private static JsonNode[] Pickers(BatteryWidgetOptions options) =>
        Nodes(ConfigTree(options)).Where(n => Type(n) == "icon").ToArray();

    private static JsonNode ConfigTree(BatteryWidgetOptions options) =>
        JsonNode.Parse(UiCanonicalJson.Serialize(ConfigView(options).Tree.Root))!;

    private static UiView ConfigView(BatteryWidgetOptions options) =>
        new(
            new UiSurface
            {
                Kind = UiSurfaceKinds.Config,
                SessionMode = UiSessionModes.Exclusive,
                Attributes = new Dictionary<string, JsonElement>(),
            },
            BatteryWidgetConfigView.Build(BatteryWidgetTypes.PanelId, options, FormDevices)
        );

    private static IEnumerable<JsonNode> Nodes(JsonNode node) =>
        new[] { node }.Concat((node["children"]?.AsArray() ?? []).SelectMany(child => Nodes(child!)));

    private static string Id(JsonNode node) => node["id"]!.GetValue<string>();

    private static string? Type(JsonNode node) => node["type"]?.GetValue<string>();

    private static bool IsGlyphShape(JsonNode node) =>
        Id(node).EndsWith("glyph-shape", StringComparison.Ordinal);
}
