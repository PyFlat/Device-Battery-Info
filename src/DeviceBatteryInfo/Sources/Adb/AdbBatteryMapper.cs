using DeviceBatteryInfo.Core;
using MacroDeck.Sdk.Android;

namespace DeviceBatteryInfo.Sources.Adb;

internal static class AdbBatteryMapper
{
    public static BatteryReading ToReading(AndroidBatteryState battery) =>
        new()
        {
            Percent = Math.Clamp(battery.Level, 0, 100),
            Status = battery.Status switch
            {
                AndroidBatteryStatus.Charging => BatteryStatus.Charging,
                AndroidBatteryStatus.Discharging or AndroidBatteryStatus.NotCharging =>
                    BatteryStatus.Discharging,
                AndroidBatteryStatus.Full => BatteryStatus.Full,
                _ when battery.IsCharging => BatteryStatus.Charging,
                _ => BatteryStatus.Unknown,
            },
        };
}
