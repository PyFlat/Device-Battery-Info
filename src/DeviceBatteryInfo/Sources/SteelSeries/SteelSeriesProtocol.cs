using System.Collections.Concurrent;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;
using Serilog;

namespace DeviceBatteryInfo.Sources.SteelSeries;

// The Rival 3 Wireless answers an output report (AA 01) with an input report whose byte 1 is the level.
// Same exchange as rivalcfg; byte 0 is the report id on both sides. It runs on AAA cells, so it never charges.
// The Arctis GameBuds answer B0 with each bud's status and level, the exchange HeadsetControl uses.
internal sealed class SteelSeriesProtocol()
    : HidProtocol(
        "SteelSeries",
        vendorId: 0x1038,
        reportLength: 65,
        HidReportKind.InputOutput,
        usagePage: 0xFFC0,
        usage: 0x0001
    )
{
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, string> _lastFrames = new(StringComparer.Ordinal);

    public SteelSeriesProtocol(ILogger logger)
        : this() => _logger = logger.ForContext<SteelSeriesProtocol>();

    private static readonly byte[] BatteryRequest = [ReportId, 0xAA, 0x01];
    private static readonly byte[] GameBudsRequest = [ReportId, 0xB0];

    private const byte ReportId = 0x00;
    private const int LevelIndex = 1;

    internal const int GameBudsProductId = 0x230A;
    private const int LeftBudStatusIndex = 4;
    private const int RightBudStatusIndex = 5;
    private const int LeftBudLevelIndex = 6;
    private const int RightBudLevelIndex = 7;
    private const byte BudOutOfCase = 0x03;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [
            new("Rival 3 Wireless", 0x1830),
            new("Arctis GameBuds", GameBudsProductId) { Kind = BatterySourceKind.Earbuds },
        ];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    )
    {
        if (channel.ProductId != GameBudsProductId)
        {
            return ParseBattery(
                await channel.ExchangeAsync(BatteryRequest, IsBatteryReport, cancellationToken)
            );
        }

        var report = await channel.ExchangeAsync(
            GameBudsRequest,
            incoming =>
            {
                LogFrameChange(channel, incoming);
                return IsGameBudsReport(incoming);
            },
            cancellationToken
        );
        return ParseGameBuds(report);
    }

    // Test build only: logs every report, not just accepted ones, so pushed reports and a missing B0 echo show.
    private void LogFrameChange(HidChannel channel, byte[] report)
    {
        var frame = Convert.ToHexString(report, 0, Math.Min(report.Length, 16));
        if (_lastFrames.TryGetValue(channel.DevicePath, out var last) && last == frame)
        {
            return;
        }

        _lastFrames[channel.DevicePath] = frame;
        _logger?.Information(
            "SteelSeries GameBuds answer on PID {ProductId:X4}: {Frame}",
            channel.ProductId,
            frame
        );
    }

    internal static bool IsBatteryReport(byte[] report) =>
        report.Length > LevelIndex && report[0] == ReportId;

    internal static bool IsGameBudsReport(byte[] report) =>
        report.Length > RightBudLevelIndex && report[0] == ReportId;

    internal static BatteryReading ParseBattery(ReadOnlySpan<byte> report)
    {
        var level = report[LevelIndex];
        if (level > 100)
        {
            throw new InvalidOperationException($"The mouse reported level {level}.");
        }

        return BatteryReading.FromPercent(level, BatteryStatus.Discharging);
    }

    // A bud in the case is switched off and reads 0%, so only buds out of the case count.
    internal static BatteryReading ParseGameBuds(ReadOnlySpan<byte> report)
    {
        int? level = null;
        if (report[LeftBudStatusIndex] == BudOutOfCase)
        {
            level = report[LeftBudLevelIndex];
        }

        if (report[RightBudStatusIndex] == BudOutOfCase)
        {
            level = Math.Min(level ?? int.MaxValue, report[RightBudLevelIndex]);
        }

        if (level is null)
        {
            throw new InvalidOperationException(
                $"No bud is out of the case (status {report[LeftBudStatusIndex]:X2}/{report[RightBudStatusIndex]:X2})."
            );
        }

        if (level > 100)
        {
            throw new InvalidOperationException($"The buds reported level {level}.");
        }

        return BatteryReading.FromPercent(level.Value, BatteryStatus.Discharging);
    }
}
