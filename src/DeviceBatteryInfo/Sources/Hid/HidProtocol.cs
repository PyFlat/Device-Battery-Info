using DeviceBatteryInfo.Core;

namespace DeviceBatteryInfo.Sources.Hid;

/// <summary>A device and every USB product id it shows up as. A wireless mouse has one for its dongle and
/// another for the cable, so list both.</summary>
internal sealed record HidDeviceInfo(string Name, params int[] ProductIds)
{
    public BatterySourceKind Kind { get; init; } = BatterySourceKind.Mouse;
}

/// <summary>One open HID connection to a device.</summary>
internal sealed class HidChannel(IHidTransport transport, string devicePath, int productId)
{
    /// <summary>The product id this connection is on, to tell a dongle from a cable.</summary>
    public int ProductId { get; } = productId;

    /// <summary>Sends the request and returns the first response <paramref name="isComplete"/> accepts.
    /// Throws when none does, so the poll keeps the last good value.</summary>
    public Task<byte[]> ExchangeAsync(
        byte[] request,
        Func<byte[], bool> isComplete,
        CancellationToken cancellationToken
    ) => transport.ExchangeAsync(devicePath, request, isComplete, cancellationToken);
}

/// <summary>A brand's USB HID feature-report protocol. Subclass it in one file, list the devices and
/// implement <see cref="ReadAsync"/>; finding the device is done for you. See
/// <c>docs/adding-a-device.md</c>.</summary>
internal abstract class HidProtocol(string brand, int vendorId, int reportLength)
{
    public string Brand { get; } = brand;

    public int VendorId { get; } = vendorId;

    /// <summary>Smallest feature-report length a matching HID interface must support.</summary>
    public int ReportLength { get; } = reportLength;

    public abstract IReadOnlyList<HidDeviceInfo> Devices { get; }

    /// <summary>Build the request, send it with <see cref="HidChannel.ExchangeAsync"/>, turn the response
    /// into a reading. Throw if the device does not answer: the same call finds the right interface.</summary>
    public abstract Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    );
}
