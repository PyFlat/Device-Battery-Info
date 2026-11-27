using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Asus;

// ROG mice answer an output report (12 07) on their FF01 interface, the exchange G-Helper uses.
// Byte 0 is the report id on both sides; the answer echoes the command.
internal sealed class AsusProtocol(TimeProvider time)
    : HidProtocol(
        "ASUS",
        vendorId: 0x0B05,
        reportLength: 65,
        HidReportKind.InputOutput,
        usagePage: 0xFF01,
        usage: 0x0001
    )
{
    private long _lastCableAnswerTicks;

    public AsusProtocol()
        : this(TimeProvider.System) { }

    private static readonly byte[] BatteryRequest = [ReportId, 0x12, 0x07];

    private const byte ReportId = 0x00;
    private const int LevelIndex = 5;
    private const int AnswerLength = 8;

    // The mouse moves to its cable id as soon as the cable is in, so that id means it charges.
    internal const int CableProductId = 0x1947;

    // Back on the dongle the level still reads a step high for a while (10 and 30 s in the owner's logs).
    internal static readonly TimeSpan CableSettleTime = TimeSpan.FromSeconds(60);

    // The Strix Impact II Wireless reports its level in quarters (0-4).
    private const int LevelStep = 25;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("ROG Strix Impact II Wireless", 0x1949, CableProductId)];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    )
    {
        var report = await channel.ExchangeAsync(BatteryRequest, IsAnswer, cancellationToken);
        var onCable = channel.ProductId == CableProductId;
        var reading = ParseBattery(report, onCable);
        var now = time.GetUtcNow().UtcTicks;
        if (onCable)
        {
            Volatile.Write(ref _lastCableAnswerTicks, now);
            return reading;
        }

        return now - Volatile.Read(ref _lastCableAnswerTicks) < CableSettleTime.Ticks
            ? new BatteryReading { Status = BatteryStatus.Charging }
            : reading;
    }

    // A dongle whose mouse is asleep or off answers FF AA instead.
    internal static bool IsAnswer(byte[] report) =>
        report.Length >= AnswerLength
        && report[0] == ReportId
        && (IsBatteryReport(report) || IsMouseUnreachable(report));

    private static bool IsBatteryReport(byte[] report) => report[1] == 0x12 && report[2] == 0x07;

    private static bool IsMouseUnreachable(byte[] report) => report[1] == 0xFF && report[2] == 0xAA;

    internal static BatteryReading ParseBattery(byte[] report, bool onCable)
    {
        if (IsMouseUnreachable(report))
        {
            throw new InvalidOperationException("The dongle cannot reach the mouse: it is asleep or off.");
        }

        var level = report[LevelIndex];
        if (level > 100 / LevelStep)
        {
            throw new InvalidOperationException($"The mouse reported level {level}.");
        }

        // On the cable the level reads a step high (4 against 3 on the dongle a moment later).
        if (onCable)
        {
            return new BatteryReading { Status = BatteryStatus.Charging };
        }

        // G-Helper reads a 0 as a mouse that is not ready, not as an empty battery.
        if (level == 0)
        {
            throw new InvalidOperationException("The mouse reported level 0: it is asleep or off.");
        }

        var percent = level * LevelStep;
        return BatteryReading.FromPercent(
            percent,
            percent >= 100 ? BatteryStatus.Full : BatteryStatus.Discharging
        );
    }
}
