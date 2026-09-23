using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class BatteryIntegrationTests
{
    private static PluginTestHarness CreateHarness() =>
        PluginTestHarness.Create(builder =>
        {
            builder
                .UseLocalization(Strings.LocalizationCatalog)
                .RegisterIntegration<BatteryIntegration>();

            builder.Services.AddOptions<BatteryPluginOptions>();
            builder.Services.AddSingleton<BatteryRegistry>();
            builder.Services.AddSingleton<BatteryTrendTracker>();
            builder.Services.AddSingleton<DeviceCatalog>();
            builder.Services.AddBatterySources();
            builder.Services.AddSingleton<BatteryPollingService>();
        });

    private static void SeedDevices(PluginTestHarness harness) =>
        harness
            .Services.GetRequiredService<DeviceCatalog>()
            .Set(
                [
                    new BatterySlot(
                        "system",
                        "This PC",
                        BatterySourceKind.System,
                        DeviceType.System
                    ),
                    new BatterySlot(
                        "headset",
                        "Headset",
                        BatterySourceKind.Headset,
                        DeviceType.Bluetooth,
                        BluetoothFriendlyName: "CORSAIR HS80 MAX Hands-Free AG"
                    ),
                ]
            );

    [Test]
    public async Task The_plugin_builds_and_initializes()
    {
        await using var harness = CreateHarness();

        Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
    }

    [Test]
    public async Task The_refresh_action_succeeds()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();

        var outcome = await harness.Actions.ExecuteAsync(
            "refresh",
            new Dictionary<string, object?>()
        );

        Assert.That(outcome.Succeeded, Is.True);
    }

    [Test]
    public async Task The_variable_catalog_is_discoverable_but_not_eagerly_declared()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();
        SeedDevices(harness);

        // Nothing eager: describe must not list the per-device variables (the host rejects an
        // OnDemand variable that also shows up in the eager list).
        var describe = await harness.Variables.DescribeAsync();
        Assert.That(describe.Succeeded, Is.True);
        var describeJson = describe.Data?.ToString() ?? string.Empty;
        Assert.That(describeJson, Does.Not.Contain("battery_system_percent"));

        var discover = await harness.Variables.DiscoverAsync(pageSize: 200);
        Assert.That(discover.Succeeded, Is.True, discover.Error?.ToString() ?? "none");
        var discoverJson = discover.Data?.ToString() ?? string.Empty;
        Assert.That(discoverJson, Does.Contain("system-percent"));
        Assert.That(discoverJson, Does.Contain("headset-charging"));

        var resolve = await harness.Variables.ResolveAsync("system-percent");
        Assert.That(resolve.Succeeded, Is.True, resolve.Error?.ToString() ?? "none");
    }

    [Test]
    public async Task Reading_a_percent_variable_returns_the_registry_value()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();

        var registry = harness.Services.GetRequiredService<BatteryRegistry>();
        registry.Update(
            new SeedSource("system", "This PC"),
            BatteryReading.FromPercent(80, BatteryStatus.Discharging)
        );

        var outcome = await harness.Variables.GetAsync("system-percent");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                outcome.Succeeded,
                Is.True,
                $"error={outcome.Error?.ToString() ?? "none"} data={outcome.Data?.ToString() ?? "none"}"
            );
            Assert.That(outcome.Data?.ToString() ?? string.Empty, Does.Contain("80"));
        }
    }

    [Test]
    public async Task The_variable_set_is_on_demand_so_the_host_never_bulk_registers_it()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();
        SeedDevices(harness);

        var integration = harness
            .Services.GetServices<IPluginIntegration>()
            .OfType<BatteryIntegration>()
            .Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(integration.SupportsCatalog, Is.True);
            Assert.That(integration.Variables, Is.Empty, "nothing may be eagerly declared");
        }

        var page = await integration.DiscoverAsync(
            new VariableCatalogQuery { PageSize = 3 }
        );
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                page.Items.Select(v => v.Materialization),
                Is.All.EqualTo(VariableMaterialization.OnDemand)
            );
            Assert.That(page.Items, Has.Count.EqualTo(3));
            Assert.That(page.ContinuationToken, Is.Not.Null);
        }

        var next = await integration.DiscoverAsync(
            new VariableCatalogQuery
            {
                PageSize = 3,
                ContinuationToken = page.ContinuationToken,
            }
        );
        Assert.That(
            next.Items.Select(i => i.Id),
            Is.Not.EquivalentTo(page.Items.Select(i => i.Id))
        );
    }

    private sealed class SeedSource(string id, string name) : IBatterySource
    {
        public string Id => id;

        public string DisplayName => name;

        public BatterySourceKind Kind => BatterySourceKind.System;

        public ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(BatteryReading.Unavailable);
    }
}

[TestFixture]
public sealed class LocalizationTests
{
    [Test]
    public void The_catalog_is_scoped_to_the_plugin_id()
    {
        Assert.That(
            Strings.LocalizationCatalog.Scope,
            Is.EqualTo("plugin:com.pyflat.device-battery-info")
        );
    }

    [Test]
    public void The_catalog_is_actually_populated()
    {
        using (Assert.EnterMultipleScope())
        {
            // The generated catalog can compile with the members present but no data embedded, which the
            // vacuous loop below would not catch; assert real content.
            Assert.That(
                Strings.LocalizationCatalog.KeysOf("en"),
                Has.Count.GreaterThanOrEqualTo(50)
            );
            Assert.That(
                Strings.LocalizationCatalog.TryGetTemplate(
                    "en",
                    "Widgets.Panel.Name",
                    out var name
                ),
                Is.True
            );
            Assert.That(name, Is.EqualTo("Battery panel"));
            Assert.That(
                Strings.LocalizationCatalog.TryGetTemplate(
                    "en",
                    "ConfigFlow.Device.AdbAddress.Label",
                    out var label
                ),
                Is.True
            );
            Assert.That(label, Is.EqualTo("adb serial or address (host:port)"));
        }
    }

    [Test]
    public void Every_default_culture_key_resolves_to_text()
    {
        foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text),
                    Is.True
                );
                Assert.That(text, Is.Not.Empty);
            }
        }
    }
}
