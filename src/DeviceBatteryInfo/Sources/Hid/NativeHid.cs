using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DeviceBatteryInfo.Sources.Hid;

/// <summary>
/// A direct <c>hid.dll</c> feature-report round trip: open the device for read+write, and when the
/// Razer control interface refuses that, reopen with <em>no</em> access at all - a zero-access handle
/// still carries the <c>HidD_SetFeature</c> / <c>HidD_GetFeature</c> IOCTLs. HidSharp only ever
/// requests read+write and throws <c>DeviceIOException</c> when the device declines, so it cannot
/// reach this interface at all; going through <c>hid.dll</c> directly (matching hidapi) can.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeHid
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    // Classic DllImport rather than LibraryImport, matching NativePowerStatusApi: the source-generated
    // marshaller wants AllowUnsafeBlocks and these few calls do not benefit from it.
    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    private static extern SafeFileHandle CreateFile(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile
    );

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(
        SafeFileHandle device,
        byte[] buffer,
        uint bufferLength
    );

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(
        SafeFileHandle device,
        byte[] buffer,
        uint bufferLength
    );

    internal static SafeFileHandle Open(string devicePath)
    {
        var handle = CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            securityAttributes: 0,
            OpenExisting,
            flagsAndAttributes: 0,
            templateFile: 0
        );

        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = CreateFile(
                devicePath,
                desiredAccess: 0,
                FileShareRead | FileShareWrite,
                securityAttributes: 0,
                OpenExisting,
                flagsAndAttributes: 0,
                templateFile: 0
            );
        }

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"Unable to open HID device {devicePath}.");
        }

        return handle;
    }

    internal static void SetFeature(SafeFileHandle device, byte[] report)
    {
        if (!HidD_SetFeature(device, report, (uint)report.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_SetFeature failed.");
        }
    }

    internal static void GetFeature(SafeFileHandle device, byte[] report)
    {
        if (!HidD_GetFeature(device, report, (uint)report.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_GetFeature failed.");
        }
    }
}
