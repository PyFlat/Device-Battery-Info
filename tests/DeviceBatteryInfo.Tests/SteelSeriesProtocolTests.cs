using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Hid;
using DeviceBatteryInfo.Sources.SteelSeries;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class SteelSeriesProtocolTests
{
    // Captured from a Rival 3 Wireless over its dongle, before and after new AAA cells, trimmed from 65 bytes.
    private static readonly byte[] OldCells = Convert.FromHexString("0013000000000000");
    private static readonly byte[] NewCells = Convert.FromHexString("003B000000000000");

    // Laid out after HeadsetControl, shifted by the report id: status left/right in bytes 4-5
    // (03 out of the case, 02 in it), level left/right in bytes 6-7.
    private static byte[] GameBuds(byte left, byte right, byte leftLevel, byte rightLevel) =>
        Convert.FromHexString($"00B00000{left:X2}{right:X2}{leftLevel:X2}{rightLevel:X2}0000000000000000");

    private sealed class FakeDongle(byte[] reply) : IHidTransport
    {
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
            return isComplete(reply)
                ? Task.FromResult(reply)
                : throw new TimeoutException("Not answered.");
        }

        public Task<byte[]> ExchangeGetInputReportAsync(
            string devicePath,
            byte[] request,
            Func<byte[], bool> isComplete,
            TimeSpan budget,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private static async Task<(BatteryReading Reading, byte[] Request)> ReadAsync(int productId, byte[] reply)
    {
        var dongle = new FakeDongle(reply);
        var protocol = new SteelSeriesProtocol();
        var device = protocol.Devices.Single(d => d.ProductIds.Contains(productId));
        var reading = await protocol.ReadAsync(
            new HidChannel(dongle, "ffc0", productId, HidReportKind.InputOutput),
            device,
            CancellationToken.None
        );
        return (reading, dongle.Writes.Single());
    }

    [Test]
    public void Reads_level_from_the_answer()
    {
        var reading = SteelSeriesProtocol.ParseBattery(OldCells);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SteelSeriesProtocol.IsBatteryReport(OldCells), Is.True);
            Assert.That(reading.Percent, Is.EqualTo(19));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
            Assert.That(SteelSeriesProtocol.ParseBattery(NewCells).Percent, Is.EqualTo(59));
        }
    }

    [Test]
    public void A_level_above_100_is_no_reading()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SteelSeriesProtocol.ParseBattery(Convert.FromHexString("0065000000000000"))
        );
    }

    [Test]
    public async Task Each_model_gets_its_own_request()
    {
        var rival = await ReadAsync(0x1830, OldCells);
        var buds = await ReadAsync(SteelSeriesProtocol.GameBudsProductId, GameBuds(0x03, 0x03, 80, 64));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rival.Request, Is.EqualTo(new byte[] { 0x00, 0xAA, 0x01 }));
            Assert.That(rival.Reading.Percent, Is.EqualTo(19));
            Assert.That(buds.Request, Is.EqualTo(new byte[] { 0x00, 0xB0 }));
            Assert.That(buds.Reading.Percent, Is.EqualTo(64));
        }
    }

    [Test]
    public void GameBuds_read_the_lower_bud_out_of_the_case()
    {
        var reading = SteelSeriesProtocol.ParseGameBuds(GameBuds(0x03, 0x03, 80, 64));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SteelSeriesProtocol.IsGameBudsReport(GameBuds(0x03, 0x03, 80, 64)), Is.True);
            Assert.That(reading.Percent, Is.EqualTo(64));
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [Test]
    public void GameBuds_ignore_a_bud_in_the_case()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SteelSeriesProtocol.ParseGameBuds(GameBuds(0x03, 0x02, 72, 0)).Percent, Is.EqualTo(72));
            Assert.That(SteelSeriesProtocol.ParseGameBuds(GameBuds(0x02, 0x03, 0, 55)).Percent, Is.EqualTo(55));
        }
    }

    [Test]
    public void GameBuds_with_no_bud_out_of_the_case_are_no_reading()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SteelSeriesProtocol.ParseGameBuds(GameBuds(0x02, 0x00, 0, 0))
        );

        Assert.That(error.Message, Does.Contain("02/00"));
    }

    [Test]
    public void GameBuds_above_100_are_no_reading()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SteelSeriesProtocol.ParseGameBuds(GameBuds(0x03, 0x03, 101, 101))
        );
    }

    [Test]
    public void A_short_report_is_not_the_gamebuds_answer()
    {
        Assert.That(SteelSeriesProtocol.IsGameBudsReport([0x00, 0xB0, 0x00]), Is.False);
    }
}
