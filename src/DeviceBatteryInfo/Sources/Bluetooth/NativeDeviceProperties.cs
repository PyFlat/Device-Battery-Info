using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DeviceBatteryInfo.Sources.Bluetooth;

internal sealed record PnpNode(string InstanceId, string? Name, string? RawBattery);

// Not Get-PnpDeviceProperty: it took ~0.3 s per node and dropped values at random when batched.
[SupportedOSPlatform("windows")]
internal static class NativeDeviceProperties
{
    private static readonly string[] BluetoothEnumerators = ["BTHENUM", "BTHHFENUM", "BTHLE", "BTHLEDEVICE"];

    // DEVPKEY_Bluetooth_Battery
    private static readonly DevPropKey BatteryKey = new(
        new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
        2
    );

    // DEVPKEY_Device_FriendlyName, else DeviceDesc, as Get-PnpDevice does.
    private static readonly DevPropKey FriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14
    );

    private static readonly DevPropKey DescriptionKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        2
    );

    private const uint CrSuccess = 0;
    private const uint CrBufferSmall = 0x1A;
    private const uint FilterEnumerator = 0x1;
    private const uint FilterPresent = 0x100;

    private const uint PropTypeSByte = 0x02;
    private const uint PropTypeByte = 0x03;
    private const uint PropTypeInt16 = 0x04;
    private const uint PropTypeUInt16 = 0x05;
    private const uint PropTypeInt32 = 0x06;
    private const uint PropTypeUInt32 = 0x07;
    private const uint PropTypeString = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DevPropKey(Guid FormatId, uint PropertyId);

    // Classic DllImport rather than LibraryImport, matching NativeHid.
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", CharSet = CharSet.Unicode)]
    private static extern uint GetDeviceIdListSize(out uint length, string filter, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", CharSet = CharSet.Unicode)]
    private static extern uint GetDeviceIdList(
        string filter,
        [Out] char[] buffer,
        uint length,
        uint flags
    );

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)]
    private static extern uint LocateDevNode(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW", CharSet = CharSet.Unicode)]
    private static extern uint GetDevNodeProperty(
        uint devInst,
        in DevPropKey key,
        out uint type,
        [Out] byte[]? buffer,
        ref uint size,
        uint flags
    );

    public static IReadOnlyList<PnpNode> ListBluetoothNodes()
    {
        var nodes = new List<PnpNode>();
        foreach (var enumerator in BluetoothEnumerators)
        {
            foreach (var instanceId in PresentIds(enumerator))
            {
                if (LocateDevNode(out var devInst, instanceId, 0) != CrSuccess)
                {
                    continue;
                }

                var name = ReadString(devInst, FriendlyNameKey) ?? ReadString(devInst, DescriptionKey);
                nodes.Add(new PnpNode(instanceId, name, ReadNumber(devInst, BatteryKey)));
            }
        }

        return nodes;
    }

    private static string[] PresentIds(string enumerator)
    {
        const uint flags = FilterEnumerator | FilterPresent;
        // The list can grow between the two calls.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (GetDeviceIdListSize(out var length, enumerator, flags) != CrSuccess || length <= 1)
            {
                return [];
            }

            var buffer = new char[length];
            var result = GetDeviceIdList(enumerator, buffer, length, flags);
            if (result == CrBufferSmall)
            {
                continue;
            }

            return result == CrSuccess
                ? new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                : [];
        }

        return [];
    }

    private static (uint Type, byte[] Data)? ReadProperty(uint devInst, DevPropKey key)
    {
        uint size = 0;
        if (GetDevNodeProperty(devInst, key, out _, null, ref size, 0) != CrBufferSmall || size == 0)
        {
            return null;
        }

        var data = new byte[size];
        return GetDevNodeProperty(devInst, key, out var type, data, ref size, 0) == CrSuccess
            ? (type, data)
            : null;
    }

    private static string? ReadString(uint devInst, DevPropKey key) =>
        ReadProperty(devInst, key) is ({ } type, { } data) && type == PropTypeString
            ? Encoding.Unicode.GetString(data).TrimEnd('\0') is { Length: > 0 } text
                ? text
                : null
            : null;

    private static string? ReadNumber(uint devInst, DevPropKey key)
    {
        if (ReadProperty(devInst, key) is not ({ } type, { } data))
        {
            return null;
        }

        long? value = type switch
        {
            PropTypeSByte when data.Length >= 1 => (sbyte)data[0],
            PropTypeByte when data.Length >= 1 => data[0],
            PropTypeInt16 when data.Length >= 2 => BinaryPrimitives.ReadInt16LittleEndian(data),
            PropTypeUInt16 when data.Length >= 2 => BinaryPrimitives.ReadUInt16LittleEndian(data),
            PropTypeInt32 when data.Length >= 4 => BinaryPrimitives.ReadInt32LittleEndian(data),
            PropTypeUInt32 when data.Length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian(data),
            _ => null,
        };
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}
