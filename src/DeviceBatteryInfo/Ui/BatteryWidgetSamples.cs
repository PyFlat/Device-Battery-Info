using DeviceBatteryInfo.Core;

namespace DeviceBatteryInfo.Ui;

internal static class BatteryWidgetSamples
{
    public static BatteryWidgetModel For(string localId) =>
        localId == BatteryWidgetTypes.TileId ? TileCharging() : Panel();

    public static BatteryWidgetModel Panel() =>
        new(
            [
                Row("phone", "Phone", 48, BatteryStatus.Discharging, BatterySourceKind.Phone),
                Row(
                    "earbuds",
                    "Earbuds",
                    2,
                    BatteryStatus.Charging,
                    BatterySourceKind.Earbuds,
                    charging: true
                ),
                Row(
                    "mouse",
                    "Mouse",
                    100,
                    BatteryStatus.Charging,
                    BatterySourceKind.Mouse,
                    charging: true
                ),
                Row("headset", "Headset", 20, BatteryStatus.Discharging, BatterySourceKind.Headset),
            ],
            BatteryWidgetOptions.Default
        );

    public static BatteryWidgetModel PanelNamed() =>
        new(
            [
                Row("laptop", "Laptop", 76, BatteryStatus.Discharging, BatterySourceKind.System),
                Row("keyboard", "Keyboard", 58, BatteryStatus.Discharging, BatterySourceKind.Keyboard),
                Row(
                    "controller",
                    "Controller",
                    34,
                    BatteryStatus.Charging,
                    BatterySourceKind.Controller,
                    charging: true
                ),
                Row("tablet", "Tablet", 100, BatteryStatus.Full, BatterySourceKind.Tablet),
            ],
            BatteryWidgetOptions.Default with
            {
                Title = "Batteries",
                ShowNames = true,
            }
        );

    public static BatteryWidgetModel PanelTrend() =>
        new(
            [
                Row(
                    "phone",
                    "Phone",
                    47,
                    BatteryStatus.Discharging,
                    BatterySourceKind.Phone,
                    trend: "-13%/1h"
                ),
                Row(
                    "mouse",
                    "Mouse",
                    82,
                    BatteryStatus.Charging,
                    BatterySourceKind.Mouse,
                    charging: true,
                    trend: "+28%/30m"
                ),
                Row("headset", "Headset", 100, BatteryStatus.Full, BatterySourceKind.Headset),
                Row(
                    "earbuds",
                    "Earbuds",
                    15,
                    BatteryStatus.Discharging,
                    BatterySourceKind.Earbuds,
                    trend: "-9%/30m"
                ),
            ],
            BatteryWidgetOptions.Default with
            {
                ShowNames = true,
                ShowRingTrend = true,
            }
        );

    public static BatteryWidgetModel PanelList() =>
        new(
            [
                Row(
                    "mouse",
                    "Mouse",
                    82,
                    BatteryStatus.Charging,
                    BatterySourceKind.Mouse,
                    charging: true,
                    timeToFull: "0:35"
                ),
                Row(
                    "phone",
                    "Phone",
                    17,
                    BatteryStatus.Discharging,
                    BatterySourceKind.Phone,
                    trend: "-13%/1h"
                ),
                Row(
                    "keyboard",
                    "Keyboard",
                    64,
                    BatteryStatus.Discharging,
                    BatterySourceKind.Keyboard,
                    stale: true
                ),
                Row("headset", "Headset", 100, BatteryStatus.Full, BatterySourceKind.Headset),
            ],
            BatteryWidgetOptions.Default with
            {
                Layout = BatteryWidgetLayout.List,
            }
        );

    public static BatteryWidgetModel PanelListDevice() =>
        new(
            [
                Row("laptop", "Laptop", 76, BatteryStatus.Discharging, BatterySourceKind.System),
                Row(
                    "controller",
                    "Controller",
                    34,
                    BatteryStatus.Charging,
                    BatterySourceKind.Controller,
                    charging: true,
                    timeToFull: "1:10"
                ),
                Row("pen", "Pen", 91, BatteryStatus.Discharging, BatterySourceKind.Pen),
                Row("earbuds", "Earbuds", 8, BatteryStatus.Discharging, BatterySourceKind.Earbuds),
            ],
            BatteryWidgetOptions.Default with
            {
                Title = "Batteries",
                Layout = BatteryWidgetLayout.List,
                Colors = BatteryColorScheme.Device,
                ListAlign = BatteryListAlignment.Center,
            }
        );

    public static BatteryWidgetModel PanelGradient() =>
        new(
            [
                Row("mouse", "Mouse", 92, BatteryStatus.Discharging, BatterySourceKind.Mouse),
                Row("keyboard", "Keyboard", 63, BatteryStatus.Discharging, BatterySourceKind.Keyboard),
                Row("headset", "Headset", 38, BatteryStatus.Discharging, BatterySourceKind.Headset),
                Row("controller", "Controller", 11, BatteryStatus.Discharging, BatterySourceKind.Controller),
            ],
            BatteryWidgetOptions.Default with
            {
                ShowNames = true,
                Colors = BatteryColorScheme.Gradient,
            }
        );

    public static BatteryWidgetModel PanelLow() =>
        new(
            [
                Row("mouse", "Mouse", 12, BatteryStatus.Discharging, BatterySourceKind.Mouse),
                Row("keyboard", "Keyboard", 6, BatteryStatus.Discharging, BatterySourceKind.Keyboard),
                Row("phone", "Phone", 58, BatteryStatus.Discharging, BatterySourceKind.Phone),
                Row("pen", "Pen", 64, BatteryStatus.Discharging, BatterySourceKind.Pen, stale: true),
            ],
            BatteryWidgetOptions.Default
        );

    public static BatteryWidgetModel PanelEmpty() =>
        new([], BatteryWidgetOptions.Default with { Title = "Batteries" });

    public static BatteryWidgetModel TileCharging() =>
        new(
            [
                Row(
                    "mouse",
                    "Mouse",
                    82,
                    BatteryStatus.Charging,
                    BatterySourceKind.Mouse,
                    charging: true,
                    timeToFull: "0:35"
                ),
            ],
            BatteryWidgetOptions.Default
        );

    public static BatteryWidgetModel TileDischarging() =>
        new(
            [
                Row(
                    "phone",
                    "Phone",
                    47,
                    BatteryStatus.Discharging,
                    BatterySourceKind.Phone,
                    trend: "-13%/1h"
                ),
            ],
            BatteryWidgetOptions.Default
        );

    public static BatteryWidgetModel TileLow() =>
        new(
            [Row("headset", "Headset", 9, BatteryStatus.Discharging, BatterySourceKind.Headset)],
            BatteryWidgetOptions.Default
        );

    public static BatteryWidgetModel TileFull() =>
        new(
            [Row("tablet", "Tablet", 100, BatteryStatus.Full, BatterySourceKind.Tablet)],
            BatteryWidgetOptions.Default
        );

    public static BatteryWidgetModel TileNoSignal() =>
        new(
            [
                Row(
                    "controller",
                    "Controller",
                    54,
                    BatteryStatus.Discharging,
                    BatterySourceKind.Controller,
                    stale: true
                ),
            ],
            BatteryWidgetOptions.Default
        );

    public static BatteryWidgetModel TileCustomColors() =>
        TileDischarging() with
        {
            Options = BatteryWidgetOptions.Default with
            {
                BackgroundColor = "#F2F2F7",
                TextColor = "#1C1C1E",
            },
        };

    public static BatteryWidgetModel TileTransparent() =>
        TileCharging() with
        {
            Options = BatteryWidgetOptions.Default with { BackgroundColor = "transparent" },
        };

    public static BatteryWidgetModel PanelListCustomColors() =>
        PanelList() with
        {
            Options = PanelList().Options with
            {
                Title = "Batteries",
                BackgroundColor = "#0A2540",
                TextColor = "#FFD60A",
            },
        };

    public static BatteryWidgetModel TileSmallRing() =>
        TileDischarging() with
        {
            Options = BatteryWidgetOptions.Default with { RingSize = 60 },
        };

    public static BatteryWidgetModel PanelSmallRings() =>
        PanelNamed() with
        {
            Options = PanelNamed().Options with { RingSize = 70 },
        };

    public static BatteryWidgetModel TileNameInRing() =>
        new(
            [
                Row(
                    "controller",
                    "DualSense Controller",
                    64,
                    BatteryStatus.Charging,
                    BatterySourceKind.Controller,
                    charging: true,
                    trend: "+28%/30m"
                ),
            ],
            BatteryWidgetOptions.Default with { NamePosition = BatteryNamePosition.Inside }
        );

    public static BatteryWidgetModel PanelNamesInRings() =>
        PanelNamed() with
        {
            Options = PanelNamed().Options with { NamePosition = BatteryNamePosition.Inside },
        };

    private static BatteryWidgetRow Row(
        string id,
        string name,
        int percent,
        BatteryStatus status,
        BatterySourceKind kind,
        bool charging = false,
        string? timeToFull = null,
        string? trend = null,
        bool stale = false
    ) => new(id, name, percent, status, charging, stale, timeToFull, trend, kind);
}
