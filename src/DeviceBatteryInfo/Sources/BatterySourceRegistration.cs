using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Adb;
using DeviceBatteryInfo.Sources.Bluetooth;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.SystemBattery;
using Microsoft.Extensions.DependencyInjection;

namespace DeviceBatteryInfo.Sources;

internal static class BatterySourceRegistration
{
    public static IServiceCollection AddBatterySources(this IServiceCollection services)
    {
        services.AddSingleton<IHidTransport, HidSharpTransport>();
        services.AddSingleton<IPnpBatteryReader, PowerShellPnpBatteryReader>();
        services.AddSingleton<IDeviceDiscovery, WindowsDeviceDiscovery>();

        services.AddSingleton<IBatterySourceProvider, SystemBatterySourceProvider>();
        services.AddSingleton<IBatterySourceProvider, AdbBatterySourceProvider>();
        services.AddSingleton<IBatterySourceProvider, BluetoothBatterySourceProvider>();

        // Families and HID protocols in this assembly register themselves, so adding one needs no wiring.
        AddAll<IDeviceFamily>(services);
        AddAll<HidProtocol>(services);
        services.AddSingleton<DeviceModelCatalog>();
        services.AddSingleton<IBatterySourceProvider, DeviceFamilyProvider>();

        return services;
    }

    private static void AddAll<T>(IServiceCollection services)
    {
        foreach (
            var type in typeof(T)
                .Assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false } && t.IsAssignableTo(typeof(T)))
        )
        {
            services.AddSingleton(typeof(T), type);
        }
    }
}
