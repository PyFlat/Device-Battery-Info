using System.Collections.Concurrent;
using DeviceBatteryInfo.Core;
using Serilog;

namespace DeviceBatteryInfo.Sources.Hid;

internal sealed class HidFamily(
    IEnumerable<HidProtocol> protocols,
    IHidTransport transport,
    ILogger logger,
    DeviceAccessProblems? accessProblems = null
) : IDeviceFamily
{
    private sealed record HidModel(HidProtocol Protocol, HidDeviceInfo Device)
        : DeviceModel(Protocol.Brand, Device.Name, Device.Kind);

    private readonly ILogger _logger = logger.ForContext<HidFamily>();
    private readonly DeviceAccessProblems _accessProblems = accessProblems ?? new();

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
        foreach (var byModel in entries.GroupBy(e => e.Model.Id))
        {
            var model = (HidModel)byModel.First().Model;
            var slots = byModel.Select(e => e.Slot).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
            IReadOnlyList<HidCandidate> found =
            [
                .. model.Device.ProductIds.SelectMany(productId =>
                    transport.FindCandidates(
                        model.Protocol.VendorId,
                        productId,
                        interfaceNumber: null,
                        model.Protocol.ReportKind == HidReportKind.Feature
                            ? model.Protocol.ReportLength
                            : 0
                    )
                ),
            ];
            IReadOnlyList<HidCandidate> candidates =
            [
                .. found.Where(c => !c.CouldNotOpen && Matches(model.Protocol, c)),
            ];
            TrackAccess(model, slots, blocked: candidates.Count == 0 && found.Any(c => c.CouldNotOpen));

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
                var candidate = await ResolveAsync(model, slot, units[i], cancellationToken);
                var channel = new HidChannel(
                    transport,
                    candidate.Path,
                    candidate.ProductId,
                    model.Protocol.ReportKind,
                    model.Protocol.ReadBudget
                );
                sources.Add(
                    new DelegateBatterySource(
                        slot,
                        async ct =>
                        {
                            try
                            {
                                return await model.Protocol.ReadAsync(channel, model.Device, ct);
                            }
                            catch (Exception exception)
                                when (exception is not OperationCanceledException)
                            {
                                // The mouse may have switched between dongle and cable, so look again.
                                _resolvedPaths.TryRemove(slot.Id, out _);
                                throw;
                            }
                        }
                    )
                );
            }
        }

        return sources;
    }

    private void TrackAccess(HidModel model, BatterySlot[] slots, bool blocked)
    {
        foreach (var slot in slots)
        {
            if (!blocked)
            {
                _accessProblems.MarkReachable(slot.Id);
            }
            else if (_accessProblems.MarkBlocked(slot.Id))
            {
                _logger.Warning(
                    "{Brand} {Product} is connected but could not be opened, so {DeviceId} cannot be read. On Linux "
                        + "its hidraw nodes need the udev rule from docs/linux-setup.md.",
                    model.Brand,
                    model.Name,
                    slot.Id
                );
            }
        }
    }

    private static bool Matches(HidProtocol protocol, HidCandidate candidate) =>
        protocol.ReportKind == HidReportKind.Feature
        || (
            candidate.OutputReportLength >= protocol.ReportLength
            && candidate.HasUsage(protocol.UsagePage, protocol.Usage)
        );

    // A blank or all-zero serial identifies nothing, so the path stands in for it.
    internal static string PhysicalUnitKey(HidCandidate candidate)
    {
        var serial = candidate.SerialNumber?.Trim();
        if (!string.IsNullOrEmpty(serial) && serial.Any(c => c != '0'))
        {
            return "s:" + serial.ToLowerInvariant();
        }

        if (candidate.LocationId is { } location)
        {
            return "m:" + location.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }

        var linux = HidSharpTransport.LinuxUsbInterfacePattern().Match(candidate.Path);
        if (linux.Success)
        {
            return "l:" + linux.Groups["unit"].Value;
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

    private async Task<HidCandidate> ResolveAsync(
        HidModel model,
        BatterySlot slot,
        IReadOnlyList<HidCandidate> candidates,
        CancellationToken cancellationToken
    )
    {
        if (
            _resolvedPaths.TryGetValue(slot.Id, out var cached)
            && candidates.FirstOrDefault(c =>
                string.Equals(c.Path, cached, StringComparison.OrdinalIgnoreCase)
            ) is { } known
        )
        {
            return known;
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var channel = new HidChannel(
                    transport,
                    candidate.Path,
                    candidate.ProductId,
                    model.Protocol.ReportKind,
                    // A device that only pushes needs its whole interval even to be found.
                    model.Protocol.ReadBudget ?? HidChannel.ProbeBudget
                );
                _ = await model.Protocol.ReadAsync(channel, model.Device, cancellationToken);
                _resolvedPaths[slot.Id] = candidate.Path;
                _logger.Information(
                    "HID device {DeviceId} answered on interface {Interface} ({Product}).",
                    slot.Id,
                    candidate.InterfaceNumber,
                    candidate.ProductName
                );
                return candidate;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Debug(exception, "HID candidate {Path} did not answer.", candidate.Path);
            }
        }

        // Nothing answered; keep the first path so the device shows as stale instead of disappearing.
        return candidates[0];
    }
}
