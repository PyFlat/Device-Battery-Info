using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Logitech;

// HID++ 2.0: look a feature up by id, then call it. Which interface, which device index and which
// feature answers the battery differ per product line, the framing does not.
internal abstract class LogitechHidppProtocol(int usagePage, int usage, byte deviceIndex)
    : HidProtocol(
        "Logitech",
        vendorId: 0x046D,
        reportLength: 20,
        HidReportKind.InputOutput,
        usagePage,
        usage
    )
{
    private const byte LongReport = 0x11;
    private const byte RootFeatureIndex = 0x00;
    private const byte ErrorFeatureIndex = 0xFF;

    // The low nibble of the function byte is a software id the device echoes back.
    private const byte SoftwareId = 0x01;

    private static readonly byte GetFeatureFunction = Function(0x00);

    // The feature that answers the battery, and the function on it to call.
    protected abstract ushort BatteryFeature { get; }

    protected abstract byte BatteryFunction { get; }

    protected abstract BatteryReading ParseBattery(ReadOnlySpan<byte> report);

    public sealed override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    )
    {
        var rootResponse = await channel.ExchangeAsync(
            BuildFeatureRequest(deviceIndex, BatteryFeature),
            response => IsReplyTo(response, deviceIndex, RootFeatureIndex, GetFeatureFunction),
            cancellationToken
        );
        var featureIndex = ParseFeatureIndex(rootResponse);

        var function = Function(BatteryFunction);
        var batteryResponse = await channel.ExchangeAsync(
            BuildCallRequest(deviceIndex, featureIndex, function),
            response => IsReplyTo(response, deviceIndex, featureIndex, function),
            cancellationToken
        );
        return ParseBattery(batteryResponse);
    }

    internal static byte Function(byte functionId) => (byte)(functionId << 4 | SoftwareId);

    internal static byte[] BuildFeatureRequest(byte deviceIndex, ushort featureId) =>
        [
            LongReport,
            deviceIndex,
            RootFeatureIndex,
            GetFeatureFunction,
            (byte)(featureId >> 8),
            (byte)(featureId & 0xFF),
        ];

    internal static byte[] BuildCallRequest(byte deviceIndex, byte featureIndex, byte function) =>
        [LongReport, deviceIndex, featureIndex, function];

    // The interface also carries unrelated reports, so match device, feature and function.
    // An error report counts too, so a refusal fails fast instead of timing out.
    internal static bool IsReplyTo(
        ReadOnlySpan<byte> report,
        byte deviceIndex,
        byte featureIndex,
        byte function
    ) =>
        report.Length >= 7
        && report[0] == LongReport
        && report[1] == deviceIndex
        && (report[2] == ErrorFeatureIndex || (report[2] == featureIndex && report[3] == function));

    internal static byte ParseFeatureIndex(ReadOnlySpan<byte> report)
    {
        ThrowOnError(report, "looking up the battery feature");

        // Feature index 0 means the device does not have the feature.
        return report[4] != 0
            ? report[4]
            : throw new NotSupportedException("The device does not have that battery feature.");
    }

    protected static void ThrowOnError(ReadOnlySpan<byte> report, string what)
    {
        if (report[2] == ErrorFeatureIndex)
        {
            throw new InvalidOperationException($"HID++ error 0x{report[5]:X2} {what}.");
        }
    }
}
