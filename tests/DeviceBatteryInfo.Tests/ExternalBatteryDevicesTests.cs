using System.Text.Json;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.HostBatteries;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Ui;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.Versioning;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class ExternalBatteryDevicesTests
{
    internal const string PhoneId = "com.example.phones:pixel";
    private static readonly Guid OwnEntry = Guid.Parse("5d3c8f2a-1b7e-4c90-a6d4-93e0f1b2c7a8");

    private static PluginTestHarness CreateHarness() =>
        PluginTestHarness.Create(builder =>
        {
            builder
                .UseLocalization(Strings.LocalizationCatalog)
                .RegisterIntegration<BatteryIntegration>();

            builder.Services.AddOptions<BatteryPluginOptions>();
            builder.Services.AddSingleton<BatteryRegistry>();
            builder.Services.AddSingleton<BatteryTrendTracker>();
            builder.Services.AddSingleton<ChargingInference>();
            builder.Services.AddSingleton<DeviceCatalog>();
            builder.Services.AddSingleton<HostBatteryPublisher>();
            builder.Services.AddBatterySources();
            builder.Services.AddSingleton<BatteryPollingService>();
        });

    private static BatteryDeviceSnapshot Snapshot(
        string providerId,
        string localId,
        string name,
        string kind = BatteryDeviceKinds.Phone,
        int? level = 55,
        BatteryChargeState state = BatteryChargeState.Charging,
        bool connected = true
    ) =>
        new()
        {
            Id = $"{providerId}:{localId}",
            LocalId = localId,
            ProviderId = providerId,
            ProviderName = providerId == "com.example.phones" ? "Phones" : "Device Battery Info",
            Name = name,
            Kind = kind,
            LevelPercent = level,
            ChargeState = state,
            IsConnected = connected,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

    internal static async Task<PluginTestHarness> InitializedWithCatalogAsync()
    {
        var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();
        harness
            .Services.GetRequiredService<DeviceCatalog>()
            .Set(
                [
                    new BatterySlot(
                        "mouse",
                        "Mouse",
                        BatterySourceKind.Mouse,
                        DeviceType.Catalog,
                        EntryId: OwnEntry
                    ),
                ]
            );
        harness.Context.Batteries.Set(
            [
                Snapshot("com.example.phones", "pixel", "Pixel 9"),
                Snapshot(
                    "com.pyflat.device-battery-info",
                    HostBatteryDevices.IdOf(OwnEntry),
                    "Mouse",
                    BatteryDeviceKinds.Mouse
                ),
            ]
        );
        return harness;
    }

    internal static BatteryIntegration Integration(PluginTestHarness harness) =>
        harness.Services.GetRequiredService<BatteryIntegration>();

    [Test]
    public async Task The_picker_lists_other_providers_devices_with_their_provider_but_not_our_own()
    {
        await using var harness = await InitializedWithCatalogAsync();

        var choices = Integration(harness).WidgetDevices();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(choices.Select(c => c.SourceId), Is.EqualTo(new[] { "mouse", PhoneId }));
            Assert.That(
                choices[1].Label.ToString(),
                Does.Contain("Widgets.Config.Devices.External")
            );
        }
    }

    [Test]
    public async Task A_picked_external_device_becomes_a_widget_row()
    {
        await using var harness = await InitializedWithCatalogAsync();

        var model = Integration(harness)
            .BuildModel(BatteryWidgetOptions.Default with { SourceIds = ["mouse", PhoneId] });

        var phone = model.Rows[1];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Rows, Has.Count.EqualTo(2));
            Assert.That(phone.Name, Is.EqualTo("Pixel 9"));
            Assert.That(phone.Percent, Is.EqualTo(55));
            Assert.That(phone.Charging, Is.True);
            Assert.That(phone.Stale, Is.False);
            Assert.That(phone.Kind, Is.EqualTo(BatterySourceKind.Phone));
        }
    }

    [Test]
    public async Task An_empty_selection_still_shows_only_this_plugins_devices()
    {
        await using var harness = await InitializedWithCatalogAsync();

        var model = Integration(harness).BuildModel(BatteryWidgetOptions.Default);

        Assert.That(model.Rows.Select(r => r.Name), Is.EqualTo(new[] { "Mouse" }));
    }

    [Test]
    public async Task A_withdrawn_external_device_leaves_the_widget()
    {
        await using var harness = await InitializedWithCatalogAsync();
        harness.Context.Batteries.Set([]);

        var model = Integration(harness)
            .BuildModel(BatteryWidgetOptions.Default with { SourceIds = [PhoneId] });

        Assert.That(model.Rows, Is.Empty);
    }

    [Test]
    public async Task A_widget_showing_an_external_device_opens_and_follows_the_catalog()
    {
        await using var harness = await InitializedWithCatalogAsync();
        var data = JsonSerializer.SerializeToElement(new { sourceIds = new[] { PhoneId } });
        var session = await Integration(harness)
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
        await using var _ = session!;
        Assert.DoesNotThrow(() => session!.BuildTree());

        var changed = false;
        session!.Changed += (_, _) => changed = true;
        harness.Context.Batteries.Set(
            [Snapshot("com.example.phones", "pixel", "Pixel 9", level: 20)]
        );

        Assert.That(changed, Is.True);
    }

    [Test]
    public void Row_keys_are_valid_ui_keys_and_stable()
    {
        var key = ExternalBatteryDevices.RowKey(PhoneId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(key, Does.Match("^[A-Za-z0-9][A-Za-z0-9._-]*$"));
            Assert.That(ExternalBatteryDevices.RowKey(PhoneId), Is.EqualTo(key));
            Assert.That(
                ExternalBatteryDevices.RowKey("com.example.phones:other"),
                Is.Not.EqualTo(key)
            );
        }
    }

    [TestCase(BatteryDeviceKinds.Headphones, BatterySourceKind.Headset)]
    [TestCase(BatteryDeviceKinds.Laptop, BatterySourceKind.System)]
    [TestCase(BatteryDeviceKinds.Watch, BatterySourceKind.Other)]
    [TestCase("a-kind-from-a-newer-host", BatterySourceKind.Other)]
    public void Kinds_map_back_to_our_icons(string kind, BatterySourceKind expected) =>
        Assert.That(ExternalBatteryDevices.KindOf(kind), Is.EqualTo(expected));

    [TestCase(BatteryChargeState.NotCharging, 100, BatteryStatus.Full)]
    [TestCase(BatteryChargeState.NotCharging, 80, BatteryStatus.Unknown)]
    [TestCase(BatteryChargeState.Charging, 40, BatteryStatus.Charging)]
    public void Charge_states_map_like_our_own_sources(
        BatteryChargeState state,
        int level,
        BatteryStatus expected
    ) => Assert.That(ExternalBatteryDevices.StatusOf(state, level), Is.EqualTo(expected));
}
