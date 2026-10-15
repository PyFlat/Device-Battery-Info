using MacroDeck.Sdk.Batteries;

namespace DeviceBatteryInfo;

public sealed partial class BatteryIntegration : IBatteryProvider
{
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
}
