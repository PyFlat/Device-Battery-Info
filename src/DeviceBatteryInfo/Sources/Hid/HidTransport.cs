using System.Diagnostics;
using System.Text.RegularExpressions;
using HidSharp;
using Serilog;

namespace DeviceBatteryInfo.Sources.Hid;

internal sealed record HidCandidate(
    string Path,
    int VendorId,
    int ProductId,
    int? InterfaceNumber,
    string ProductName,
    int FeatureReportLength,
    string? SerialNumber = null,
    int InputReportLength = 0,
    int OutputReportLength = 0,
    int? UsagePage = null,
    int? Usage = null,
    IReadOnlyList<(int Page, int Usage)>? Usages = null
)
{
    // macOS presents one device per interface with every top-level collection inside it, while Windows
    // presents one device per collection, so the first usage alone is not enough to find a vendor collection.
    public bool HasUsage(int? page, int? usage) =>
        Usages is { } all ? all.Contains((page ?? -1, usage ?? -1)) : UsagePage == page && Usage == usage;
}

// Plumbing only: a protocol owns its report layout and passes finished request bytes in.
internal interface IHidTransport
{
    IReadOnlyList<HidCandidate> FindCandidates(
        int vendorId,
        int productId,
        int? interfaceNumber,
        int minFeatureReportLength
    );

    IReadOnlyList<HidCandidate> ListFeatureReportDevices();

    // Retries until isComplete accepts the response or the budget runs out, then throws.
    Task<byte[]> ExchangeAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        TimeSpan budget,
        CancellationToken cancellationToken
    );

    // Reads incoming reports until isComplete accepts one or the budget runs out. The device may send
    // unrelated reports first.
    Task<byte[]> ExchangeReportsAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        TimeSpan budget,
        CancellationToken cancellationToken
    );

    // Reads one feature report without writing anything first (unlike ExchangeAsync).
    Task<byte[]> GetFeatureAsync(string devicePath, byte reportId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed partial class HidSharpTransport : IHidTransport
{
    // Keep the SetFeature/GetFeature window tight so a foreign poller's answer cannot land in it.
    private static readonly TimeSpan FirstSettleDelay = TimeSpan.FromMilliseconds(3);
    private static readonly TimeSpan MaxSettleDelay = TimeSpan.FromMilliseconds(50);

    // Jittered, so retries do not stay in lockstep with a placeholder frame or a foreign poller.
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMilliseconds(120);

    private readonly ILogger _logger;
    private readonly Func<string, IFeatureChannel> _openFeatureChannel;
    private readonly bool _boundBlockingOpen;

    public HidSharpTransport(ILogger logger)
        : this(logger, OpenPlatformChannel, boundBlockingOpen: !OperatingSystem.IsWindows()) { }

    // HidSharp retries a refused open for about a second on macOS, so there the whole exchange is
    // bounded by its budget instead of blocking the poll.
    internal HidSharpTransport(
        ILogger logger,
        Func<string, IFeatureChannel> openFeatureChannel,
        bool boundBlockingOpen
    )
    {
        _logger = logger.ForContext<HidSharpTransport>();
        _openFeatureChannel = openFeatureChannel;
        _boundBlockingOpen = boundBlockingOpen;
    }

    private static IFeatureChannel OpenPlatformChannel(string devicePath) =>
        OperatingSystem.IsWindows()
            ? new NativeFeatureChannel(NativeHid.Open(devicePath))
            : new HidSharpFeatureChannel(devicePath);

    [GeneratedRegex(
        @"mi_(?<n>[0-9a-fA-F]{1,2})|IOUSBHostInterface@(?<n>[0-9a-fA-F]+)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex InterfacePattern();

    public IReadOnlyList<HidCandidate> FindCandidates(
        int vendorId,
        int productId,
        int? interfaceNumber,
        int minFeatureReportLength
    ) =>
        DeviceList
            .Local.GetHidDevices(vendorId, productId)
            .Select(d => Describe(d, withUsage: true))
            .Where(c => c.FeatureReportLength >= minFeatureReportLength)
            .Where(c => interfaceNumber is null || c.InterfaceNumber == interfaceNumber)
            // Which HID collection answers varies by model, so try them in ascending order and let the caller's
            // probe decide.
            .OrderBy(c => c.InterfaceNumber ?? int.MaxValue)
            .ToArray();

    public IReadOnlyList<HidCandidate> ListFeatureReportDevices() =>
        DeviceList
            .Local.GetHidDevices()
            .Select(d => Describe(d))
            .Where(c => c.FeatureReportLength > 0)
            .OrderBy(c => c.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.InterfaceNumber ?? int.MaxValue)
            .ToArray();

    public Task<byte[]> ExchangeAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        TimeSpan budget,
        CancellationToken cancellationToken
    ) =>
        BoundAsync(
            $"HID feature-report exchange on {devicePath}",
            budget,
            ct => ExchangeFeatureAsync(devicePath, request, isComplete, budget, ct),
            cancellationToken
        );

    // Budget expiry is an InvalidOperationException, never a cancellation: HidFamily and the poll loop
    // let OperationCanceledException escape and would skip every other source of the cycle.
    internal async Task<byte[]> BoundAsync(
        string operation,
        TimeSpan budget,
        Func<CancellationToken, Task<byte[]>> work,
        CancellationToken cancellationToken
    )
    {
        if (!_boundBlockingOpen)
        {
            return await work(cancellationToken);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = Task.Run(() => work(linked.Token), CancellationToken.None);
        try
        {
            return await task.WaitAsync(budget, cancellationToken);
        }
        catch (TimeoutException) when (!task.IsCompleted)
        {
            await linked.CancelAsync();
            throw new InvalidOperationException(
                $"{operation} did not complete within {budget.TotalMilliseconds} ms."
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"{operation} was cancelled before it completed.");
        }
        catch (OperationCanceledException)
        {
            await linked.CancelAsync();
            throw;
        }
    }

    private async Task<byte[]> ExchangeFeatureAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        TimeSpan budget,
        CancellationToken cancellationToken
    )
    {
        var reportLength =
            DeviceList
                .Local.GetHidDevices()
                .FirstOrDefault(d =>
                    string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)
                )
                ?.GetMaxFeatureReportLength()
            ?? request.Length + 1;

        // hidapi prepends a report-id byte: the request lands at offset 1 when the report length carries it,
        // otherwise at 0.
        var offset = Math.Clamp(reportLength - request.Length, 0, 1);
        var buffer = new byte[reportLength];
        request.CopyTo(buffer, offset);

        using var channel = _openFeatureChannel(devicePath);

        var clock = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            channel.Set(buffer);
            await Task.Delay(SettleDelay(attempt), cancellationToken);

            var response = new byte[reportLength];
            channel.Get(response);
            if (isComplete(response))
            {
                return response;
            }

            _logger.Debug(
                "HID feature-report exchange on {Path} not ready, attempt {Attempt}: {Response}",
                devicePath,
                attempt,
                Convert.ToHexString(response)
            );

            if (clock.Elapsed >= budget)
            {
                throw new InvalidOperationException(
                    $"HID feature-report exchange on {devicePath} did not complete within "
                        + $"{budget.TotalMilliseconds} ms ({attempt} attempts). Vendor software polling the "
                        + "same device can keep answering in its place."
                );
            }

            await Task.Delay(NextRetryDelay(), cancellationToken);
        }
    }

    public Task<byte[]> GetFeatureAsync(
        string devicePath,
        byte reportId,
        CancellationToken cancellationToken
    )
    {
        var device =
            DeviceList
                .Local.GetHidDevices()
                .FirstOrDefault(d =>
                    string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)
                ) ?? throw new InvalidOperationException($"HID device {devicePath} is not present.");

        cancellationToken.ThrowIfCancellationRequested();
        var report = new byte[device.GetMaxFeatureReportLength()];
        report[0] = reportId;
        using var channel = _openFeatureChannel(devicePath);
        channel.Get(report);
        return Task.FromResult(report);
    }

    private static TimeSpan SettleDelay(int attempt) =>
        TimeSpan.FromMilliseconds(
            Math.Min(
                FirstSettleDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt - 1, 10)),
                MaxSettleDelay.TotalMilliseconds
            )
        );

    private static TimeSpan NextRetryDelay() =>
        TimeSpan.FromMilliseconds(
            Random.Shared.Next(
                (int)MinRetryDelay.TotalMilliseconds,
                (int)MaxRetryDelay.TotalMilliseconds
            )
        );

    private const int ReportReadTimeoutMs = 250;

    public Task<byte[]> ExchangeReportsAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        TimeSpan budget,
        CancellationToken cancellationToken
    ) =>
        BoundAsync(
            $"HID report exchange on {devicePath}",
            budget,
            ct => ExchangeInputOutputReportsAsync(devicePath, request, isComplete, budget, ct),
            cancellationToken
        );

    private async Task<byte[]> ExchangeInputOutputReportsAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        TimeSpan budget,
        CancellationToken cancellationToken
    )
    {
        var device =
            DeviceList
                .Local.GetHidDevices()
                .FirstOrDefault(d =>
                    string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)
                ) ?? throw new InvalidOperationException($"HID device {devicePath} is not present.");

        return await Task.Run(
            () =>
            {
                using var stream = device.Open();
                cancellationToken.ThrowIfCancellationRequested();
                stream.ReadTimeout = ReportReadTimeoutMs;

                // An empty request only listens, for a device that pushes its state on its own (Rapoo).
                if (request.Length > 0)
                {
                    var output = new byte[Math.Max(OutputReportLength(device, request), request.Length)];
                    request.CopyTo(output, 0);
                    stream.Write(output);
                }

                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < budget)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var input = new byte[device.GetMaxInputReportLength()];
                    try
                    {
                        stream.Read(input);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }

                    if (isComplete(input))
                    {
                        return input;
                    }

                    _logger.Debug(
                        "HID report on {Path} did not match the request: {Report}",
                        devicePath,
                        Convert.ToHexString(input)
                    );
                }

                throw new InvalidOperationException(
                    $"HID report exchange on {devicePath} was not answered within {budget.TotalMilliseconds} ms."
                );
            },
            cancellationToken
        );
    }

    // Off Windows one device carries every collection, so its longest output report is not the length of the
    // report being sent and the device would ignore a padded one.
    private static int OutputReportLength(HidDevice device, byte[] request)
    {
        var longest = device.GetMaxOutputReportLength();
        if (OperatingSystem.IsWindows() || request.Length == 0)
        {
            return longest;
        }

        try
        {
            return OutputReportLength(
                device
                    .GetReportDescriptor()
                    .DeviceItems.SelectMany(i => i.OutputReports)
                    .Select(r => (r.ReportID, r.Length)),
                request[0],
                longest
            );
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return longest;
        }
    }

    internal static int OutputReportLength(
        IEnumerable<(byte ReportId, int Length)> reports,
        byte reportId,
        int fallback
    ) => reports.Where(r => r.ReportId == reportId).Select(r => r.Length).FirstOrDefault(fallback);

    internal static int? ParseInterfaceNumber(string devicePath)
    {
        var match = InterfacePattern().Match(devicePath);
        return match.Success
            ? int.Parse(match.Groups["n"].Value, System.Globalization.NumberStyles.HexNumber, null)
            : null;
    }

    internal static HidCandidate Describe(HidDevice device, bool withUsage = false)
    {
        int featureLength;
        try
        {
            featureLength = device.GetMaxFeatureReportLength();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            featureLength = 0;
        }

        var interfaceNumber = ParseInterfaceNumber(device.DevicePath);
        var usages = withUsage ? TopLevelUsages(device) : null;

        return new HidCandidate(
            device.DevicePath,
            device.VendorID,
            device.ProductID,
            interfaceNumber,
            SafeName(device),
            featureLength,
            SafeSerial(device),
            SafeLength(device.GetMaxInputReportLength),
            SafeLength(device.GetMaxOutputReportLength),
            usages is { Count: > 0 } ? usages[0].Page : null,
            usages is { Count: > 0 } ? usages[0].Usage : null,
            usages
        );
    }

    private static int SafeLength(Func<int> length)
    {
        try
        {
            return length();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return 0;
        }
    }

    private static IReadOnlyList<(int Page, int Usage)>? TopLevelUsages(HidDevice device)
    {
        try
        {
            return
            [
                .. device
                    .GetReportDescriptor()
                    .DeviceItems.SelectMany(i => i.Usages.GetAllValues())
                    .Select(value => ((int)(value >> 16), (int)(value & 0xFFFF))),
            ];
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static string SafeName(HidDevice device)
    {
        try
        {
            var name = device.GetProductName();
            return string.IsNullOrWhiteSpace(name) ? "HID device" : name.Trim();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return "HID device";
        }
    }

    private static string? SafeSerial(HidDevice device)
    {
        try
        {
            var serial = device.GetSerialNumber();
            return string.IsNullOrWhiteSpace(serial) ? null : serial.Trim();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}
