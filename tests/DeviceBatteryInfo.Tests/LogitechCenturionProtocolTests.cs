using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.Logitech;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class LogitechCenturionProtocolTests
{
    private sealed class FakeReceiver(byte level, byte state, bool headsetOn = true) : IHidTransport
    {
        private static readonly ushort[] Receiver = [0x0000, 0x0001, 0x0100, 0x0003, 0x010A];
        private static readonly ushort[] Headset = [0x0000, 0x0001, 0x0101, 0x0100, 0x0104, 0x010A];

        public List<byte[]> Writes { get; } = [];

        public IReadOnlyList<HidCandidate> FindCandidates(
            int vendorId,
            int productId,
            int? interfaceNumber,
            int minFeatureReportLength
        ) => [];

        public IReadOnlyList<HidCandidate> ListFeatureReportDevices() => [];

        public Task<byte[]> ExchangeAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<byte[]> ExchangeReportsAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        )
        {
            Writes.Add(request);
            var reply = Replies(LogitechCenturionProtocol.Payload(request)).FirstOrDefault(isComplete);
            return reply is null
                ? throw new TimeoutException("Not answered.")
                : Task.FromResult(reply);
        }

        private IEnumerable<byte[]> Replies(byte[] payload)
        {
            yield return Frame(0x07, 0x01, 0x05);

            if (payload[0] == 3 && payload[1] == 0x11)
            {
                var message = payload[4..];
                yield return Frame(3, 0x11);
                if (!headsetOn)
                {
                    yield break;
                }

                var data = Answer(Headset, message[1], (byte)(message[2] & 0xF0), message[3..]);
                yield return data is null
                    ? Frame(3, 0x10, 0, 4, 0x00, 0xFF, message[1])
                    : Frame([3, 0x10, 0, (byte)(3 + data.Length), 0x00, message[1], message[2], .. data]);
            }
            else
            {
                var data = Answer(Receiver, payload[0], (byte)(payload[1] & 0xF0), payload[2..]);
                yield return data is null
                    ? Frame(0xFF, payload[0], payload[1], 0x05)
                    : Frame([payload[0], payload[1], .. data]);
            }
        }

        private byte[]? Answer(ushort[] table, byte index, byte function, byte[] parameters)
        {
            if (index == 0 && function == 0x00)
            {
                var wanted = (ushort)(parameters[0] << 8 | parameters[1]);
                return [(byte)Math.Max(Array.IndexOf(table, wanted), 0)];
            }

            return index < table.Length && table[index] == LogitechCenturionProtocol.BatteryFeature && function == 0x00
                ? [level, 0, state]
                : null;
        }

        private static byte[] Frame(params byte[] payload)
        {
            var report = new byte[64];
            report[0] = 0x51;
            report[1] = (byte)(payload.Length + 1);
            payload.CopyTo(report, 3);
            return report;
        }
    }

    private static Task<BatteryReading> ReadAsync(FakeReceiver receiver) =>
        new LogitechCenturionProtocol().ReadAsync(
            new HidChannel(receiver, "ffa0", 0x0AF7, HidReportKind.InputOutput),
            new LogitechCenturionProtocol().Devices[0],
            CancellationToken.None
        );

    [Test]
    public void The_requests_are_framed_as_solaar_and_headsetcontrol_frame_them()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                LogitechCenturionProtocol.BuildDirectRequest(0, 0x00, 0x00, 0x03),
                Is.EqualTo(new byte[] { 0x51, 0x05, 0x00, 0x00, 0x01, 0x00, 0x03 })
            );
            Assert.That(
                LogitechCenturionProtocol.BuildBridgeRequest(3, 4, 0x00),
                Is.EqualTo(new byte[] { 0x51, 0x08, 0x00, 0x03, 0x11, 0x00, 0x03, 0x00, 0x04, 0x01 })
            );
        }
    }

    [Test]
    public async Task Reads_the_level_through_the_bridge()
    {
        var receiver = new FakeReceiver(level: 80, state: 0);

        var reading = await ReadAsync(receiver);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(80));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
            Assert.That(receiver.Writes, Has.Count.EqualTo(3));
        }
    }

    [TestCase((byte)1, BatteryStatus.Charging)]
    [TestCase((byte)2, BatteryStatus.Charging)]
    [TestCase((byte)3, BatteryStatus.Full)]
    [TestCase((byte)9, BatteryStatus.Unknown)]
    public async Task The_state_byte_gives_the_status(byte state, BatteryStatus status)
    {
        var reading = await ReadAsync(new FakeReceiver(level: 75, state));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reading.Percent, Is.EqualTo(75));
            Assert.That(reading.Status, Is.EqualTo(status));
        }
    }

    [Test]
    public void A_headset_that_is_off_only_gets_the_acknowledgement_and_is_no_reading() =>
        Assert.ThrowsAsync<TimeoutException>(() => ReadAsync(new FakeReceiver(80, 0, headsetOn: false)));

    [Test]
    public void A_level_above_100_is_no_reading() =>
        Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(new FakeReceiver(0xC8, 0)));

    [Test]
    public void A_missing_feature_is_a_different_device() =>
        Assert.Throws<NotSupportedException>(() => LogitechCenturionProtocol.ParseFeatureIndex([0x00]));

    [Test]
    public void Another_function_or_a_notification_is_not_the_answer()
    {
        byte[] Report(params byte[] payload) => [0x51, (byte)(payload.Length + 1), 0x00, .. payload];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(LogitechCenturionProtocol.IsBridgeReply(Report(3, 0x11), 3, 4, 0x00), Is.False);
            Assert.That(
                LogitechCenturionProtocol.IsBridgeReply(Report(3, 0x10, 0, 4, 0xFF, 4, 0x01, 5), 3, 4, 0x00),
                Is.False
            );
            Assert.That(
                LogitechCenturionProtocol.IsBridgeReply(Report(3, 0x10, 0, 4, 0x00, 4, 0x11, 6), 3, 4, 0x00),
                Is.False
            );
            Assert.That(LogitechCenturionProtocol.IsDirectReply(Report(1, 0x01, 9), 1, 0x10), Is.False);
        }
    }
}
