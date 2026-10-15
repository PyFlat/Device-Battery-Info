using DeviceBatteryInfo.HostBatteries;
using MacroDeck.Sdk.Batteries;

namespace DeviceBatteryInfo;

public sealed partial class BatteryIntegration : IBatteryProvider
{
    private IBatteryApi _batteries = UnsupportedBatteryApi.Instance;

    // Re-raised from the catalog's own event, which the host drops on every re-initialization while
    // widget sessions stay open across it.
    private event EventHandler? BatteryCatalogChanged;

    // The plugin's own name, a proper noun like the manifest's.
    public string ProviderName => "Device Battery Info";

    async Task IBatteryProvider.InitializeAsync(
        IBatteryProviderContext context,
        CancellationToken cancellationToken
    )
    {
        _polling.Polled -= OnPolled;
        _polling.Polled += OnPolled;
        await _hostBatteries.AttachAsync(context, cancellationToken);
    }

    Task IBatteryProvider.ShutdownAsync(CancellationToken cancellationToken)
    {
        _polling.Polled -= OnPolled;
        return _hostBatteries.DetachAsync(cancellationToken);
    }

    private void OnPolled(object? sender, EventArgs e) =>
        _ = _hostBatteries.SyncAsync(CancellationToken.None);

    private void AttachBatteryCatalog(IBatteryApi batteries)
    {
        _batteries.Changed -= OnBatteryCatalogChanged;
        _batteries = batteries;
        _batteries.Changed += OnBatteryCatalogChanged;
    }

    private void DetachBatteryCatalog()
    {
        _batteries.Changed -= OnBatteryCatalogChanged;
        _batteries = UnsupportedBatteryApi.Instance;
    }

    private void OnBatteryCatalogChanged(object? sender, BatteryDevicesChangedEventArgs e) =>
        BatteryCatalogChanged?.Invoke(this, EventArgs.Empty);

    private IReadOnlyList<BatteryDeviceSnapshot> ExternalDevices() =>
        ExternalBatteryDevices.List(_batteries, _catalog.Devices);
}
