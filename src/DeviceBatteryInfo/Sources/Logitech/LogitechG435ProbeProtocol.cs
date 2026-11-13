using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;
using HidSharp;
using Serilog;

namespace DeviceBatteryInfo.Sources.Logitech;

// Probe build for the G435 (dongle 0ADF), whose only vendor collection is FF00:0001. It logs what the
// dongle exposes and answers to HID++ requests; it is not meant to ship.
internal sealed class LogitechG435ProbeProtocol()
    : HidProtocol(
        "Logitech",
        vendorId: 0x046D,
        reportLength: 1,
        HidReportKind.InputOutput,
        usagePage: 0xFF00,
        usage: 0x0001
    )
{
    private const int ProductId = 0x0ADF;
    private const byte ShortReport = 0x10;
    private const byte LongReport = 0x11;
    private const byte SoftwareId = 0x0A;
    private const byte PingData = 0x5A;

    private static readonly byte[] DeviceIndexes = [0xFF, 0x01, 0x00];

    private static readonly (ushort Id, string Name)[] Features =
    [
        (0x0001, "FeatureSet"),
        (0x0003, "FirmwareInfo"),
        (0x0005, "DeviceName"),
        (0x1000, "BatteryStatus"),
        (0x1001, "BatteryVoltage"),
        (0x1004, "UnifiedBattery"),
        (0x1F20, "AdcMeasurement"),
        (0x0104, "CenturionBattery"),
    ];

    private readonly ILogger? _logger;
    private readonly Lock _lock = new();
    private bool _probed;
    private (byte ReportId, byte DeviceIndex, byte FeatureIndex, ushort FeatureId)? _battery;

    public LogitechG435ProbeProtocol(ILogger logger)
        : this() => _logger = logger.ForContext<LogitechG435ProbeProtocol>();

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [new("G435 (probe)", ProductId) { Kind = BatterySourceKind.Headset }];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    )
    {
        bool probe;
        lock (_lock)
        {
            probe = !_probed;
            _probed = true;
        }

        if (probe)
        {
            await ProbeAsync(channel, cancellationToken);
        }

        if (_battery is not { } battery)
        {
            throw new InvalidOperationException("G435 probe: no battery feature answered.");
        }

        var function = Function(battery.FeatureId == 0x1004 ? (byte)0x01 : (byte)0x00);
        var reply = await ExchangeAsync(
            channel,
            [battery.ReportId, battery.DeviceIndex, battery.FeatureIndex, function],
            battery.DeviceIndex,
            "battery read",
            cancellationToken
        );
        if (reply is null || reply[2] != battery.FeatureIndex)
        {
            throw new InvalidOperationException("G435 probe: the battery read did not answer.");
        }

        return battery.FeatureId switch
        {
            0x1004 or 0x1000 => BatteryReading.FromPercent(reply[4]),
            _ => throw new InvalidOperationException(
                $"G435 probe: feature {battery.FeatureId:X4} answered, see the log."
            ),
        };
    }

    private async Task ProbeAsync(HidChannel channel, CancellationToken cancellationToken)
    {
        Log("G435 probe: start on {0}", channel.DevicePath);
        LogInterfaces();

        try
        {
            await channel.ExchangeAsync(
                [],
                report =>
                {
                    Log("G435 probe: unprompted report {0}", Hex(report));
                    return false;
                },
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log("G435 probe: listening ended ({0})", exception.Message);
        }

        foreach (var reportId in new[] { ShortReport, LongReport })
        {
            foreach (var deviceIndex in DeviceIndexes)
            {
                var ping = await ExchangeAsync(
                    channel,
                    [reportId, deviceIndex, 0x00, Function(0x01), 0x00, 0x00, PingData],
                    deviceIndex,
                    $"ping {reportId:X2} to device {deviceIndex:X2}",
                    cancellationToken
                );
                if (ping is null || ping[2] != 0x00)
                {
                    continue;
                }

                Log(
                    "G435 probe: HID++ {0}.{1} on report {2:X2}, device {3:X2}",
                    ping[4],
                    ping[5],
                    reportId,
                    deviceIndex
                );
                await LookUpFeaturesAsync(channel, reportId, deviceIndex, cancellationToken);
                if (_battery is not null)
                {
                    return;
                }
            }
        }

        Log("G435 probe: done, no battery feature found");
    }

    private async Task LookUpFeaturesAsync(
        HidChannel channel,
        byte reportId,
        byte deviceIndex,
        CancellationToken cancellationToken
    )
    {
        foreach (var (id, name) in Features)
        {
            var reply = await ExchangeAsync(
                channel,
                [reportId, deviceIndex, 0x00, Function(0x00), (byte)(id >> 8), (byte)(id & 0xFF)],
                deviceIndex,
                $"getFeature {id:X4} {name}",
                cancellationToken
            );
            if (reply is null || reply[2] != 0x00 || reply[4] == 0)
            {
                continue;
            }

            Log("G435 probe: {0} ({1:X4}) is at index {2:X2}", name, id, reply[4]);
            if (id is 0x1000 or 0x1001 or 0x1004 or 0x1F20 && _battery is null)
            {
                _battery = (reportId, deviceIndex, reply[4], id);
            }
        }
    }

    private async Task<byte[]?> ExchangeAsync(
        HidChannel channel,
        byte[] request,
        byte deviceIndex,
        string what,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var reply = await channel.ExchangeAsync(
                request,
                report =>
                {
                    Log("G435 probe: {0} <- {1}", what, Hex(report));
                    return report.Length >= 7
                        && report[0] is ShortReport or LongReport
                        && report[1] == deviceIndex
                        && (report[2] is 0x8F or 0xFF || report[2] == request[2]);
                },
                cancellationToken
            );
            return reply;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log("G435 probe: {0} -> {1}: no answer ({2})", what, Hex(request), exception.Message);
            return null;
        }
    }

    private void LogInterfaces()
    {
        try
        {
            foreach (var device in DeviceList.Local.GetHidDevices(VendorId, ProductId))
            {
                string descriptor;
                try
                {
                    descriptor = Convert.ToHexString(device.GetRawReportDescriptor());
                }
                catch (Exception exception)
                {
                    descriptor = $"unreadable ({exception.Message})";
                }

                Log(
                    "G435 probe: interface {0} in {1} out {2} feature {3} descriptor {4}",
                    device.DevicePath,
                    SafeLength(device.GetMaxInputReportLength),
                    SafeLength(device.GetMaxOutputReportLength),
                    SafeLength(device.GetMaxFeatureReportLength),
                    descriptor
                );
            }
        }
        catch (Exception exception)
        {
            Log("G435 probe: listing interfaces failed ({0})", exception.Message);
        }
    }

    private static int SafeLength(Func<int> length)
    {
        try
        {
            return length();
        }
        catch
        {
            return -1;
        }
    }

    // Everything goes into the rendered message: structured properties are not kept in the log file.
    private void Log(string format, params object[] args) =>
        _logger?.Information("{Message}", string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));

    private static byte Function(byte functionId) => (byte)(functionId << 4 | SoftwareId);

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 32));
}
