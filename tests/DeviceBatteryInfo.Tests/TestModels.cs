using DeviceBatteryInfo.ConfigFlow;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.Razer;

namespace DeviceBatteryInfo.Tests;

internal static class TestModels
{
    public static DeviceModelCatalog Catalog() =>
        new(
            [
                new HidFamily(
                    [new RazerProtocol()],
                    new HidSharpTransport(Serilog.Core.Logger.None),
                    Serilog.Core.Logger.None
                ),
            ]
        );
}
