using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DeviceBatteryInfo.Sources.Hid;

// The hidraw feature-report ioctls directly. HidSharp's SetFeature/GetFeature on Linux return a zeroed
// buffer without an error (measured on a DeathAdder V3 Pro dongle), while the same ioctls answer correctly.
internal static class LinuxHidraw
{
    private const uint IocWrite = 1;
    private const uint IocRead = 2;
    private const uint HidrawType = 'H';
    private const uint SetFeatureNumber = 0x06;
    private const uint GetFeatureNumber = 0x07;
    private const uint GetInputNumber = 0x0A;
    private const int MaxReportLength = 0x3FFF;

    // Classic DllImport, matching NativeHid. ioctl is variadic, which x64 and arm64 Linux pass like fixed args.
    [SupportedOSPlatform("linux")]
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int Ioctl(SafeFileHandle fd, nuint request, byte[] buffer);

    // HidSharp's DevicePath on Linux is the sysfs path ending in the hidraw node's name.
    internal static string DeviceNode(string devicePath)
    {
        var name = Path.GetFileName(devicePath);
        return name.StartsWith("hidraw", StringComparison.Ordinal)
            ? "/dev/" + name
            : throw new InvalidOperationException($"{devicePath} is not a hidraw device.");
    }

    [SupportedOSPlatform("linux")]
    internal static SafeFileHandle Open(string devicePath) =>
        File.OpenHandle(DeviceNode(devicePath), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

    [SupportedOSPlatform("linux")]
    internal static SafeFileHandle? TryOpen(string devicePath)
    {
        try
        {
            return Open(devicePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("linux")]
    internal static void SetFeature(SafeFileHandle handle, byte[] report) =>
        Call(handle, SetFeatureNumber, report, "HIDIOCSFEATURE");

    [SupportedOSPlatform("linux")]
    internal static void GetFeature(SafeFileHandle handle, byte[] report) =>
        Call(handle, GetFeatureNumber, report, "HIDIOCGFEATURE");

    [SupportedOSPlatform("linux")]
    internal static void GetInput(SafeFileHandle handle, byte[] report) =>
        Call(handle, GetInputNumber, report, "HIDIOCGINPUT");

    [SupportedOSPlatform("linux")]
    internal static bool TryGetInputReport(SafeFileHandle handle, byte[] report)
    {
        try
        {
            GetInput(handle, report);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // The size field has 14 bits; a longer length would spill into the direction bits and name another ioctl.
    internal static nuint Request(uint number, int length) =>
        length is > 0 and <= MaxReportLength
            ? ((IocRead | IocWrite) << 30) | ((uint)length << 16) | (HidrawType << 8) | number
            : throw new ArgumentOutOfRangeException(nameof(length), length, "Not a valid hidraw report length.");

    [SupportedOSPlatform("linux")]
    private static void Call(SafeFileHandle handle, uint number, byte[] report, string name)
    {
        if (Ioctl(handle, Request(number, report.Length), report) < 0)
        {
            throw new IOException($"{name} failed (errno {Marshal.GetLastPInvokeError()}).");
        }
    }
}
