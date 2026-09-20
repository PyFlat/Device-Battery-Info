namespace DeviceBatteryInfo.Core;

// Called every poll cycle: keep it cheap, and return an empty list instead of throwing
// when nothing is connected.
public interface IBatterySourceProvider
{
    ValueTask<IReadOnlyList<IBatterySource>> DiscoverAsync(CancellationToken cancellationToken);
}
