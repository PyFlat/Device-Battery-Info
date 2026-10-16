using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Logitech;

// Logitech's Centurion framing, not HID++; layout from Solaar and HeadsetControl via HaloBattery (MIT).
internal sealed class LogitechCenturionProtocol()
    : HidProtocol(
        "Logitech",
        vendorId: 0x046D,
        reportLength: 64,
        HidReportKind.InputOutput,
        usagePage: 0xFFA0,
        usage: 0x0001
    )
{
    private const byte ReportId = 0x51;
    private const byte SoftwareId = 0x01;
    private const byte RootIndex = 0x00;
    private const byte ErrorIndex = 0xFF;

    private const byte FirstFunction = 0x00;

    private const byte BridgeSend = 0x10;

    internal const ushort BridgeFeature = 0x0003;
    internal const ushort BatteryFeature = 0x0104;

    private const byte Discharging = 0;
    private const byte Charging = 1;
    private const byte ChargingOverUsb = 2;
    private const byte ChargeComplete = 3;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("G Pro X 2 Lightspeed", 0x0AF7) { Kind = BatterySourceKind.Headset }];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    )
    {
        var bridge = ParseFeatureIndex(
            await DirectAsync(channel, RootIndex, FirstFunction, Id(BridgeFeature), cancellationToken)
        );
        var battery = ParseFeatureIndex(
            await BridgeAsync(channel, bridge, RootIndex, FirstFunction, Id(BatteryFeature), cancellationToken)
        );
        return ParseBattery(await BridgeAsync(channel, bridge, battery, FirstFunction, [], cancellationToken));
    }

    internal static async Task<byte[]> DirectAsync(
        HidChannel channel,
        byte featureIndex,
        byte function,
        byte[] parameters,
        CancellationToken cancellationToken
    )
    {
        var reply = Payload(
            await channel.ExchangeAsync(
                BuildDirectRequest(featureIndex, function, parameters),
                r => IsDirectReply(r, featureIndex, function),
                cancellationToken
            )
        );
        return reply[0] == ErrorIndex
            ? throw new InvalidOperationException($"Centurion error 0x{reply[3]:X2} on receiver feature {featureIndex}.")
            : reply[2..];
    }

    internal static async Task<byte[]> BridgeAsync(
        HidChannel channel,
        byte bridgeIndex,
        byte featureIndex,
        byte function,
        byte[] parameters,
        CancellationToken cancellationToken
    )
    {
        var reply = Payload(
            await channel.ExchangeAsync(
                BuildBridgeRequest(bridgeIndex, featureIndex, function, parameters),
                r => IsBridgeReply(r, bridgeIndex, featureIndex, function),
                cancellationToken
            )
        );
        return reply[5] == ErrorIndex
            ? throw new InvalidOperationException($"The headset rejected feature {featureIndex}.")
            : reply[7..];
    }

    internal static byte[] Id(ushort featureId) => [(byte)(featureId >> 8), (byte)featureId];

    private static byte WithSoftwareId(byte function) => (byte)((function & 0xF0) | SoftwareId);

    private static byte[] Frame(params byte[] payload) =>
        [ReportId, (byte)(payload.Length + 1), 0x00, .. payload];

    internal static byte[] BuildDirectRequest(byte featureIndex, byte function, params byte[] parameters) =>
        Frame([featureIndex, WithSoftwareId(function), .. parameters]);

    internal static byte[] BuildBridgeRequest(
        byte bridgeIndex,
        byte featureIndex,
        byte function,
        params byte[] parameters
    )
    {
        byte[] message = [0x00, featureIndex, WithSoftwareId(function), .. parameters];
        return Frame(
            [
                bridgeIndex,
                WithSoftwareId(BridgeSend),
                (byte)(message.Length >> 8 & 0x0F),
                (byte)message.Length,
                .. message,
            ]
        );
    }

    internal static byte[] Payload(byte[] report) =>
        report.Length >= 4 && report[0] == ReportId && report[1] > 1 && report[1] + 2 <= report.Length
            ? report[3..(2 + report[1])]
            : [];

    internal static bool IsDirectReply(byte[] report, byte featureIndex, byte function)
    {
        var payload = Payload(report);
        var echoed = WithSoftwareId(function);
        return payload.Length >= 2
            && (
                (payload[0] == featureIndex && payload[1] == echoed)
                || (payload.Length >= 4 && payload[0] == ErrorIndex && payload[1] == featureIndex && payload[2] == echoed)
            );
    }

    // The receiver acknowledges the send first; only the headset's answer counts.
    internal static bool IsBridgeReply(byte[] report, byte bridgeIndex, byte featureIndex, byte function)
    {
        var payload = Payload(report);
        return payload.Length >= 7
            && payload[0] == bridgeIndex
            && payload[1] == BridgeSend
            && payload[4] == 0x00
            && (
                (payload[5] == featureIndex && payload[6] == WithSoftwareId(function))
                || (payload[5] == ErrorIndex && payload[6] == featureIndex)
            );
    }

    internal static byte ParseFeatureIndex(ReadOnlySpan<byte> data) =>
        data.Length > 0 && data[0] != 0
            ? data[0]
            : throw new NotSupportedException("The device does not have that Centurion feature.");

    internal static BatteryReading ParseBattery(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data[0] > 100)
        {
            throw new InvalidOperationException(
                $"The headset reported level {(data.Length == 0 ? "nothing" : data[0].ToString(System.Globalization.CultureInfo.InvariantCulture))}."
            );
        }

        var level = data[0];
        return (data.Length >= 3 ? data[2] : Discharging) switch
        {
            Discharging => BatteryReading.FromPercent(level, BatteryStatus.Discharging),
            Charging or ChargingOverUsb => BatteryReading.FromPercent(level, BatteryStatus.Charging),
            ChargeComplete => BatteryReading.FromPercent(level, BatteryStatus.Full),
            _ => BatteryReading.FromPercent(level),
        };
    }
}
