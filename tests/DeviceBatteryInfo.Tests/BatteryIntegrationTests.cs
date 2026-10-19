using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.HostBatteries;
using DeviceBatteryInfo.Sources;
using DeviceBatteryInfo.Variables;
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
            builder.Services.AddSingleton<ChargingInference>();
            builder.Services.AddSingleton<DeviceCatalog>();
            builder.Services.AddSingleton<HostBatteryPublisher>();
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
    public async Task The_configured_devices_reach_the_host_battery_catalog()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();
        var entryId = Guid.NewGuid();
        harness
            .Services.GetRequiredService<DeviceCatalog>()
            .Set(
                [
                    new BatterySlot(
                        "mouse",
                        "Mouse",
                        BatterySourceKind.Mouse,
                        DeviceType.Catalog,
                        EntryId: entryId
                    ),
                ]
            );

        await harness.Services.GetRequiredService<HostBatteryPublisher>().SyncAsync(default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(harness.Declared.Select(c => c.Kind), Does.Contain("battery-provider"));
            Assert.That(
                harness.Context.BatteryProvider.Devices.Keys,
                Is.EqualTo(new[] { HostBatteryDevices.IdOf(entryId) })
            );
        }
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

        var discover = await harness.Variables.DiscoverAsync(search: "percent", pageSize: 200);
        Assert.That(discover.Succeeded, Is.True, discover.Error?.ToString() ?? "none");
        var discoverJson = discover.Data?.ToString() ?? string.Empty;
        Assert.That(discoverJson, Does.Contain("system-percent"));
        Assert.That(discoverJson, Does.Contain("headset-percent"));

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
            new VariableCatalogQuery { ParentId = "devices/system", PageSize = 3 }
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
                ParentId = "devices/system",
                PageSize = 3,
                ContinuationToken = page.ContinuationToken,
            }
        );
        Assert.That(
            next.Items.Select(i => i.Id),
            Is.Not.EquivalentTo(page.Items.Select(i => i.Id))
        );
    }

    private static BatteryIntegration SeedPhoneAndLaptop(PluginTestHarness harness)
    {
        harness
            .Services.GetRequiredService<DeviceCatalog>()
            .Set(
                [
                    new BatterySlot(
                        "phone",
                        "Pixel 8",
                        BatterySourceKind.Phone,
                        DeviceType.AdbPhone,
                        AdbAddress: "1.2.3.4:5555"
                    ),
                    new BatterySlot(
                        "laptop",
                        "Work Laptop",
                        BatterySourceKind.System,
                        DeviceType.System
                    ),
                ]
            );

        return harness.Services.GetRequiredService<BatteryIntegration>();
    }

    [Test]
    public async Task The_top_level_is_one_folder_per_device()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();
        var integration = SeedPhoneAndLaptop(harness);

        var page = await integration.DiscoverAsync(new VariableCatalogQuery());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                page.Items.Select(i => i.Id),
                Is.EqualTo(new[] { "devices/phone", "devices/laptop" })
            );
            Assert.That(
                page.Items.Select(i => i.DisplayName.Literal),
                Is.EqualTo(new[] { "Pixel 8", "Work Laptop" })
            );
            Assert.That(page.Items.Select(i => i.IsContainer), Is.All.True);
            Assert.That(page.Items.Select(i => i.IsBindable), Is.All.False);
            Assert.That(page.ContinuationToken, Is.Null);
        }
    }

    [Test]
    public async Task A_folder_holds_exactly_its_devices_variables()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();
        var integration = SeedPhoneAndLaptop(harness);

        var page = await integration.DiscoverAsync(
            new VariableCatalogQuery { ParentId = "devices/phone" }
        );
        var unknown = await integration.DiscoverAsync(
            new VariableCatalogQuery { ParentId = "devices/tablet" }
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items, Has.Count.EqualTo(BatteryVariableCatalog.FieldsPerDevice));
            Assert.That(page.Items.Select(i => i.Id), Is.All.StartsWith("phone-"));
            Assert.That(page.Items.Select(i => i.ParentId), Is.All.EqualTo("devices/phone"));
            Assert.That(page.Items.Select(i => i.IsContainer), Is.All.False);
            Assert.That(unknown.Items, Is.Empty);
        }
    }

    [TestCase("phone percent")]
    [TestCase("PIXEL percent")]
    [TestCase("battery_phone_percent")]
    [TestCase("battery.phone.percent")]
    public async Task A_search_lists_the_matching_variables_across_devices(string search)
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();
        var integration = SeedPhoneAndLaptop(harness);

        var page = await integration.DiscoverAsync(new VariableCatalogQuery { Search = search });

        Assert.That(page.Items.Select(i => i.Id), Is.EqualTo(new[] { "phone-percent" }));
    }

    [Test]
    public async Task A_search_never_lists_folders_or_another_devices_variables()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();
        var integration = SeedPhoneAndLaptop(harness);

        var percents = await integration.DiscoverAsync(
            new VariableCatalogQuery { Search = "percent" }
        );
        var laptop = await integration.DiscoverAsync(
            new VariableCatalogQuery { Search = "work charging" }
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                percents.Items.Select(i => i.Id),
                Is.EqualTo(new[] { "phone-percent", "laptop-percent" })
            );
            Assert.That(percents.Items.Select(i => i.IsContainer), Is.All.False);
            Assert.That(laptop.Items.Select(i => i.Id), Is.EqualTo(new[] { "laptop-charging" }));
        }
    }

    [Test]
    public async Task A_folder_id_resolves_to_its_folder_and_never_to_a_variable()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();
        // A slot id ending in a field suffix, so the folder id would parse as that field without the guard.
        harness
            .Services.GetRequiredService<DeviceCatalog>()
            .Set(
                [new BatterySlot("pen-online", "Pen", BatterySourceKind.Pen, DeviceType.System)]
            );
        var integration = harness.Services.GetRequiredService<BatteryIntegration>();

        var folder = await integration.ResolveAsync("devices/pen-online");
        var variable = await integration.ResolveAsync("pen-online-online");
        var reading = await integration.ReadAsync("devices/pen-online");
        var subscribed = await integration.SubscribeAsync(["devices/pen-online"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(folder?.IsContainer, Is.True);
            Assert.That(folder?.IsBindable, Is.False);
            Assert.That(variable?.IsContainer, Is.False);
            Assert.That(
                BatteryVariableCatalog.TryResolve("devices/pen-online", out _, out _),
                Is.False
            );
            Assert.That(reading, Is.EqualTo(VariableReading.Unavailable));
            Assert.That(
                subscribed.Single(),
                Is.EqualTo(VariableValue.Unavailable("devices/pen-online"))
            );
        }
    }

    [Test]
    public void Variable_ids_and_names_of_a_slot_stay_as_released()
    {
        var slot = new BatterySlot(
            "gaming-headset",
            "Gaming Headset",
            BatterySourceKind.Headset,
            DeviceType.Bluetooth
        );

        var variables = BatteryVariableCatalog.ForDevice(slot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                variables.Select(v => v.Id),
                Is.EqualTo(
                    new[]
                    {
                        "gaming-headset-percent",
                        "gaming-headset-status",
                        "gaming-headset-charging",
                        "gaming-headset-online",
                        "gaming-headset-time-to-full",
                        "gaming-headset-trend",
                        "gaming-headset-trend-rate",
                    }
                )
            );
            Assert.That(
                variables.Select(v => v.Name),
                Is.EqualTo(
                    new[]
                    {
                        "battery_gaming_headset_percent",
                        "battery_gaming_headset_status",
                        "battery_gaming_headset_charging",
                        "battery_gaming_headset_online",
                        "battery_gaming_headset_time_to_full",
                        "battery_gaming_headset_trend",
                        "battery_gaming_headset_trend_rate",
                    }
                )
            );
        }
    }

    private sealed class SeedSource(string id, string name) : IBatterySource
    {
        public string Id => id;

        public string DisplayName => name;

        public BatterySourceKind Kind => BatterySourceKind.System;

        public ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(BatteryReading.Unavailable);
    }

    private static async Task<IReadOnlyList<MacroDeck.Sdk.Issues.IntegrationIssue>> IssuesAsync(
        PluginTestHarness harness
    ) => await harness.Services.GetRequiredService<BatteryIntegration>().GetIssuesAsync();

    [Test]
    public async Task A_blocked_device_raises_the_linux_setup_issue_only_on_linux()
    {
        await using var harness = CreateHarness();
        SeedDevices(harness);
        harness.Services.GetRequiredService<DeviceAccessProblems>().MarkBlocked("headset");

        var issues = await IssuesAsync(harness);

        if (!OperatingSystem.IsLinux())
        {
            Assert.That(issues, Is.Empty);
            return;
        }

        Assert.That(issues, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues[0].Id, Is.EqualTo(BatteryIntegration.LinuxDeviceAccessIssueId));
            Assert.That(issues[0].Severity, Is.EqualTo(MacroDeck.Sdk.Issues.IntegrationIssueSeverity.Error));
        }
    }

    [Test]
    public async Task A_removed_entry_no_longer_raises_the_issue()
    {
        await using var harness = CreateHarness();
        harness.Services.GetRequiredService<DeviceAccessProblems>().MarkBlocked("gone");
        SeedDevices(harness);

        Assert.That(await IssuesAsync(harness), Is.Empty);
    }

    [Test]
    public async Task Resolving_opens_the_setup_guide_and_an_unknown_issue_fails()
    {
        await using var harness = CreateHarness();
        var integration = harness.Services.GetRequiredService<BatteryIntegration>();
        var opened = new List<string>();
        integration.OpenInBrowser = opened.Add;

        var known = await integration.ResolveIssueAsync(BatteryIntegration.LinuxDeviceAccessIssueId);
        var unknown = await integration.ResolveIssueAsync("something-else");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(known.Success, Is.True);
            Assert.That(opened, Is.EqualTo(new[] { BatteryIntegration.LinuxSetupGuideUrl }));
            Assert.That(unknown.Success, Is.False);
        }
    }

    [Test]
    public async Task Without_a_browser_the_failure_still_carries_the_guide()
    {
        await using var harness = CreateHarness();
        var integration = harness.Services.GetRequiredService<BatteryIntegration>();
        integration.OpenInBrowser = _ => throw new System.ComponentModel.Win32Exception("no xdg-open");

        var resolution = await integration.ResolveIssueAsync(BatteryIntegration.LinuxDeviceAccessIssueId);

        Assert.That(resolution.Success, Is.False);
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
