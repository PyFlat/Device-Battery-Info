using DeviceBatteryInfo.Core;

namespace DeviceBatteryInfo.Sources.Hid;

// List every product id: a wireless mouse has one for its dongle and another for the cable.
internal sealed record HidDeviceInfo(string Name, params int[] ProductIds)
{
    public BatterySourceKind Kind { get; init; } = BatterySourceKind.Mouse;
}

internal enum HidReportKind
{
    // Request and answer share one feature-report call (Razer).
    Feature,

    // Output report out, input reports back, on a vendor usage page (Logitech HID++).
    InputOutput,
}

internal sealed class HidChannel(
    IHidTransport transport,
    string devicePath,
    int productId,
    HidReportKind kind = HidReportKind.Feature,
    TimeSpan? budget = null
)
{
    public static readonly TimeSpan ReadBudget = TimeSpan.FromMilliseconds(2000);

    // Finding the right interface tries every candidate, so a silent one must not stall the poll for long.
    public static readonly TimeSpan ProbeBudget = TimeSpan.FromMilliseconds(400);

    public int ProductId { get; } = productId;

    // Returns the first response isComplete accepts. Throws when none does, so the poll keeps
    // the last good value.
    public Task<byte[]> ExchangeAsync(
        byte[] request,
        Func<byte[], bool> isComplete,
        CancellationToken cancellationToken
    ) =>
        kind == HidReportKind.Feature
            ? transport.ExchangeAsync(
                devicePath,
                request,
                isComplete,
                budget ?? ReadBudget,
                cancellationToken
            )
            : transport.ExchangeReportsAsync(devicePath, request, isComplete, cancellationToken);
}

// Subclass it, list the devices and implement ReadAsync. Finding the device is done for you.
internal abstract class HidProtocol(
    string brand,
    int vendorId,
    int reportLength,
    HidReportKind reportKind = HidReportKind.Feature,
    int? usagePage = null,
    int? usage = null
)
{
    public string Brand { get; } = brand;

    public int VendorId { get; } = vendorId;

    // Smallest report length the right interface must support (the output length for InputOutput).
    public int ReportLength { get; } = reportLength;

    public HidReportKind ReportKind { get; } = reportKind;

    // InputOutput only: the vendor usage of the right interface.
    public int? UsagePage { get; } = usagePage;

    public int? Usage { get; } = usage;

    public abstract IReadOnlyList<HidDeviceInfo> Devices { get; }

    // Send the request through the channel and parse the response. Throw when the device does not
    // answer: the same call is used to find the right interface.
    public abstract Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    );
}
