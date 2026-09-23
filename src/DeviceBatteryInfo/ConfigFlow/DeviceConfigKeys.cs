using DeviceBatteryInfo.Core;

namespace DeviceBatteryInfo.ConfigFlow;

internal static class DeviceConfigKeys
{
    public const string Name = "name";
    public const string Type = "type";

    public const string Category = "category";

    public const string CatalogBrand = "catalogBrand";
    public const string CatalogDevice = "catalogDevice";

    public const string AdbAddress = "adbAddress";
    public const string AdbAddressCustom = "adbAddressCustom";
    public const string BluetoothName = "bluetoothName";
    public const string BluetoothNameCustom = "bluetoothNameCustom";
    public const string BluetoothKind = "bluetoothKind";

    public const string TypeSystem = "system";
    public const string TypeAdbPhone = "adb-phone";
    public const string TypeBluetooth = "bluetooth";

    public const string TypeCatalog = "catalog";

    public const string CategoryOther = "other";

    public static DeviceType ParseType(string? value) =>
        value switch
        {
            TypeSystem => DeviceType.System,
            TypeBluetooth => DeviceType.Bluetooth,
            TypeCatalog => DeviceType.Catalog,
            _ => DeviceType.AdbPhone,
        };

    public static DeviceType? CategoryToType(string? value) =>
        value switch
        {
            CategoryOther => null,
            TypeSystem => DeviceType.System,
            TypeBluetooth => DeviceType.Bluetooth,
            _ => DeviceType.AdbPhone,
        };

    public static string TypeToCategory(DeviceType type) =>
        type == DeviceType.Catalog ? CategoryOther : TypeValue(type);

    public static string TypeValue(DeviceType type) =>
        type switch
        {
            DeviceType.System => TypeSystem,
            DeviceType.Bluetooth => TypeBluetooth,
            DeviceType.Catalog => TypeCatalog,
            _ => TypeAdbPhone,
        };

    public static BatterySourceKind ParseKind(string? value) =>
        value switch
        {
            "mouse" => BatterySourceKind.Mouse,
            "keyboard" => BatterySourceKind.Keyboard,
            "headset" => BatterySourceKind.Headset,
            "earbuds" => BatterySourceKind.Earbuds,
            "phone" => BatterySourceKind.Phone,
            "tablet" => BatterySourceKind.Tablet,
            "controller" => BatterySourceKind.Controller,
            "pen" => BatterySourceKind.Pen,
            _ => BatterySourceKind.Other,
        };

    public static string KindValue(BatterySourceKind kind) =>
        kind switch
        {
            BatterySourceKind.Mouse => "mouse",
            BatterySourceKind.Keyboard => "keyboard",
            BatterySourceKind.Headset => "headset",
            BatterySourceKind.Earbuds => "earbuds",
            BatterySourceKind.Phone => "phone",
            BatterySourceKind.Tablet => "tablet",
            BatterySourceKind.Controller => "controller",
            BatterySourceKind.Pen => "pen",
            _ => "other",
        };
}
