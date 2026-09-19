using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;

namespace DeviceBatteryInfo.Sources.Razer;

/// <summary>Razer's HID power-class report. Verified on the DeathAdder V3 Pro only: add another mouse
/// after reading its battery on the real device.</summary>
internal sealed class RazerProtocol() : HidProtocol("Razer", vendorId: 0x1532, RequestLength)
{
    private const int RequestLength = 90;

    public const byte CommandBatteryLevel = 0x80;
    public const byte CommandChargingStatus = 0x84;

    private const byte TransactionId = 0x1F;
    private const byte CommandClassPower = 0x07;
    private const byte StatusSuccessful = 0x02;

    // Indexes into the raw buffer, where byte 0 is the report id.
    private const int StatusIndex = 1;
    private const int CommandClassIndex = 7;
    private const int CommandIdIndex = 8;
    private const int ValueIndex = 10;

    public override IReadOnlyList<HidDeviceInfo> Devices { get; } =
        [
            new("DeathAdder V3 Pro", 0x00B7, 0x00B6), 
            new("Basilisk V3 Pro", 0x00AB, 0x00AA)
        ];

    public override async Task<BatteryReading> ReadAsync(
        HidChannel channel,
        HidDeviceInfo device,
        CancellationToken cancellationToken
    )
    {
        var percent = PercentFromRaw(
            await QueryAsync(channel, CommandBatteryLevel, cancellationToken)
        );
        var charging = await QueryAsync(channel, CommandChargingStatus, cancellationToken) == 1;

        return new BatteryReading
        {
            Percent = percent,
            Status =
                charging ? BatteryStatus.Charging
                : percent >= 100 ? BatteryStatus.Full
                : BatteryStatus.Discharging,
        };
    }

    internal static async Task<byte> QueryAsync(
        HidChannel channel,
        byte commandId,
        CancellationToken cancellationToken
    )
    {
        var response = await channel.ExchangeAsync(
            BuildRequest(commandId),
            response => IsCompletedResponse(response, commandId),
            cancellationToken
        );
        return response[ValueIndex];
    }

    internal static byte[] BuildRequest(byte commandId)
    {
        var report = new byte[RequestLength];
        report[1] = TransactionId;
        report[5] = 0x02;
        report[6] = CommandClassPower;
        report[7] = commandId;

        byte checksum = 0;
        for (var i = 3; i < 88; i++)
        {
            checksum ^= report[i];
        }

        report[88] = checksum;
        return report;
    }

    // A dongle still talking to the mouse answers with a zeroed placeholder frame, which would read as
    // a spurious 0%, so a response only counts once it echoes success and the command.
    internal static bool IsCompletedResponse(ReadOnlySpan<byte> report, byte commandId) =>
        report.Length > ValueIndex
        && report[StatusIndex] == StatusSuccessful
        && report[CommandClassIndex] == CommandClassPower
        && report[CommandIdIndex] == commandId;

    internal static int PercentFromRaw(byte raw) =>
        (int)Math.Round(raw / 255.0 * 100.0, MidpointRounding.AwayFromZero);
}
