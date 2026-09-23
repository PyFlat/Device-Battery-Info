using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Variables;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class BatteryRegistryTests
{
    private sealed class FakeSource(string id) : IBatterySource
    {
        public string Id => id;

        public string DisplayName => id;

        public BatterySourceKind Kind => BatterySourceKind.Other;

        public ValueTask<BatteryReading> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(BatteryReading.Unavailable);
    }

    [Test]
    public void Update_stores_the_reading_and_raises_changed()
    {
        var registry = new BatteryRegistry(TimeProvider.System);
        var source = new FakeSource("mouse");
        BatterySnapshotChangedEventArgs? seen = null;
        registry.Changed += (_, e) => seen = e;

        registry.Update(source, BatteryReading.FromPercent(80, BatteryStatus.Discharging));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.TryGet("mouse", out var snapshot), Is.True);
            Assert.That(snapshot.Reading.Percent, Is.EqualTo(80));
            Assert.That(snapshot.IsStale, Is.False);
            Assert.That(seen?.Current?.Reading.Percent, Is.EqualTo(80));
        }
    }

    [Test]
    public void Failures_keep_the_last_reading_until_the_stale_threshold()
    {
        var registry = new BatteryRegistry(TimeProvider.System);
        var source = new FakeSource("phone");
        registry.Update(source, BatteryReading.FromPercent(50));

        registry.RecordFailure(source, staleAfter: 3);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.TryGet("phone", out var afterOne) && afterOne.IsStale, Is.False);
            Assert.That(afterOne.Reading.Percent, Is.EqualTo(50));
        }

        registry.RecordFailure(source, staleAfter: 3);
        registry.RecordFailure(source, staleAfter: 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                registry.TryGet("phone", out var afterThree) && afterThree.IsStale,
                Is.True
            );
            Assert.That(afterThree.Reading.Percent, Is.EqualTo(50));
        }
    }

    [Test]
    public void Retain_drops_sources_that_are_no_longer_present()
    {
        var registry = new BatteryRegistry(TimeProvider.System);
        registry.Update(new FakeSource("a"), BatteryReading.FromPercent(1));
        registry.Update(new FakeSource("b"), BatteryReading.FromPercent(2));

        registry.Retain(new HashSet<string> { "a" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.TryGet("a", out _), Is.True);
            Assert.That(registry.TryGet("b", out _), Is.False);
        }
    }
}

[TestFixture]
public sealed class BatteryVariableCatalogTests
{
    [Test]
    public void Builds_seven_variables_per_slot()
    {
        var slots = new[]
        {
            new BatterySlot("system", "This PC", BatterySourceKind.System, DeviceType.System),
            new BatterySlot(
                "phone",
                "Phone",
                BatterySourceKind.Phone,
                DeviceType.AdbPhone,
                AdbAddress: "1.2.3.4:5555"
            ),
            new BatterySlot(
                "gaming-headset",
                "Gaming Headset",
                BatterySourceKind.Headset,
                DeviceType.Bluetooth,
                BluetoothFriendlyName: "CORSAIR HS80 MAX Hands-Free AG"
            ),
        };
        var variables = BatteryVariableCatalog.Build(slots);

        Assert.That(variables, Has.Count.EqualTo(slots.Length * 7));
        Assert.That(variables.Select(v => v.Name), Does.Contain("battery_system_percent"));
        Assert.That(
            variables.Select(v => v.Name),
            Does.Contain("battery_gaming_headset_time_to_full")
        );
        Assert.That(variables.Select(v => v.Name), Does.Contain("battery_system_trend"));
        Assert.That(
            variables.Select(v => v.Name),
            Does.Contain("battery_gaming_headset_trend_rate")
        );
    }

    [TestCase("system-percent", "system", "Percent")]
    [TestCase("gaming-headset-time-to-full", "gaming-headset", "TimeToFull")]
    [TestCase("gaming-headset-charging", "gaming-headset", "Charging")]
    [TestCase("system-trend", "system", "Trend")]
    [TestCase("gaming-headset-trend-rate", "gaming-headset", "TrendRate")]
    public void Resolves_a_local_id_back_to_slot_and_field(
        string localId,
        string expectedSlot,
        string expectedField
    )
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                BatteryVariableCatalog.TryResolve(localId, out var slotId, out var field),
                Is.True
            );
            Assert.That(slotId, Is.EqualTo(expectedSlot));
            Assert.That(field.ToString(), Is.EqualTo(expectedField));
        }
    }

    [Test]
    public void Rejects_an_unknown_suffix()
    {
        Assert.That(BatteryVariableCatalog.TryResolve("system-voltage", out _, out _), Is.False);
    }
}
