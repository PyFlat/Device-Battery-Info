using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Aula;

// Compx 2.4G receiver frames (battery-hub, womier-l65-linux): report 0x13, 20 bytes, byte-sum checksum
// last. Wired mode enumerates as a separate Sinowealth device (258A) and is not read.
internal sealed class AulaProtocol()
    : HidProtocol(
        "AULA",
        vendorId: 0x3554,
        reportLength: FrameLength,
        HidReportKind.InputOutput,
        usagePage: 0xFF02,
        usage: 0x0002
    )
{
    private const int FrameLength = 20;
    private const byte ReportId = 0x13;
    private const byte BatteryCommand = 0x4A;
    private const int LevelIndex = 5;
    private const int StateIndex = 6;

    private const byte OnBattery = 0x01;

    // Set while the cable is in. The level then always reads 100 (a 97% keyboard included), so it
    // says neither the charge nor whether charging has finished.
    private const byte OnCable = 0x10;

    private static readonly byte[] BatteryRequest = BuildRequest(BatteryCommand);

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("F75", 0xFA09) { Kind = BatterySourceKind.Keyboard }];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    ) => ParseBattery(await channel.ExchangeAsync(BatteryRequest, IsBatteryReply, cancellationToken));

    internal static byte[] BuildRequest(byte command)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = command;
        frame[^1] = Checksum(frame);
        return frame;
    }

    // Sum of every byte before the last, report id included.
    private static byte Checksum(ReadOnlySpan<byte> frame)
    {
        byte sum = 0;
        foreach (var b in frame[..^1])
        {
            sum += b;
        }

        return sum;
    }

    // The receiver also pushes other 0x13 frames (a 0x0A status among them), so match the command. The
    // reply may set the command's high bit (womier-l65-linux masks it), hence the 0x7F.
    internal static bool IsBatteryReply(byte[] report) =>
        report.Length >= FrameLength
        && report[0] == ReportId
        && (report[1] & 0x7F) == BatteryCommand
        && report[^1] == Checksum(report);

    internal static BatteryReading ParseBattery(ReadOnlySpan<byte> report)
    {
        var level = report[LevelIndex];
        var state = report[StateIndex];

        // battery-hub only trusts 1-100; anything else is no reading, not an empty battery.
        if (level is 0 or > 100)
        {
            throw new InvalidOperationException($"The keyboard reported level {level}.");
        }

        return state switch
        {
            OnCable => new BatteryReading { Status = BatteryStatus.Charging },
            OnBattery => BatteryReading.FromPercent(level, BatteryStatus.Discharging),
            _ => BatteryReading.FromPercent(level),
        };
    }
}
