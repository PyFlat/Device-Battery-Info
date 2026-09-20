# Adding a device

Everything under "Other devices" is a **model** that belongs to a **family** (a way of talking to a
device). Pick the case that fits. None of them touch the config flow, the enums or the translations.

| You want to add | You write |
| --- | --- |
| Another Razer mouse | one line |
| A brand that talks USB HID feature reports (Logitech, SteelSeries, ...) | one file, one class |
| Anything else (Bluetooth LE, a vendor API, ...) | one file, one class |

Only add a device after you have read its battery on the real hardware.

## Another mouse from a known brand

Add one line to `Devices` in `Sources/Razer/RazerProtocol.cs`: the name and **every** USB product id the
mouse shows up as.

```csharp
new("Some Wireless Mouse", 0x00AB, 0x00AA),   // dongle, cable
```

A wireless mouse has one product id for its dongle and another when it is plugged in with the cable
(Device Manager, hardware ids: `VID_1532&PID_00AB`). Leave the cable one out and the mouse disappears as
soon as it is wired. Run the hardware tests once in each mode to find them and to check that both answer:

```
dotnet test --filter Category=Hardware
```

If the new mouse needs a different request, branch on `channel.ProductId` inside `ReadAsync`.

## A new HID brand

Create `Sources/<Brand>/<Brand>Protocol.cs`. The class is found automatically. You describe the brand
and turn one exchange into a reading. The plugin handles finding the device, picking the right HID
interface and telling two identical units apart.

```csharp
internal sealed class LogitechProtocol() : HidProtocol("Logitech", vendorId: 0x046D, reportLength: 20)
{
    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
    [
        new("G Pro X Superlight 2", 0xC09B),
    ];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel, HidDeviceInfo device, CancellationToken cancellationToken)
    {
        var response = await channel.ExchangeAsync(
            request: BuildBatteryRequest(),
            isComplete: r => r.Length > 4 && r[1] == 0x10,
            cancellationToken);

        return BatteryReading.FromPercent(response[4]);
    }
}
```

`Sources/Logitech/LogitechProtocol.cs` is the real, hardware-verified version of this (HID++ 2.0). It
sits on `LogitechHidppProtocol`, which holds the framing every Logitech device shares, so a second
Logitech device is a feature id, a function and a parser. What each part means:

- `vendorId` is the brand's USB vendor id. `reportLength` is the smallest report the right interface
  supports.
- Most brands (Razer) exchange **feature reports**, which is the default. Some (Logitech HID++) write an
  output report and read input reports on a vendor interface instead. For those pass
  `HidReportKind.InputOutput` and the interface's `usagePage`/`usage` to the base constructor, as
  `LogitechProtocol` does. The interface listing below shows which one your device has. That device may
  also send unrelated reports, so `isComplete` must match the answer to your request.
- `ExchangeAsync` sends your bytes and returns the first response `isComplete` accepts. If none does it
  throws, which is what you want: the plugin keeps the last good value instead of showing garbage.
  Devices sometimes answer with an empty placeholder frame first, so check that the frame really is the
  answer to your request (Razer checks a status byte and the echoed command).
- `ReadAsync` must throw when the device does not answer. The plugin uses the same call to find out
  which HID interface is the right one.
- For charging, return `new BatteryReading { Percent = .., Status = BatteryStatus.Charging }`.

To see what your device exposes, run `HardwareTests` (`dotnet test --filter Category=Hardware` from
`tests/DeviceBatteryInfo.Tests`). It lists every HID interface of the supported brands with its report
sizes and usage page, then reads each supported device the way the plugin does. Add your brand's protocol
to its `Protocols` list.

`Sources/Razer/RazerProtocol.cs` is a complete real example. Keep the byte layout in small `static`
methods (`BuildRequest`, `IsCompletedResponse`) so a test can check them without a device.

## Something that is not USB HID

Derive from `SimpleDeviceFamily` (`Sources/DeviceFamily.cs`), list the models, implement `ReadAsync`:

```csharp
internal sealed class AcmeFamily : SimpleDeviceFamily
{
    public override IReadOnlyList<DeviceModel> Models { get; } =
    [
        new("Acme", "Wireless Mouse 3", BatterySourceKind.Mouse),
    ];

    protected override ValueTask<BatteryReading> ReadAsync(
        DeviceModel model, BatterySlot slot, CancellationToken cancellationToken) { ... }
}
```

Throw from `ReadAsync` when the read failed (the value is kept and shown as stale). Override
`IsPresentAsync` if the device can be unplugged: an absent device is hidden until it returns.
Constructor dependencies such as `ILogger` are injected.

Not supported yet: a device that needs input from the user beyond picking its model (an address, a
pairing name). Those are the three generic backends in the config flow.

## Tests

`HidProtocolTests.cs` is the shortest possible protocol test, `HidFamilyTests.cs` shows a fake transport,
and `BatterySourceParsingTests.cs` tests the Razer byte layout. Then run `dotnet build && dotnet test`.
