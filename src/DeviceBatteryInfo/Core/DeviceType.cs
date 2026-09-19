namespace DeviceBatteryInfo.Core;

/// <summary>Which built-in backend reads a configured device</summary>
public enum DeviceType
{
    System,
    AdbPhone,
    Bluetooth,

    /// <summary>A model from a device family; the slot's <c>CatalogDeviceId</c> says which one.</summary>
    Catalog,
}
