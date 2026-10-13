using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DeviceBatteryInfo.Sources.Hid;

// The report buffers hold the report id at index 0.
internal interface IFeatureChannel : IDisposable
{
    void Set(byte[] report);

    void Get(byte[] report);
}

[SupportedOSPlatform("windows")]
internal sealed class NativeFeatureChannel(SafeFileHandle handle) : IFeatureChannel
{
    public void Set(byte[] report) => NativeHid.SetFeature(handle, report);

    public void Get(byte[] report) => NativeHid.GetFeature(handle, report);

    public void Dispose() => handle.Dispose();
}

[SupportedOSPlatform("linux")]
internal sealed class LinuxFeatureChannel(SafeFileHandle handle) : IFeatureChannel
{
    public void Set(byte[] report) => LinuxHidraw.SetFeature(handle, report);

    public void Get(byte[] report) => LinuxHidraw.GetFeature(handle, report);

    public void Dispose() => handle.Dispose();
}

[SupportedOSPlatform("macos")]
internal sealed class MacFeatureChannel(MacHidDevice device) : IFeatureChannel
{
    public void Set(byte[] report) => device.SetReport(MacHidReportType.Feature, report);

    public void Get(byte[] report) => device.GetFeature(report);

    public void Dispose() => device.Dispose();
}
