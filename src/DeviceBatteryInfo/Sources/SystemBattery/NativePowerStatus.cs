using System.Runtime.InteropServices;

namespace DeviceBatteryInfo.Sources.SystemBattery;

[StructLayout(LayoutKind.Sequential)]
internal struct NativePowerStatus
{
    public byte AcLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;

    public const byte AcOffline = 0;
    public const byte AcOnline = 1;
    public const byte AcUnknown = 255;

    public const byte FlagCharging = 0x08;
    public const byte FlagNoBattery = 0x80;
    public const byte FlagUnknown = 0xFF;

    public const byte PercentUnknown = 255;
    public const int TimeUnknown = -1;
}

internal static class NativePowerStatusApi
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out NativePowerStatus lpSystemPowerStatus);
}
