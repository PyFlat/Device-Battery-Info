using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources;
using MacroDeck.Plugin.Testing.Fakes;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class CatalogNotificationTests
{
    [Test]
    public void The_integration_does_not_take_a_catalog_notifier()
    {
        var takesNotifier = typeof(BatteryIntegration)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType.Name.Contains("CatalogNotifier", StringComparison.Ordinal));

        Assert.That(
            takesNotifier,
            Is.False,
            "BatteryIntegration must not depend on IPluginCatalogNotifier - notifying the host drops its widgets and localization."
        );
    }

    [Test]
    public async Task Re_initializing_with_a_changing_device_set_never_throws()
    {
        var integration = Build(out var catalog, out _);

        await integration.InitializeAsync(new FakeIntegrationContext());
        catalog.Set(
            [new BatterySlot("laptop", "Laptop", BatterySourceKind.System, DeviceType.System)]
        );
        Assert.DoesNotThrowAsync(() => integration.InitializeAsync(new FakeIntegrationContext()));
    }

    [Test]
    public async Task A_runtime_source_appearing_after_init_never_throws()
    {
        var integration = Build(out _, out var registry);

        await integration.InitializeAsync(new FakeIntegrationContext());

        Assert.DoesNotThrow(
            () =>
                registry.Update(
                    new StubSource("addon", "Add-on"),
                    BatteryReading.FromPercent(50, BatteryStatus.Discharging)
                )
        );
        // let any stray fire-and-forget work settle
        await Task.Delay(200);
    }

    private static BatteryIntegration Build(out DeviceCatalog catalog, out BatteryRegistry registry)
    {
        var options = Options.Create(new BatteryPluginOptions());
        catalog = new DeviceCatalog();
        registry = new BatteryRegistry(TimeProvider.System);
        var polling = new BatteryPollingService(
            Array.Empty<IBatterySourceProvider>(),
            registry,
            new StaticOptionsMonitor<BatteryPluginOptions>(options.Value),
            Serilog.Core.Logger.None
        );

        return new BatteryIntegration(
            polling,
            registry,
            new BatteryTrendTracker(registry),
            catalog,
            TestModels.Catalog(),
            new NoDiscovery(),
            Serilog.Core.Logger.None
        );
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class NoDiscovery : IDeviceDiscovery
    {
        public Task<IReadOnlyList<BluetoothDeviceCandidate>> ListBluetoothDevicesAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<BluetoothDeviceCandidate>>([]);

        public Task<IReadOnlyList<AndroidDeviceCandidate>> ListAndroidDevicesAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<AndroidDeviceCandidate>>([]);

        public Task<IReadOnlyList<DiscoveredHidDevice>> ListHidDevicesAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<DiscoveredHidDevice>>([]);
    }

    private sealed class StubSource(string id, string name) : IBatterySource
    {
        public string Id => id;

        public string DisplayName => name;

        public BatterySourceKind Kind => BatterySourceKind.System;

        public ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(BatteryReading.Unavailable);
    }
}
