using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Logitech;

// Verified on a G Pro X Superlight 2, both behind its receiver (C54D) and on the cable (C09B).
internal sealed class LogitechProtocol()
    : HidProtocol(
        "Logitech",
        vendorId: 0x046D,
        reportLength: 20,
        HidReportKind.InputOutput,
        usagePage: 0xFF00,
        usage: 0x0002
    )
{
    private const byte LongReport = 0x11;

    // Slot 1 is the first paired device. A mouse on its cable answers to any index.
    private const byte DeviceIndex = 0x01;

    private const byte RootFeatureIndex = 0x00;
    private const ushort UnifiedBatteryFeature = 0x1004;

    // The low nibble of the function byte is a software id the device echoes back.
    private const byte SoftwareId = 0x01;
    private const byte GetFeatureFunction = 0x00 << 4 | SoftwareId;
    private const byte GetStatusFunction = 0x01 << 4 | SoftwareId;

    private const byte ErrorFeatureIndex = 0xFF;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("G Pro X Superlight 2", 0xC54D, 0xC09B)];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    )
    {
        var rootResponse = await channel.ExchangeAsync(
            BuildFeatureRequest(UnifiedBatteryFeature),
            response => IsReplyTo(response, RootFeatureIndex, GetFeatureFunction),
            cancellationToken
        );
        var featureIndex = ParseFeatureIndex(rootResponse);

        var statusResponse = await channel.ExchangeAsync(
            BuildStatusRequest(featureIndex),
            response => IsReplyTo(response, featureIndex, GetStatusFunction),
            cancellationToken
        );
        return ParseStatus(statusResponse);
    }

    internal static byte[] BuildFeatureRequest(ushort featureId) =>
        [
            LongReport,
            DeviceIndex,
            RootFeatureIndex,
            GetFeatureFunction,
            (byte)(featureId >> 8),
            (byte)(featureId & 0xFF),
        ];

    internal static byte[] BuildStatusRequest(byte featureIndex) =>
        [LongReport, DeviceIndex, featureIndex, GetStatusFunction];

    // The receiver also pushes unrelated reports, so match device, feature and function.
    // An error report counts too, so a refusal fails fast instead of timing out.
    internal static bool IsReplyTo(ReadOnlySpan<byte> report, byte featureIndex, byte function) =>
        report.Length >= 7
        && report[0] == LongReport
        && report[1] == DeviceIndex
        && (
            report[2] == ErrorFeatureIndex
            || (report[2] == featureIndex && report[3] == function)
        );

    internal static byte ParseFeatureIndex(ReadOnlySpan<byte> report)
    {
        if (report[2] == ErrorFeatureIndex)
        {
            throw new InvalidOperationException($"HID++ error 0x{report[5]:X2} looking up the battery feature.");
        }

        // Feature index 0 means the device does not have the feature.
        return report[4] != 0
            ? report[4]
            : throw new NotSupportedException("The device has no Unified Battery feature.");
    }

    internal static BatteryReading ParseStatus(ReadOnlySpan<byte> report)
    {
        if (report[2] == ErrorFeatureIndex)
        {
            throw new InvalidOperationException($"HID++ error 0x{report[5]:X2} reading the battery status.");
        }

        var percent = report[4];
        var status = report[6] switch
        {
            0 => percent >= 100 ? BatteryStatus.Full : BatteryStatus.Discharging,
            1 or 2 => BatteryStatus.Charging,
            3 => BatteryStatus.Full,
            _ => BatteryStatus.Unknown,
        };

        return new BatteryReading { Percent = percent, Status = status };
    }
}
