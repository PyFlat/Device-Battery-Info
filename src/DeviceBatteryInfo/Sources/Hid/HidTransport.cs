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
    string? SerialNumber = null
);

// Generic HID plumbing only - no knowledge of any device's report format. A device family
// (Sources/Razer/, ...) owns its own report layout and passes the finished request bytes in.
internal interface IHidTransport
{
    IReadOnlyList<HidCandidate> FindCandidates(
        int vendorId,
        int productId,
        int? interfaceNumber,
        int minFeatureReportLength
    );

    IReadOnlyList<HidCandidate> ListFeatureReportDevices();

    // Retries until isComplete accepts the response or attempts run out; throws otherwise
    Task<byte[]> ExchangeAsync(
        string devicePath,
        byte[] request,
        Func<byte[], bool> isComplete,
        CancellationToken cancellationToken
    );
}

internal sealed partial class HidSharpTransport(ILogger logger) : IHidTransport
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(50);

    // A dongle can occasionally answer a poll with a not-yet-ready placeholder frame while it is still
    // talking to its device. Re-issue the exchange a few times before giving up so one such frame does
    // not surface as a bad reading; the ceiling stays well inside the source read timeout (min 2s).
    private const int MaxQueryAttempts = 4;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(40);

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
            .Select(Describe)
            .Where(c => c.FeatureReportLength >= minFeatureReportLength)
            .Where(c => interfaceNumber is null || c.InterfaceNumber == interfaceNumber)
            // Which HID collection answers battery queries varies by model (the DeathAdder V3 Pro
            // dongle uses its lowest one, mi_00; others use a higher one), so try them in ascending
            // order and let the caller's own probe find which one actually replies.
            .OrderBy(c => c.InterfaceNumber ?? int.MaxValue)
            .ToArray();

    public IReadOnlyList<HidCandidate> ListFeatureReportDevices() =>
        DeviceList
            .Local.GetHidDevices()
            .Select(Describe)
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

        // hidapi prepends a report-id byte, so an N-byte request goes out as N+1 with report id 0 at
        // index 0. Match that: the request lands at reportLength - request.Length (1 when the length
        // carries the id byte, 0 otherwise).
        var offset = Math.Clamp(reportLength - request.Length, 0, 1);
        var buffer = new byte[reportLength];
        request.CopyTo(buffer, offset);

        using var handle = NativeHid.Open(devicePath);

        for (var attempt = 1; attempt <= MaxQueryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeHid.SetFeature(handle, buffer);
            await Task.Delay(SettleDelay, cancellationToken);

            var response = new byte[reportLength];
            NativeHid.GetFeature(handle, response);
            if (isComplete(response))
            {
                return response;
            }

            _logger.Debug(
                "HID feature-report exchange on {Path} not ready, attempt {Attempt}/{Max}: {Response}",
                devicePath,
                attempt,
                MaxQueryAttempts,
                Convert.ToHexString(response)
            );
            await Task.Delay(RetryDelay, cancellationToken);
        }

        throw new InvalidOperationException(
            $"HID feature-report exchange on {devicePath} did not complete after {MaxQueryAttempts} attempts."
        );
    }

    internal static HidCandidate Describe(HidDevice device)
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
            SafeSerial(device)
        );
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
