using DeviceBatteryInfo.Core;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class ChargingInferenceTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeSource(string id) : IBatterySource
    {
        public string Id => id;

        public string DisplayName => id;

        public BatterySourceKind Kind => BatterySourceKind.Headset;

        public ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(BatteryReading.Unavailable);
    }

    private static (ChargingInference Inference, BatteryRegistry Registry, ManualTimeProvider Time) Build()
    {
        var time = new ManualTimeProvider();
        var registry = new BatteryRegistry(time);
        return (new ChargingInference(registry, time), registry, time);
    }

    // What the Bluetooth source reports: a level, and a discharging state it cannot actually know.
    private static BatteryReading Assumed(int percent) =>
        new()
        {
            Percent = percent,
            Status = BatteryStatus.Discharging,
            StatusIsAssumed = true,
        };

    private static BatteryStatus Feed(
        ChargingInference inference,
        ManualTimeProvider time,
        params int[] levels
    )
    {
        var status = BatteryStatus.Unknown;
        foreach (var level in levels)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            status = inference.Apply("buds", Assumed(level)).Status;
        }

        return status;
    }

    [Test]
    public void A_rise_of_one_point_is_jitter_and_two_points_is_charging()
    {
        var (inference, _, time) = Build();

        Assert.That(Feed(inference, time, 50, 51), Is.EqualTo(BatteryStatus.Discharging));
        Assert.That(Feed(inference, time, 52), Is.EqualTo(BatteryStatus.Charging));
    }

    [Test]
    public void A_single_ten_percent_step_is_charging()
    {
        var (inference, _, time) = Build();

        Assert.That(Feed(inference, time, 40, 50), Is.EqualTo(BatteryStatus.Charging));
    }

    [Test]
    public void A_drop_below_the_peak_ends_charging()
    {
        var (inference, _, time) = Build();
        Feed(inference, time, 40, 50, 60);

        Assert.That(Feed(inference, time, 50), Is.EqualTo(BatteryStatus.Discharging));
    }

    [Test]
    public void Holding_the_level_while_charging_stays_charging_until_the_idle_timeout()
    {
        var (inference, _, time) = Build();
        Feed(inference, time, 80, 90, 100);

        time.Advance(ChargingInference.IdleTimeout - TimeSpan.FromMinutes(2));
        Assert.That(Feed(inference, time, 100), Is.EqualTo(BatteryStatus.Charging));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.That(Feed(inference, time, 100), Is.EqualTo(BatteryStatus.Discharging));
    }

    [Test]
    public void A_rise_spread_over_more_than_the_window_is_not_charging()
    {
        var (inference, _, time) = Build();
        Feed(inference, time, 50);

        time.Advance(ChargingInference.RiseWindow + TimeSpan.FromMinutes(1));
        Assert.That(Feed(inference, time, 52), Is.EqualTo(BatteryStatus.Discharging));
    }

    [Test]
    public void After_charging_ends_the_old_low_does_not_restart_it()
    {
        var (inference, _, time) = Build();
        Feed(inference, time, 40, 50, 60, 59);

        Assert.That(Feed(inference, time, 60), Is.EqualTo(BatteryStatus.Discharging));
    }

    [Test]
    public void A_state_the_source_reported_is_never_replaced()
    {
        var (inference, _, time) = Build();

        foreach (var level in new[] { 40, 50, 60 })
        {
            time.Advance(TimeSpan.FromMinutes(1));
            var reading = inference.Apply(
                "mouse",
                BatteryReading.FromPercent(level, BatteryStatus.Discharging)
            );
            Assert.That(reading.Status, Is.EqualTo(BatteryStatus.Discharging));
        }
    }

    [Test]
    public void An_unknown_state_is_inferred_too()
    {
        var (inference, _, time) = Build();
        inference.Apply("phone", BatteryReading.FromPercent(30));
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.That(
            inference.Apply("phone", BatteryReading.FromPercent(35)).Status,
            Is.EqualTo(BatteryStatus.Charging)
        );
    }

    [Test]
    public void A_removed_device_starts_over()
    {
        var (inference, registry, time) = Build();
        var source = new FakeSource("buds");
        Feed(inference, time, 40);
        registry.Update(source, Assumed(40));
        registry.Retain(new HashSet<string>(StringComparer.Ordinal));

        Assert.That(Feed(inference, time, 50), Is.EqualTo(BatteryStatus.Discharging));
    }
}
