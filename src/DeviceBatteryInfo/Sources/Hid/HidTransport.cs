using System.Diagnostics;
using System.Text.RegularExpressions;
using HidSharp;
using HidSharp.Reports;
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
    IReadOnlyList<(int Page, int Usage)>? Usages = null,
    long? LocationId = null
)
{
    // macOS and Linux present one device per interface with every top-level collection inside it, Windows one
    // per collection, so the first usage alone is not enough to find a vendor collection.
    public bool HasUsage(int? page, int? usage) =>
        Usages is { } all ? all.Contains((page ?? -1, usage ?? -1)) : UsagePage == page && Usage == usage;

    // Every HID interface has at least one report, so no length at all means it could not be opened: on Linux
    // a hidraw node without the udev rule, which would otherwise look exactly like an absent device.
    public bool CouldNotOpen => FeatureReportLength == 0 && InputReportLength == 0 && OutputReportLength == 0;
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

    // HidSharp retries a refused open for about a second on macOS, so off Windows the whole exchange is
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
        OperatingSystem.IsWindows() ? new NativeFeatureChannel(NativeHid.Open(devicePath))
        : OperatingSystem.IsLinux() ? new LinuxFeatureChannel(LinuxHidraw.Open(devicePath))
        : OperatingSystem.IsMacOS() ? new MacFeatureChannel(MacHid.Open(devicePath))
        : throw new PlatformNotSupportedException();

    [GeneratedRegex(@"mi_(?<n>[0-9a-fA-F]{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsInterfacePattern();

    // Linux paths are sysfs paths through the USB interface node, "<port>:<config>.<interface>" in decimal,
    // e.g. /sys/devices/.../usb1/1-5/1-5.3/1-5.3:1.1/0003:1532:00B7.0002/hidraw/hidraw1.
    [GeneratedRegex(@"^(?<unit>/sys/devices/.*)/\d+-[\d.]+:\d+\.(?<n>\d+)/")]
    internal static partial Regex LinuxUsbInterfacePattern();

    public IReadOnlyList<HidCandidate> FindCandidates(
        int vendorId,
        int productId,
        int? interfaceNumber,
        int minFeatureReportLength
    ) =>
        ListDevices(vendorId, productId, withUsage: true)
            .Where(c => c.CouldNotOpen || c.FeatureReportLength >= minFeatureReportLength)
            .Where(c => interfaceNumber is null || c.InterfaceNumber == interfaceNumber)
            // Which HID collection answers varies by model, so try them in ascending order and let the caller's
            // probe decide.
            .OrderBy(c => c.InterfaceNumber ?? int.MaxValue)
            .ToArray();

    public IReadOnlyList<HidCandidate> ListFeatureReportDevices() =>
        ListDevices(vendorId: null, productId: null, withUsage: false)
            .Where(c => c.FeatureReportLength > 0)
            .OrderBy(c => c.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.InterfaceNumber ?? int.MaxValue)
            .ToArray();

    private static IEnumerable<HidCandidate> ListDevices(int? vendorId, int? productId, bool withUsage) =>
        OperatingSystem.IsMacOS()
            ? MacHid
                .Enumerate()
                .Where(e => (vendorId is null || e.VendorId == vendorId) && (productId is null || e.ProductId == productId))
                .Select(Describe)
            : DeviceList.Local.GetHidDevices(vendorId, productId).Select(d => Describe(d, withUsage));

    private static HidDevice? FindHidSharpDevice(string devicePath) =>
        DeviceList
            .Local.GetHidDevices()
            .FirstOrDefault(d => string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));

    private static int? FeatureReportLength(string devicePath) =>
        OperatingSystem.IsMacOS()
            ? MacHid.Find(devicePath) is { } entry ? Describe(entry).FeatureReportLength : null
            : FindHidSharpDevice(devicePath)?.GetMaxFeatureReportLength();

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
        var reportLength = FeatureReportLength(devicePath) ?? request.Length + 1;

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
        var reportLength =
            FeatureReportLength(devicePath)
            ?? throw new InvalidOperationException($"HID device {devicePath} is not present.");

        cancellationToken.ThrowIfCancellationRequested();
        var report = new byte[reportLength];
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

    private static readonly TimeSpan ReportReadTimeout = TimeSpan.FromMilliseconds(250);

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
    ) =>
        await Task.Run(
            () =>
            {
                using var stream = OpenReportStream(devicePath);
                cancellationToken.ThrowIfCancellationRequested();

                // An empty request only listens, for a device that pushes its state on its own (Rapoo).
                if (request.Length > 0)
                {
                    var output = new byte[Math.Max(stream.OutputReportLength(request[0]), request.Length)];
                    request.CopyTo(output, 0);
                    stream.Write(output);
                }

                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < budget)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (stream.Read(ReportReadTimeout) is not { } input)
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

    private static IReportStream OpenReportStream(string devicePath)
    {
        if (OperatingSystem.IsMacOS())
        {
            var entry =
                MacHid.Find(devicePath)
                ?? throw new InvalidOperationException($"HID device {devicePath} is not present.");
            return new MacReportStream(MacHid.Open(devicePath), ParseDescriptor(entry.ReportDescriptor));
        }

        var device =
            FindHidSharpDevice(devicePath)
            ?? throw new InvalidOperationException($"HID device {devicePath} is not present.");
        return new HidSharpReportStream(device);
    }

    // Off Windows one device carries every collection, so its longest output report is not the length of the
    // report being sent and the device would ignore a padded one.
    internal static int OutputReportLength(ReportDescriptor? descriptor, byte reportId, int longest) =>
        OperatingSystem.IsWindows() || descriptor is null
            ? longest
            : OutputReportLength(
                descriptor.DeviceItems.SelectMany(i => i.OutputReports).Select(r => (r.ReportID, r.Length)),
                reportId,
                longest
            );

    internal static int OutputReportLength(
        IEnumerable<(byte ReportId, int Length)> reports,
        byte reportId,
        int fallback
    ) => reports.Where(r => r.ReportId == reportId).Select(r => r.Length).FirstOrDefault(fallback);

    internal static int? ParseInterfaceNumber(string devicePath)
    {
        var linux = LinuxUsbInterfacePattern().Match(devicePath);
        if (linux.Success)
        {
            return int.Parse(linux.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        var match = WindowsInterfacePattern().Match(devicePath);
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

    internal static HidCandidate Describe(MacHidEntry entry)
    {
        var descriptor = ParseDescriptor(entry.ReportDescriptor);
        var usages = descriptor is null ? null : TopLevelUsages(descriptor);

        return new HidCandidate(
            entry.Path,
            entry.VendorId,
            entry.ProductId,
            entry.InterfaceNumber,
            string.IsNullOrWhiteSpace(entry.Product) ? "HID device" : entry.Product.Trim(),
            descriptor?.MaxFeatureReportLength ?? 0,
            string.IsNullOrWhiteSpace(entry.SerialNumber) ? null : entry.SerialNumber.Trim(),
            descriptor?.MaxInputReportLength ?? 0,
            descriptor?.MaxOutputReportLength ?? 0,
            usages is { Count: > 0 } ? usages[0].Page : null,
            usages is { Count: > 0 } ? usages[0].Usage : null,
            usages,
            entry.LocationId
        );
    }

    private static ReportDescriptor? ParseDescriptor(byte[]? descriptor)
    {
        if (descriptor is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return new ReportDescriptor(descriptor);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
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
            return TopLevelUsages(device.GetReportDescriptor());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static IReadOnlyList<(int Page, int Usage)> TopLevelUsages(ReportDescriptor descriptor) =>
        [
            .. descriptor
                .DeviceItems.SelectMany(i => i.Usages.GetAllValues())
                .Select(value => ((int)(value >> 16), (int)(value & 0xFFFF))),
        ];

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

// One opened interface for the output/input report exchange.
internal interface IReportStream : IDisposable
{
    int OutputReportLength(byte reportId);

    void Write(byte[] report);

    // Null when no report arrived within the timeout.
    byte[]? Read(TimeSpan timeout);
}

internal sealed class HidSharpReportStream(HidDevice device) : IReportStream
{
    private readonly HidStream _stream = device.Open();

    public int OutputReportLength(byte reportId)
    {
        var longest = device.GetMaxOutputReportLength();
        try
        {
            return HidSharpTransport.OutputReportLength(device.GetReportDescriptor(), reportId, longest);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return longest;
        }
    }

    public void Write(byte[] report) => _stream.Write(report);

    public byte[]? Read(TimeSpan timeout)
    {
        _stream.ReadTimeout = (int)timeout.TotalMilliseconds;
        var input = new byte[device.GetMaxInputReportLength()];
        try
        {
            _stream.Read(input);
            return input;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}

[System.Runtime.Versioning.SupportedOSPlatform("macos")]
internal sealed class MacReportStream : IReportStream
{
    private readonly MacHidDevice _device;
    private readonly ReportDescriptor? _descriptor;

    public MacReportStream(MacHidDevice device, ReportDescriptor? descriptor)
    {
        _device = device;
        _descriptor = descriptor;
        var usesReportIds = descriptor?.Reports.Any(r => r.ReportID != 0) ?? true;
        device.ListenForInputReports(Math.Max(descriptor?.MaxInputReportLength ?? 0, 64), !usesReportIds);
    }

    public int OutputReportLength(byte reportId) =>
        HidSharpTransport.OutputReportLength(_descriptor, reportId, _descriptor?.MaxOutputReportLength ?? 0);

    public void Write(byte[] report) => _device.SetReport(MacHidReportType.Output, report);

    public byte[]? Read(TimeSpan timeout) => _device.ReadInputReport(timeout);

    public void Dispose() => _device.Dispose();
}
