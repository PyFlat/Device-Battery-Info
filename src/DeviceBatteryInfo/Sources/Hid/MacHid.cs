using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DeviceBatteryInfo.Sources.Hid;

internal sealed record MacHidEntry(
    string Path,
    int VendorId,
    int ProductId,
    string? Product,
    string? SerialNumber,
    int? InterfaceNumber,
    long? LocationId,
    byte[]? ReportDescriptor
);

internal enum MacHidReportType
{
    Output = 1,
    Feature = 2,
}

// IOKit directly: HidSharp's IORegistry paths are capped at 511 bytes, a deep hub cascade exceeds that and the
// device silently drops out. The registry entry id has no such limit.
internal static class MacHid
{
    internal const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    internal const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const string PathPrefix = "IORegistryEntryID:";
    private const uint MainPort = 0;
    private const uint IterateRecursively = 1;
    private const uint IterateParents = 2;
    private const nint NumberSInt64Type = 4;
    internal const uint Utf8Encoding = 0x08000100;

    internal static string PathOf(ulong entryId) =>
        PathPrefix + entryId.ToString("X", CultureInfo.InvariantCulture);

    internal static bool TryParseEntryId(string path, out ulong entryId)
    {
        entryId = 0;
        return path.StartsWith(PathPrefix, StringComparison.Ordinal)
            && ulong.TryParse(
                path.AsSpan(PathPrefix.Length),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out entryId
            );
    }

    [SupportedOSPlatform("macos")]
    internal static IReadOnlyList<MacHidEntry> Enumerate(int? vendorId = null, int? productId = null)
    {
        if (IOServiceGetMatchingServices(MainPort, IOServiceMatching(CString("IOHIDDevice")), out var iterator) != 0)
        {
            return [];
        }

        var entries = new List<MacHidEntry>();
        try
        {
            uint service;
            while ((service = IOIteratorNext(iterator)) != 0)
            {
                try
                {
                    if (Describe(service, vendorId, productId) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
                finally
                {
                    _ = IOObjectRelease(service);
                }
            }
        }
        finally
        {
            _ = IOObjectRelease(iterator);
        }

        return entries;
    }

    [SupportedOSPlatform("macos")]
    internal static byte[]? ReportDescriptor(string path)
    {
        var service = MatchingService(path);
        if (service == 0)
        {
            return null;
        }

        try
        {
            return DataProperty(service, "ReportDescriptor");
        }
        finally
        {
            _ = IOObjectRelease(service);
        }
    }

    [SupportedOSPlatform("macos")]
    internal static MacHidDevice Open(string path)
    {
        var service = MatchingService(path);
        if (service == 0)
        {
            throw new InvalidOperationException($"HID device {path} is not present.");
        }

        nint device;
        byte[]? descriptor;
        try
        {
            device = IOHIDDeviceCreate(0, service);
            descriptor = DataProperty(service, "ReportDescriptor");
        }
        finally
        {
            _ = IOObjectRelease(service);
        }

        if (device == 0)
        {
            throw new InvalidOperationException($"Unable to create HID device {path}.");
        }

        var result = IOHIDDeviceOpen(device, 0);
        if (result != 0)
        {
            CFRelease(device);
            throw new IOException($"Unable to open HID device {path} (IOReturn 0x{result:X8}).");
        }

        return new MacHidDevice(device, descriptor);
    }

    private static uint MatchingService(string path) =>
        TryParseEntryId(path, out var entryId)
            ? IOServiceGetMatchingService(MainPort, IORegistryEntryIDMatching(entryId))
            : 0;

    private static MacHidEntry? Describe(uint service, int? wantedVendorId, int? wantedProductId)
    {
        // HidSharp skipped devices without a USB id too (the built-in Apple ones).
        if (
            IORegistryEntryGetRegistryEntryID(service, out var entryId) != 0
            || NumberProperty(service, "VendorID") is not { } vendorId
            || NumberProperty(service, "ProductID") is not { } productId
            || (wantedVendorId is not null && vendorId != wantedVendorId)
            || (wantedProductId is not null && productId != wantedProductId)
        )
        {
            return null;
        }

        return new MacHidEntry(
            PathOf(entryId),
            (int)vendorId,
            (int)productId,
            StringProperty(service, "Product"),
            StringProperty(service, "SerialNumber"),
            (int?)ReadNumber(SearchParents(service, "bInterfaceNumber")),
            NumberProperty(service, "LocationID"),
            DataProperty(service, "ReportDescriptor")
        );
    }

    private static nint Property(uint service, string key)
    {
        var name = CreateString(key);
        try
        {
            return IORegistryEntryCreateCFProperty(service, name, 0, 0);
        }
        finally
        {
            CFRelease(name);
        }
    }

    // The interface number lives on the USB interface node above the HID device.
    private static nint SearchParents(uint service, string key)
    {
        var name = CreateString(key);
        try
        {
            return IORegistryEntrySearchCFProperty(
                service,
                CString("IOService"),
                name,
                0,
                IterateRecursively | IterateParents
            );
        }
        finally
        {
            CFRelease(name);
        }
    }

    private static long? NumberProperty(uint service, string key) => ReadNumber(Property(service, key));

    private static long? ReadNumber(nint value)
    {
        if (value == 0)
        {
            return null;
        }

        try
        {
            return CFGetTypeID(value) == CFNumberGetTypeID()
                && CFNumberGetValue(value, NumberSInt64Type, out var number)
                ? number
                : null;
        }
        finally
        {
            CFRelease(value);
        }
    }

    private static string? StringProperty(uint service, string key)
    {
        var value = Property(service, key);
        if (value == 0)
        {
            return null;
        }

        try
        {
            if (CFGetTypeID(value) != CFStringGetTypeID())
            {
                return null;
            }

            var buffer = new byte[CFStringGetMaximumSizeForEncoding(CFStringGetLength(value), Utf8Encoding) + 1];
            if (!CFStringGetCString(value, buffer, buffer.Length, Utf8Encoding))
            {
                return null;
            }

            var end = Array.IndexOf(buffer, (byte)0);
            return Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
        }
        finally
        {
            CFRelease(value);
        }
    }

    private static byte[]? DataProperty(uint service, string key)
    {
        var value = Property(service, key);
        if (value == 0)
        {
            return null;
        }

        try
        {
            if (CFGetTypeID(value) != CFDataGetTypeID())
            {
                return null;
            }

            var data = new byte[CFDataGetLength(value)];
            Marshal.Copy(CFDataGetBytePtr(value), data, 0, data.Length);
            return data;
        }
        finally
        {
            CFRelease(value);
        }
    }

    internal static nint CreateString(string value) =>
        CFStringCreateWithCString(0, CString(value), Utf8Encoding);

    private static byte[] CString(string value) => Encoding.UTF8.GetBytes(value + '\0');

    // Classic DllImport, matching NativeHid: the source-generated marshaller wants AllowUnsafeBlocks.
    [DllImport(IOKit)]
    private static extern nint IOServiceMatching(byte[] name);

    [DllImport(IOKit)]
    private static extern int IOServiceGetMatchingServices(uint mainPort, nint matching, out uint iterator);

    [DllImport(IOKit)]
    private static extern uint IOServiceGetMatchingService(uint mainPort, nint matching);

    [DllImport(IOKit)]
    private static extern uint IOIteratorNext(uint iterator);

    [DllImport(IOKit)]
    private static extern int IOObjectRelease(uint value);

    [DllImport(IOKit)]
    private static extern int IORegistryEntryGetRegistryEntryID(uint entry, out ulong entryId);

    [DllImport(IOKit)]
    private static extern nint IORegistryEntryIDMatching(ulong entryId);

    [DllImport(IOKit)]
    private static extern nint IORegistryEntryCreateCFProperty(
        uint entry,
        nint key,
        nint allocator,
        uint options
    );

    [DllImport(IOKit)]
    private static extern nint IORegistryEntrySearchCFProperty(
        uint entry,
        byte[] plane,
        nint key,
        nint allocator,
        uint options
    );

    [DllImport(IOKit)]
    private static extern nint IOHIDDeviceCreate(nint allocator, uint service);

    [DllImport(IOKit)]
    private static extern int IOHIDDeviceOpen(nint device, uint options);

    [DllImport(CoreFoundation)]
    internal static extern void CFRelease(nint value);

    [DllImport(CoreFoundation)]
    private static extern nuint CFGetTypeID(nint value);

    [DllImport(CoreFoundation)]
    private static extern nuint CFNumberGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFStringGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFDataGetTypeID();

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFNumberGetValue(nint number, nint type, out long value);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringCreateWithCString(
        nint allocator,
        byte[] value,
        uint encoding
    );

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(nint value);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFStringGetCString(nint value, byte[] buffer, nint size, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetLength(nint value);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetBytePtr(nint value);
}

// An opened IOHIDDevice. Report buffers hold the report id at index 0, as on every other platform.
[SupportedOSPlatform("macos")]
internal sealed class MacHidDevice : IDisposable
{
    private delegate void InputReportCallback(
        nint context,
        int result,
        nint sender,
        int type,
        uint reportId,
        nint report,
        nint reportLength
    );

    // Static so the delegate outlives every registration. A private mode keeps the run loop to this device.
    private static readonly InputReportCallback OnInputReport = HandleInputReport;
    private static readonly nint RunLoopMode = MacHid.CreateString("DeviceBatteryInfo.HidInputReports");
    private const int RunLoopFinished = 1;
    private const int RunLoopStopped = 2;

    private readonly nint _device;
    private readonly Queue<byte[]> _inputReports = new();
    private GCHandle _self;
    private nint _inputBuffer;
    private nint _runLoop;
    private bool _prependReportId;

    internal MacHidDevice(nint device, byte[]? reportDescriptor)
    {
        _device = device;
        ReportDescriptor = reportDescriptor;
    }

    public byte[]? ReportDescriptor { get; }

    // Like hidapi, an unnumbered report (id 0) goes out without its id byte.
    public void SetReport(MacHidReportType type, byte[] report)
    {
        var data = report[0] == 0 ? report[1..] : report;
        Check(IOHIDDeviceSetReport(_device, (int)type, report[0], data, data.Length), "IOHIDDeviceSetReport");
    }

    // A numbered report comes back with its id in front, an unnumbered one is read in after the id byte.
    public void GetFeature(byte[] report)
    {
        if (report[0] != 0)
        {
            nint length = report.Length;
            Check(
                IOHIDDeviceGetReport(_device, (int)MacHidReportType.Feature, report[0], report, ref length),
                "IOHIDDeviceGetReport"
            );
            return;
        }

        var data = new byte[report.Length - 1];
        nint read = data.Length;
        Check(IOHIDDeviceGetReport(_device, (int)MacHidReportType.Feature, 0, data, ref read), "IOHIDDeviceGetReport");
        data.AsSpan(0, (int)read).CopyTo(report.AsSpan(1));
    }

    // Input reports are delivered on the run loop of the thread that calls this, so read them on that thread.
    public void ListenForInputReports(int maxReportLength, bool prependReportId)
    {
        _prependReportId = prependReportId;
        _inputBuffer = Marshal.AllocHGlobal(maxReportLength);
        _self = GCHandle.Alloc(this);
        _runLoop = CFRunLoopGetCurrent();
        IOHIDDeviceRegisterInputReportCallback(
            _device,
            _inputBuffer,
            maxReportLength,
            OnInputReport,
            GCHandle.ToIntPtr(_self)
        );
        IOHIDDeviceScheduleWithRunLoop(_device, _runLoop, RunLoopMode);
    }

    public byte[]? ReadInputReport(TimeSpan timeout)
    {
        if (_inputReports.Count == 0)
        {
            var result = CFRunLoopRunInMode(RunLoopMode, timeout.TotalSeconds, returnAfterSourceHandled: true);
            if (result is RunLoopFinished or RunLoopStopped)
            {
                throw new IOException($"The HID input report run loop ended (CFRunLoopRunResult {result}).");
            }
        }

        return _inputReports.TryDequeue(out var report) ? report : null;
    }

    public void Dispose()
    {
        if (_self.IsAllocated)
        {
            IOHIDDeviceRegisterInputReportCallback(_device, _inputBuffer, 0, null, 0);
            IOHIDDeviceUnscheduleFromRunLoop(_device, _runLoop, RunLoopMode);
        }

        _ = IOHIDDeviceClose(_device, 0);
        MacHid.CFRelease(_device);

        if (_self.IsAllocated)
        {
            _self.Free();
        }

        if (_inputBuffer != 0)
        {
            Marshal.FreeHGlobal(_inputBuffer);
        }
    }

    // Windows and HidSharp put a zero report id in front of an unnumbered report, so the protocols expect it.
    private static void HandleInputReport(
        nint context,
        int result,
        nint sender,
        int type,
        uint reportId,
        nint report,
        nint reportLength
    )
    {
        if (result != 0 || reportLength <= 0 || GCHandle.FromIntPtr(context).Target is not MacHidDevice device)
        {
            return;
        }

        var offset = device._prependReportId ? 1 : 0;
        var copy = new byte[reportLength + offset];
        Marshal.Copy(report, copy, offset, (int)reportLength);
        device._inputReports.Enqueue(copy);
    }

    private static void Check(int result, string call)
    {
        if (result != 0)
        {
            throw new IOException($"{call} failed (IOReturn 0x{result:X8}).");
        }
    }

    [DllImport(MacHid.IOKit)]
    private static extern int IOHIDDeviceClose(nint device, uint options);

    [DllImport(MacHid.IOKit)]
    private static extern int IOHIDDeviceSetReport(
        nint device,
        int type,
        nint reportId,
        byte[] report,
        nint reportLength
    );

    [DllImport(MacHid.IOKit)]
    private static extern int IOHIDDeviceGetReport(
        nint device,
        int type,
        nint reportId,
        byte[] report,
        ref nint reportLength
    );

    [DllImport(MacHid.IOKit)]
    private static extern void IOHIDDeviceRegisterInputReportCallback(
        nint device,
        nint report,
        nint reportLength,
        InputReportCallback? callback,
        nint context
    );

    [DllImport(MacHid.IOKit)]
    private static extern void IOHIDDeviceScheduleWithRunLoop(nint device, nint runLoop, nint mode);

    [DllImport(MacHid.IOKit)]
    private static extern void IOHIDDeviceUnscheduleFromRunLoop(nint device, nint runLoop, nint mode);

    [DllImport(MacHid.CoreFoundation)]
    private static extern nint CFRunLoopGetCurrent();

    [DllImport(MacHid.CoreFoundation)]
    private static extern int CFRunLoopRunInMode(
        nint mode,
        double seconds,
        [MarshalAs(UnmanagedType.U1)] bool returnAfterSourceHandled
    );
}
