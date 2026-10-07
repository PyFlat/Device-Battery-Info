namespace DeviceBatteryInfo.Sources.Bluetooth;

// Several Bluetooth sources of one poll share a single fetch. A failed or cancelled fetch is never cached,
// so each waiter then fetches for itself.
internal sealed class BluetoothSnapshot(
    Func<CancellationToken, Task<IReadOnlyList<(string Name, string? RawBattery)>>> fetch,
    TimeProvider? timeProvider = null,
    StringComparer? nameComparer = null
) : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly StringComparer _names = nameComparer ?? StringComparer.Ordinal;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<(string Name, string? RawBattery)>? _devices;
    private long _timestamp;

    public async Task<string?> ReadRawAsync(string friendlyName, CancellationToken cancellationToken)
    {
        var devices = await GetAsync(cancellationToken);
        return devices.FirstOrDefault(d => _names.Equals(d.Name, friendlyName)).RawBattery;
    }

    private async Task<IReadOnlyList<(string Name, string? RawBattery)>> GetAsync(
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_devices is not null && _time.GetElapsedTime(_timestamp) < Lifetime)
            {
                return _devices;
            }

            var fresh = await fetch(cancellationToken);
            _devices = fresh;
            _timestamp = _time.GetTimestamp();
            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
