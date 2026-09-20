using System.Collections.Concurrent;

namespace DeviceBatteryInfo.Core;

// History is appended from Changed on the poll thread and read concurrently by GetTrend, so each
// step replaces the segment with a new immutable instance instead of mutating one.
public sealed class BatteryTrendTracker
{
    // Bounds how far back a rate can look, so a segment cannot grow unbounded in memory
    private static readonly TimeSpan MaxHistory = TimeSpan.FromHours(3);

    // Below this the delta is dominated by poll jitter rather than the device's actual drain, so
    // GetTrend withholds a reading rather than publish a noisy one.
    public static readonly TimeSpan MinWindow = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, Segment> _segments = new(StringComparer.Ordinal);

    public BatteryTrendTracker(BatteryRegistry registry) => registry.Changed += OnSnapshotChanged;

    private void OnSnapshotChanged(object? sender, BatterySnapshotChangedEventArgs e)
    {
        if (e.Current is not { } current)
        {
            if (e.Previous is { } removed)
            {
                _segments.TryRemove(removed.Id, out _);
            }

            return;
        }

        if (current.Reading.Percent is not { } percent)
        {
            return;
        }

        _segments.AddOrUpdate(
            current.Id,
            _ => Segment.Start(current.Reading.IsCharging, current.UpdatedUtc, percent),
            (_, existing) =>
                existing.Record(current.Reading.IsCharging, current.UpdatedUtc, percent, MaxHistory)
        );
    }

    // Null until the current charging segment has enough history. A charge-state flip starts a new
    // segment, so a discharge rate and a charge rate never mix.
    public BatteryTrend? GetTrend(BatterySnapshot snapshot)
    {
        if (
            snapshot.Reading.Percent is not { } percent
            || !_segments.TryGetValue(snapshot.Id, out var segment)
            || segment.IsCharging != snapshot.Reading.IsCharging
        )
        {
            return null;
        }

        var window = snapshot.UpdatedUtc - segment.Oldest.Timestamp;
        return window < MinWindow
            ? null
            : new BatteryTrend(
                percent - segment.Oldest.Percent,
                window,
                snapshot.Reading.IsCharging
            );
    }

    private readonly record struct Sample(DateTimeOffset Timestamp, int Percent);

    private sealed record Segment(bool IsCharging, Sample[] Samples)
    {
        public Sample Oldest => Samples[0];

        public static Segment Start(bool isCharging, DateTimeOffset timestamp, int percent) =>
            new(isCharging, [new Sample(timestamp, percent)]);

        public Segment Record(
            bool isCharging,
            DateTimeOffset timestamp,
            int percent,
            TimeSpan maxHistory
        )
        {
            if (isCharging != IsCharging)
            {
                return Start(isCharging, timestamp, percent);
            }

            var samples =
                Samples[^1].Percent == percent
                    ? Samples
                    : [.. Samples, new Sample(timestamp, percent)];

            var cutoff = timestamp - maxHistory;
            var trimStart = 0;
            while (trimStart < samples.Length - 1 && samples[trimStart].Timestamp < cutoff)
            {
                trimStart++;
            }

            return new Segment(isCharging, trimStart == 0 ? samples : samples[trimStart..]);
        }
    }
}

public sealed record BatteryTrend(int DeltaPercent, TimeSpan Window, bool IsCharging);
