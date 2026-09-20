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
    int? Usage = null
);

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

    // Retries until isComplete accepts the response, then throws.
    Task<byte[]> ExchangeAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        CancellationToken cancellationToken
    );

    // Reads incoming reports until isComplete accepts one. The device may send unrelated reports first.
    Task<byte[]> ExchangeReportsAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        CancellationToken cancellationToken
    );
}

internal sealed partial class HidSharpTransport(ILogger logger) : IHidTransport
{
    // Keep the SetFeature/GetFeature window tight so a foreign poller's answer cannot land in it.
    private static readonly TimeSpan FirstSettleDelay = TimeSpan.FromMilliseconds(3);
    private static readonly TimeSpan MaxSettleDelay = TimeSpan.FromMilliseconds(50);

    // Jittered, so retries do not stay in lockstep with a placeholder frame or a foreign poller.
    private static readonly TimeSpan QueryBudget = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMilliseconds(120);

    private readonly ILogger _logger = logger.ForContext<HidSharpTransport>();

    [GeneratedRegex(@"mi_(?<n>[0-9a-fA-F]{1,2})", RegexOptions.IgnoreCase)]
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

    public async Task<byte[]> ExchangeAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        CancellationToken cancellationToken
    )
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HID feature reports are Windows-only in v1.");
        }

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

        using var handle = NativeHid.Open(devicePath);

        var clock = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeHid.SetFeature(handle, buffer);
            await Task.Delay(SettleDelay(attempt), cancellationToken);

            var response = new byte[reportLength];
            NativeHid.GetFeature(handle, response);
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

            if (clock.Elapsed >= QueryBudget)
            {
                throw new InvalidOperationException(
                    $"HID feature-report exchange on {devicePath} did not complete within "
                        + $"{QueryBudget.TotalMilliseconds} ms ({attempt} attempts). Vendor software polling the "
                        + "same device can keep answering in its place."
                );
            }

            await Task.Delay(NextRetryDelay(), cancellationToken);
        }
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
    private static readonly TimeSpan ReportExchangeBudget = TimeSpan.FromMilliseconds(1500);

    public async Task<byte[]> ExchangeReportsAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        CancellationToken cancellationToken
    )
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HID reports are Windows-only in v1.");
        }

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
                stream.ReadTimeout = ReportReadTimeoutMs;

                var output = new byte[Math.Max(device.GetMaxOutputReportLength(), request.Length)];
                request.CopyTo(output, 0);
                stream.Write(output);

                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < ReportExchangeBudget)
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
                    $"HID report exchange on {devicePath} was not answered within {ReportExchangeBudget.TotalMilliseconds} ms."
                );
            },
            cancellationToken
        );
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

        var match = InterfacePattern().Match(device.DevicePath);
        int? interfaceNumber = match.Success
            ? int.Parse(match.Groups["n"].Value, System.Globalization.NumberStyles.HexNumber, null)
            : null;

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
            withUsage ? TopLevelUsage(device)?.Page : null,
            withUsage ? TopLevelUsage(device)?.Usage : null
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

    private static (int Page, int Usage)? TopLevelUsage(HidDevice device)
    {
        try
        {
            var usage = device
                .GetReportDescriptor()
                .DeviceItems.SelectMany(i => i.Usages.GetAllValues())
                .Cast<uint?>()
                .FirstOrDefault();
            return usage is { } value ? ((int)(value >> 16), (int)(value & 0xFFFF)) : null;
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
