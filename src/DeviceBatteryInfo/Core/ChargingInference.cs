using System.Collections.Concurrent;

namespace DeviceBatteryInfo.Core;

// Some sources cannot tell charging from discharging (Bluetooth only reports a level), so a level that
// keeps rising is read as charging. A reading whose source did report a state is passed through.
// Each device is polled by one task at a time, so its state has a single writer.
public sealed class ChargingInference
{
    // Two points filter 1% jitter; a device that reports in 10% steps passes on one step.
    public const int MinRise = 2;

    // How far back the lowest level is looked for when deciding a rise started.
    public static readonly TimeSpan RiseWindow = TimeSpan.FromMinutes(15);

    // A charged device stops rising, and one taken off the charger may not drop a step for a while.
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public ChargingInference(BatteryRegistry registry, TimeProvider time)
    {
        _time = time;
        registry.Changed += OnSnapshotChanged;
    }

    public BatteryReading Apply(string id, BatteryReading reading)
    {
        if (
            reading.Percent is not { } percent
            || !(reading.StatusIsAssumed || reading.Status == BatteryStatus.Unknown)
        )
        {
            _states.TryRemove(id, out _);
            return reading;
        }

        var now = _time.GetUtcNow();
        var state = _states.TryGetValue(id, out var existing)
            ? existing.Next(now, percent)
            : State.Start(now, percent);
        _states[id] = state;

        return state.Charging ? reading with { Status = BatteryStatus.Charging } : reading;
    }

    private void OnSnapshotChanged(object? sender, BatterySnapshotChangedEventArgs e)
    {
        if (e.Current is null && e.Previous is { } removed)
        {
            _states.TryRemove(removed.Id, out _);
        }
    }

    private readonly record struct Sample(DateTimeOffset Timestamp, int Percent);

    private sealed record State(Sample[] Samples, bool Charging, int Peak, DateTimeOffset LastRise)
    {
        public static State Start(DateTimeOffset now, int percent) =>
            new([new Sample(now, percent)], false, percent, now);

        public State Next(DateTimeOffset now, int percent)
        {
            if (Charging)
            {
                if (percent > Peak)
                {
                    return this with { Peak = percent, LastRise = now };
                }

                // A drop or a long stall ends it; the history restarts so the old low cannot
                // re-trigger a rise straight away.
                return percent < Peak || now - LastRise >= IdleTimeout
                    ? Start(now, percent)
                    : this;
            }

            var samples = Samples[^1].Percent == percent ? Samples : [.. Samples, new Sample(now, percent)];
            var cutoff = now - RiseWindow;
            var trimStart = 0;
            while (trimStart < samples.Length - 1 && samples[trimStart].Timestamp < cutoff)
            {
                trimStart++;
            }

            samples = trimStart == 0 ? samples : samples[trimStart..];
            var lowest = samples.Min(s => s.Percent);
            return percent - lowest >= MinRise
                ? new State([new Sample(now, percent)], true, percent, now)
                : this with { Samples = samples };
        }
    }
}
