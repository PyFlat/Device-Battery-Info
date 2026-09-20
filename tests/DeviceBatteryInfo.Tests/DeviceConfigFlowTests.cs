using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.ConfigFlow;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class DeviceConfigFlowTests
{
    private sealed class FakeContext(string? entryTitle = null) : IConfigFlowEntryContext
    {
        public IOAuthSession OAuth => throw new NotSupportedException();

        public string? EntryTitle => entryTitle;
    }

    private sealed class FakeDiscovery(
        IReadOnlyList<BluetoothDeviceCandidate>? bluetooth = null,
        IReadOnlyList<DiscoveredHidDevice>? hid = null
    ) : IDeviceDiscovery
    {
        public Task<IReadOnlyList<BluetoothDeviceCandidate>> ListBluetoothDevicesAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult(bluetooth ?? []);

        public Task<IReadOnlyList<DiscoveredHidDevice>> ListHidDevicesAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult(hid ?? []);
    }

    private static DeviceConfigFlow Flow(IReadOnlyList<BatterySlot>? current = null) =>
        new(new FakeDiscovery(), TestModels.Catalog(), current ?? [], Serilog.Core.Logger.None);

    // Drives start, basics, other and details on one flow instance, the way the host does.
    private static async Task<ConfigFlowResult> RunAsync(
        DeviceConfigFlow flow,
        Dictionary<string, object?> basics,
        Dictionary<string, object?>? other = null,
        Dictionary<string, object?>? details = null,
        FakeContext? context = null
    )
    {
        context ??= new FakeContext();
        await flow.StartAsync(context, CancellationToken.None);

        var result = await flow.SubmitAsync("basics", basics, context, CancellationToken.None);
        if (result.Kind == ConfigFlowResultKind.Step && result.NextStep!.StepId == "other")
        {
            result = await flow.SubmitAsync("other", other ?? [], context, CancellationToken.None);
        }

        return result.Kind == ConfigFlowResultKind.Step && result.NextStep!.StepId == "details"
            ? await flow.SubmitAsync("details", details ?? [], context, CancellationToken.None)
            : result;
    }

    [Test]
    public async Task Start_returns_the_basics_step()
    {
        var result = await Flow().StartAsync(new FakeContext(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
            Assert.That(result.NextStep!.StepId, Is.EqualTo("basics"));
            Assert.That(
                result.NextStep!.Fields.Select(f => f.Name),
                Is.EquivalentTo(new[] { "name", "category" })
            );
        }
    }

    [Test]
    public async Task Basics_advances_to_a_details_step_scoped_to_the_type()
    {
        var flow = Flow();
        await flow.StartAsync(new FakeContext(), CancellationToken.None);

        var result = await flow.SubmitAsync(
            "basics",
            new Dictionary<string, object?> { ["name"] = "My Phone", ["category"] = "adb-phone" },
            new FakeContext(),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
            Assert.That(result.NextStep!.StepId, Is.EqualTo("details"));
            Assert.That(result.NextStep!.Fields.Select(f => f.Name), Does.Contain("adbAddress"));
        }
        Assert.That(result.NextStep!.Fields.Select(f => f.Name), Does.Not.Contain("bluetoothName"));
    }

    [Test]
    public async Task A_system_battery_completes_straight_from_basics()
    {
        var flow = Flow();
        await flow.StartAsync(new FakeContext(), CancellationToken.None);

        var result = await flow.SubmitAsync(
            "basics",
            new Dictionary<string, object?> { ["name"] = "This PC", ["category"] = "system" },
            new FakeContext(),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
            Assert.That(result.Values!.Keys, Is.EquivalentTo(new[] { "name", "type" }));
        }
    }

    [Test]
    public async Task Basics_other_advances_to_the_brand_and_model_step()
    {
        var flow = Flow();
        await flow.StartAsync(new FakeContext(), CancellationToken.None);

        var result = await flow.SubmitAsync(
            "basics",
            new Dictionary<string, object?> { ["name"] = "Mouse", ["category"] = "other" },
            new FakeContext(),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
            Assert.That(result.NextStep!.StepId, Is.EqualTo("other"));
        }

        var brand = result.NextStep!.Fields.First(f => f.Name == "catalogBrand");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(brand.Options!.Select(o => o.Value), Does.Contain("razer"));
            Assert.That(
                result.NextStep!.Fields.Select(f => f.Name),
                Does.Contain("catalogDevice:razer")
            );
            Assert.That(
                result.NextStep!.Links.Select(l => l.Url),
                Has.Some.Contains("docs/adding-a-device.md")
            );
        }
    }

    [Test]
    public async Task The_other_step_completes_for_a_catalog_model_that_needs_no_details()
    {
        var flow = Flow();
        var context = new FakeContext();
        await flow.StartAsync(context, CancellationToken.None);
        await flow.SubmitAsync(
            "basics",
            new Dictionary<string, object?> { ["name"] = "Mouse", ["category"] = "other" },
            context,
            CancellationToken.None
        );

        var result = await flow.SubmitAsync(
            "other",
            new Dictionary<string, object?>
            {
                ["catalogBrand"] = "razer",
                ["catalogDevice:razer"] = "razer-deathadder-v3-pro",
            },
            context,
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
            Assert.That(result.Values!["type"].Value, Is.EqualTo("catalog"));
            Assert.That(
                result.Values!["catalogDevice"].Value,
                Is.EqualTo("razer-deathadder-v3-pro")
            );
        }
    }

    [Test]
    public async Task The_other_step_rejects_a_model_that_is_not_the_selected_brand()
    {
        var flow = Flow();
        var context = new FakeContext();
        await flow.StartAsync(context, CancellationToken.None);
        await flow.SubmitAsync(
            "basics",
            new Dictionary<string, object?> { ["name"] = "Mouse", ["category"] = "other" },
            context,
            CancellationToken.None
        );

        var result = await flow.SubmitAsync(
            "other",
            new Dictionary<string, object?>
            {
                ["catalogBrand"] = "logitech",
                ["catalogDevice:logitech"] = "razer-deathadder-v3-pro",
            },
            context,
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
            Assert.That(result.NextStep!.StepId, Is.EqualTo("other"));
            Assert.That(result.FieldErrors, Does.ContainKey("catalogDevice"));
        }
    }

    [Test]
    public async Task Editing_pre_fills_both_steps_from_the_named_device()
    {
        var devices = new[]
        {
            new BatterySlot(
                "my-phone",
                "My Phone",
                BatterySourceKind.Phone,
                DeviceType.AdbPhone,
                AdbAddress: "10.0.0.5:5555"
            ),
        };
        var flow = Flow(devices);

        var basics = await flow.StartAsync(new FakeContext("My Phone"), CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                basics.NextStep!.Fields.First(f => f.Name == "name").DefaultValue,
                Is.EqualTo("My Phone")
            );
            Assert.That(
                basics.NextStep!.Fields.First(f => f.Name == "category").DefaultValue,
                Is.EqualTo("adb-phone")
            );
        }

        var details = await flow.SubmitAsync(
            "basics",
            new Dictionary<string, object?>(),
            new FakeContext("My Phone"),
            CancellationToken.None
        );
        Assert.That(
            details.NextStep!.Fields.First(f => f.Name == "adbAddress").DefaultValue,
            Is.EqualTo("10.0.0.5:5555")
        );
    }

    [Test]
    public async Task Editing_a_catalog_device_pre_selects_its_brand_and_model()
    {
        var devices = new[]
        {
            new BatterySlot(
                "mouse",
                "Mouse",
                BatterySourceKind.Mouse,
                DeviceType.Catalog,
                CatalogDeviceId: "razer-deathadder-v3-pro"
            ),
        };
        var flow = Flow(devices);
        var context = new FakeContext("Mouse");

        var basics = await flow.StartAsync(context, CancellationToken.None);
        Assert.That(
            basics.NextStep!.Fields.First(f => f.Name == "category").DefaultValue,
            Is.EqualTo("other")
        );

        var other = await flow.SubmitAsync(
            "basics",
            new Dictionary<string, object?>(),
            context,
            CancellationToken.None
        );
        using (Assert.EnterMultipleScope())
        {
            Assert.That(other.NextStep!.StepId, Is.EqualTo("other"));
            Assert.That(
                other.NextStep!.Fields.First(f => f.Name == "catalogBrand").DefaultValue,
                Is.EqualTo("razer")
            );
            Assert.That(
                other.NextStep!.Fields.First(f => f.Name == "catalogDevice:razer").DefaultValue,
                Is.EqualTo("razer-deathadder-v3-pro")
            );
        }
    }

    [Test]
    public async Task A_valid_phone_completes_with_only_its_own_values()
    {
        var result = await RunAsync(
            Flow(),
            basics: new() { ["name"] = "My Phone", ["category"] = "adb-phone" },
            details: new() { ["adbAddress"] = "1.2.3.4:5555" }
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
            Assert.That(result.EntryTitle, Is.EqualTo("My Phone"));
            Assert.That(result.Values!["type"].Value, Is.EqualTo("adb-phone"));
            Assert.That(result.Values!["adbAddress"].Value, Is.EqualTo("1.2.3.4:5555"));
            Assert.That(result.Values!["adbExecutable"].Value, Is.EqualTo("adb"));
            Assert.That(result.Values!.Keys, Does.Not.Contain("bluetoothName"));
        }
        Assert.That(result.Values!.Keys, Does.Not.Contain("razerDevice"));
        Assert.That(result.Values!.Keys, Does.Not.Contain("catalogDevice"));
    }

    [Test]
    public async Task A_catalog_device_persists_only_its_backend_type_and_catalog_id()
    {
        var result = await RunAsync(
            Flow(),
            basics: new() { ["name"] = "Mouse", ["category"] = "other" },
            other: new()
            {
                ["catalogBrand"] = "razer",
                ["catalogDevice:razer"] = "razer-deathadder-v3-pro",
            }
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
            Assert.That(
                result.Values!.Keys,
                Is.EquivalentTo(new[] { "name", "type", "catalogDevice" })
            );
            Assert.That(result.Values!["type"].Value, Is.EqualTo("catalog"));
            Assert.That(
                result.Values!["catalogDevice"].Value,
                Is.EqualTo("razer-deathadder-v3-pro")
            );
        }
        // The catalog carries the USB id now; the flow never asks for or stores it.
        Assert.That(result.Values!.Keys, Does.Not.Contain("vendorId"));
        Assert.That(result.Values!.Keys, Does.Not.Contain("razerDevice"));
    }

    [Test]
    public async Task Editing_uses_the_entry_title_when_the_name_field_is_omitted()
    {
        var flow = Flow();
        var context = new FakeContext("This PC");
        await flow.StartAsync(context, CancellationToken.None);

        var result = await flow.SubmitAsync(
            "basics",
            new Dictionary<string, object?> { ["category"] = "system" },
            context,
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
            Assert.That(result.EntryTitle, Is.EqualTo("This PC"));
        }
    }

    [Test]
    public async Task A_missing_name_is_a_field_error_on_the_basics_step()
    {
        var flow = Flow();
        await flow.StartAsync(new FakeContext(), CancellationToken.None);

        var result = await flow.SubmitAsync(
            "basics",
            new Dictionary<string, object?> { ["category"] = "adb-phone" },
            new FakeContext(),
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
            Assert.That(result.NextStep!.StepId, Is.EqualTo("basics"));
            Assert.That(result.FieldErrors, Does.ContainKey("name"));
        }
    }

    [Test]
    public async Task A_phone_without_an_address_is_a_field_error_on_the_details_step()
    {
        var result = await RunAsync(
            Flow(),
            basics: new() { ["name"] = "P", ["category"] = "adb-phone" }
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
            Assert.That(result.NextStep!.StepId, Is.EqualTo("details"));
            Assert.That(result.FieldErrors, Does.ContainKey("adbAddress"));
        }
    }

    [Test]
    public async Task A_bluetooth_device_without_a_name_is_a_field_error()
    {
        var result = await RunAsync(
            Flow(),
            basics: new() { ["name"] = "H", ["category"] = "bluetooth" }
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
            Assert.That(result.FieldErrors, Does.ContainKey("bluetoothName"));
        }
    }
}
