using System.Collections.Concurrent;

namespace DeviceBatteryInfo.Core;

// Reads never block on device I/O. The poll loop is the only writer.
public sealed class BatteryRegistry(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = time;

    // Raised on the poll loop's thread after any snapshot changes. Handlers must not block.
    public event EventHandler<BatterySnapshotChangedEventArgs>? Changed;

    public IReadOnlyList<BatterySnapshot> Snapshots =>
        _entries
            .Values.Select(e => e.Snapshot)
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool TryGet(string id, out BatterySnapshot snapshot)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            snapshot = entry.Snapshot;
            return true;
        }

        snapshot = null!;
        return false;
    }

    public void Update(IBatterySource source, BatteryReading reading)
    {
        var snapshot = new BatterySnapshot
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Kind = source.Kind,
            Reading = reading,
            IsStale = false,
            UpdatedUtc = _time.GetUtcNow(),
        };

        Publish(source.Id, _ => new Entry(snapshot, 0));
    }

    // Keeps the previous reading visible until the failure streak crosses staleAfter, rather than
    // blanking it on the first failed read.
    public void RecordFailure(IBatterySource source, int staleAfter)
    {
        Publish(
            source.Id,
            existing =>
            {
                var failures = (existing?.ConsecutiveFailures ?? 0) + 1;
                var previousReading = existing?.Snapshot.Reading ?? BatteryReading.Unavailable;
                var snapshot = new BatterySnapshot
                {
                    Id = source.Id,
                    DisplayName = source.DisplayName,
                    Kind = source.Kind,
                    Reading = previousReading,
                    IsStale = failures >= staleAfter,
                    UpdatedUtc = existing?.Snapshot.UpdatedUtc ?? _time.GetUtcNow(),
                };

                return new Entry(snapshot, failures);
            }
        );
    }

    // Drops sources that discovery no longer returns, so an unplugged device leaves the widget.
    public void Retain(IReadOnlySet<string> liveIds)
    {
        foreach (var id in _entries.Keys.Where(id => !liveIds.Contains(id)).ToArray())
        {
            if (_entries.TryRemove(id, out var removed))
            {
                Changed?.Invoke(this, new BatterySnapshotChangedEventArgs(removed.Snapshot, null));
            }
        }
    }

    private void Publish(string id, Func<Entry?, Entry> update)
    {
        Entry? previous = _entries.TryGetValue(id, out var existing) ? existing : null;
        var next = update(previous);
        _entries[id] = next;

        if (previous is null || !SnapshotEquivalent(previous.Snapshot, next.Snapshot))
        {
            Changed?.Invoke(
                this,
                new BatterySnapshotChangedEventArgs(previous?.Snapshot, next.Snapshot)
            );
        }
    }

    private static bool SnapshotEquivalent(BatterySnapshot a, BatterySnapshot b) =>
        a.Reading == b.Reading && a.IsStale == b.IsStale && a.DisplayName == b.DisplayName;

    private sealed record Entry(BatterySnapshot Snapshot, int ConsecutiveFailures);
}

// Current is null when the source was removed
public sealed class BatterySnapshotChangedEventArgs(
    BatterySnapshot? previous,
    BatterySnapshot? current
) : EventArgs
{
    public BatterySnapshot? Previous { get; } = previous;

    public BatterySnapshot? Current { get; } = current;
}
