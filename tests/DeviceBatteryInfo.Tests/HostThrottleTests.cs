using DeviceBatteryInfo.Core;
using DeviceBatteryInfo.Sources.Adb;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Android;
using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

[TestFixture]
public sealed class HostThrottleTests
{
    // Named like the hosting package's internal exception, which is how the retry recognizes it.
    private sealed class HostInvocationException(string message) : Exception(message);

    [Test]
    public async Task A_throttled_call_is_retried_until_it_goes_through()
    {
        var attempts = 0;
        var result = await HostCallRetry.RunAsync(
            () =>
            {
                attempts++;
                return attempts < 3
                    ? throw new HostInvocationException("This plugin is calling back into the host too quickly.")
                    : Task.FromResult("registered");
            },
            CancellationToken.None
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo("registered"));
            Assert.That(attempts, Is.EqualTo(3));
        }
    }

    [Test]
    public void Any_other_failure_is_not_retried()
    {
        var attempts = 0;

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            HostCallRetry.RunAsync<string>(
                () =>
                {
                    attempts++;
                    throw new InvalidOperationException("broken");
                },
                CancellationToken.None
            )
        );
        Assert.That(attempts, Is.EqualTo(1));
    }

    // Holds every battery call open briefly and records how many overlap.
    private sealed class SlowDevice(IAndroidDevice inner, Func<Task> onBatteryRead) : IAndroidDevice
    {
        public string Serial => inner.Serial;

        public AndroidDeviceInfo Info => inner.Info;

        public AndroidDeviceState State => inner.State;

        public async Task<AndroidBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken = default)
        {
            await onBatteryRead();
            return await inner.GetBatteryStateAsync(cancellationToken);
        }

        public Task<AndroidShellResult> ExecuteShellAsync(string command, CancellationToken cancellationToken = default) =>
            inner.ExecuteShellAsync(command, cancellationToken);

        public Task PushFileAsync(string localPath, string remotePath, CancellationToken cancellationToken = default) =>
            inner.PushFileAsync(localPath, remotePath, cancellationToken);

        public Task PullFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default) =>
            inner.PullFileAsync(remotePath, localPath, cancellationToken);

        public Task InstallApkAsync(string apkPath, CancellationToken cancellationToken = default) =>
            inner.InstallApkAsync(apkPath, cancellationToken);

        public Task UninstallPackageAsync(string packageName, CancellationToken cancellationToken = default) =>
            inner.UninstallPackageAsync(packageName, cancellationToken);

        public Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default) =>
            inner.IsPackageInstalledAsync(packageName, cancellationToken);
    }

    private sealed class SlowManager(FakeAndroidDeviceManager inner, Func<Task> onBatteryRead)
        : IAndroidDeviceManager
    {
        public AndroidDeviceAccess Access => inner.Access;

        public IReadOnlyCollection<IAndroidDevice> Devices => inner.Devices;

        public event EventHandler<AndroidDeviceEventArgs>? DeviceConnected
        {
            add => inner.DeviceConnected += value;
            remove => inner.DeviceConnected -= value;
        }

        public event EventHandler<AndroidDeviceEventArgs>? DeviceDisconnected
        {
            add => inner.DeviceDisconnected += value;
            remove => inner.DeviceDisconnected -= value;
        }

        public event EventHandler<AndroidDeviceEventArgs>? DeviceStateChanged
        {
            add => inner.DeviceStateChanged += value;
            remove => inner.DeviceStateChanged -= value;
        }

        public event EventHandler? AccessChanged
        {
            add => inner.AccessChanged += value;
            remove => inner.AccessChanged -= value;
        }

        public IAndroidDevice? FindDevice(string serial) =>
            inner.FindDevice(serial) is { } device ? new SlowDevice(device, onBatteryRead) : null;

        public Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default) =>
            inner.ConnectAsync(address, cancellationToken);
    }

    [Test]
    public async Task At_most_two_phones_are_read_over_adb_at_once()
    {
        var fake = new FakeAndroidDeviceManager();
        var slots = Enumerable
            .Range(1, 5)
            .Select(i =>
            {
                fake.AddDevice($"serial-{i}");
                return new BatterySlot($"phone-{i}", $"Phone {i}", BatterySourceKind.Phone, DeviceType.AdbPhone, AdbAddress: $"serial-{i}");
            })
            .ToArray();

        var inFlight = 0;
        var peak = 0;
        var manager = new SlowManager(
            fake,
            async () =>
            {
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref peak, now);
                await Task.Delay(50);
                Interlocked.Decrement(ref inFlight);
            }
        );

        var catalog = new DeviceCatalog();
        catalog.Set(slots);
        using var provider = new AdbBatterySourceProvider(manager, catalog);
        var sources = await provider.DiscoverAsync(CancellationToken.None);

        await Task.WhenAll(sources.Select(s => s.ReadAsync(CancellationToken.None).AsTask()));

        Assert.That(peak, Is.EqualTo(2));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }
}
