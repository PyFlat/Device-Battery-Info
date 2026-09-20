namespace DeviceBatteryInfo.Core;

public enum DeviceType
{
    System,
    AdbPhone,
    Bluetooth,

    // The slot's CatalogDeviceId says which device family model it is.
    Catalog,
}
