using System.Collections.Concurrent;
using DeviceBatteryInfo.Core;
using Serilog;

namespace DeviceBatteryInfo.Sources.Hid;

/// <summary>Runs every <see cref="HidProtocol"/>: finds each configured device by USB id, works out which
/// HID interface answers and keeps identical units apart.</summary>
internal sealed class HidFamily(
    IEnumerable<HidProtocol> protocols,
    IHidTransport transport,
    ILogger logger
) : IDeviceFamily
{
    private sealed record HidModel(HidProtocol Protocol, HidDeviceInfo Device)
        : DeviceModel(Protocol.Brand, Device.Name, Device.Kind);

    private readonly ILogger _logger = logger.ForContext<HidFamily>();

    // A dongle keeps its path while plugged into the same port, so remember which interface answered.
    private readonly ConcurrentDictionary<string, string> _resolvedPaths = new(
        StringComparer.Ordinal
    );

    public IReadOnlyList<DeviceModel> Models { get; } =
        [.. protocols.SelectMany(p => p.Devices.Select(d => new HidModel(p, d)))];

    public async ValueTask<IReadOnlyList<IBatterySource>> DiscoverAsync(
        IReadOnlyList<(BatterySlot Slot, DeviceModel Model)> entries,
        CancellationToken cancellationToken
    )
    {
        var sources = new List<IBatterySource>();
        if (!OperatingSystem.IsWindows())
        {
            return sources;
        }

        foreach (var byModel in entries.GroupBy(e => e.Model.Id))
        {
            var model = (HidModel)byModel.First().Model;
            var slots = byModel.Select(e => e.Slot).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
            var candidates = transport.FindCandidates(
                model.Protocol.VendorId,
                model.Device.ProductId,
                interfaceNumber: null,
                model.Protocol.ReportLength
            );

            // One entry may use any interface. Two entries for the same model must not both claim
            // whichever unit answers first, so each gets its own.
            var units =
                slots.Length == 1
                    ? candidates.Count == 0 ? [] : new[] { candidates }
                    : candidates
                        .GroupBy(PhysicalUnitKey)
                        .OrderBy(g => g.Key, StringComparer.Ordinal)
                        .Select(g => (IReadOnlyList<HidCandidate>)[.. g])
                        .ToArray();

            if (slots.Length > 1)
            {
                _logger.Information(
                    "{Product}: {EntryCount} configured, {UnitCount} unit(s) connected.",
                    model.Name,
                    slots.Length,
                    units.Length
                );
            }

            for (var i = 0; i < slots.Length && i < units.Length; i++)
            {
                var slot = slots[i];
                var path = await ResolvePathAsync(model, slot, units[i], cancellationToken);
                var channel = new HidChannel(transport, path);
                sources.Add(
                    new DelegateBatterySource(
                        slot,
                        async ct => await model.Protocol.ReadAsync(channel, model.Device, ct)
                    )
                );
            }
        }

        return sources;
    }

    // The USB serial identifies a unit. Dongles without one share a parent-instance token in the path
    // (\\?\hid#vid_1532&pid_00b7&mi_00#8&1abcd&0&0000#{guid} -> "8&1abcd") across their interfaces.
    private static string PhysicalUnitKey(HidCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.SerialNumber))
        {
            return "s:" + candidate.SerialNumber.Trim().ToLowerInvariant();
        }

        var parts = candidate.Path.Split('#');
        if (parts.Length >= 4)
        {
            var instance = parts[2].Split('&');
            if (instance.Length >= 2)
            {
                return "i:" + string.Join('&', instance[0], instance[1]).ToLowerInvariant();
            }
        }

        return "p:" + candidate.Path.ToLowerInvariant();
    }

    private async Task<string> ResolvePathAsync(
        HidModel model,
        BatterySlot slot,
        IReadOnlyList<HidCandidate> candidates,
        CancellationToken cancellationToken
    )
    {
        if (
            _resolvedPaths.TryGetValue(slot.Id, out var cached)
            && candidates.Any(c => string.Equals(c.Path, cached, StringComparison.OrdinalIgnoreCase))
        )
        {
            return cached;
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var channel = new HidChannel(transport, candidate.Path);
                _ = await model.Protocol.ReadAsync(channel, model.Device, cancellationToken);
                _resolvedPaths[slot.Id] = candidate.Path;
                _logger.Information(
                    "HID device {DeviceId} answered on interface {Interface} ({Product}).",
                    slot.Id,
                    candidate.InterfaceNumber,
                    candidate.ProductName
                );
                return candidate.Path;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Debug(exception, "HID candidate {Path} did not answer.", candidate.Path);
            }
        }

        // Nothing answered; keep the first path so the device shows as stale instead of disappearing.
        return candidates[0].Path;
    }
}
