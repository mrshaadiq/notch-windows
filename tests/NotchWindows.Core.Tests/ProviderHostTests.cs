using Microsoft.Extensions.Logging.Abstractions;

namespace NotchWindows.Core.Tests;

public class ProviderHostTests
{
    private sealed class FakeProvider(string name) : IEventProvider
    {
        private int _startCount;
        public string Name { get; } = name;
        public volatile ProviderStatus StatusValue = ProviderStatus.Stopped;
        public ProviderStatus Status => StatusValue;
        public Func<bool> ThrowOnStart { get; set; } = () => false;
        public int StartCount => Volatile.Read(ref _startCount);
        public event Action<IslandEvent>? Raised;
        public event Action<AmbientUpdate>? AmbientChanged;
        public event Action<Exception>? Faulted;

        public void Start()
        {
            Interlocked.Increment(ref _startCount);
            if (ThrowOnStart()) throw new InvalidOperationException("start failed");
            StatusValue = ProviderStatus.Running;
        }

        public void Stop() => StatusValue = ProviderStatus.Stopped;
        public void Fault() => Faulted?.Invoke(new InvalidOperationException("fault"));
        public void RaiseAmbient() => AmbientChanged?.Invoke(new AmbientUpdate(IslandKind.Media, null));
        public void Raise() => Raised?.Invoke(new IslandEvent(IslandKind.Notification, 1, null));
    }

    private static ProviderHost Host(IEnumerable<IEventProvider> ps, EventBus? bus = null) =>
        new(bus ?? new EventBus(), ps, TimeSpan.Zero, NullLogger<ProviderHost>.Instance);

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met within 1s");
            await Task.Delay(5);
        }
    }

    private static async Task Settle() => await Task.Delay(100);

    [Fact]
    public async Task Host_RestartsFaultedProvider()
    {
        var p = new FakeProvider("a");
        var host = Host([p]);
        host.StartAll();
        Assert.Equal(1, p.StartCount);

        p.Fault();

        await WaitFor(() => p.StartCount == 2);
        Assert.False(host.IsDisabled(p));
    }

    [Fact]
    public async Task Host_DisablesProviderAfterThreeConsecutiveFailures()
    {
        var p = new FakeProvider("a");
        var host = Host([p]);
        host.StartAll();

        p.Fault();
        await WaitFor(() => p.StartCount == 2);
        p.Fault();
        await WaitFor(() => p.StartCount == 3);
        p.Fault();

        await WaitFor(() => host.IsDisabled(p));
        await Settle();
        Assert.Equal(3, p.StartCount);
    }

    [Fact]
    public async Task Host_FailureCounterResetsWhenProviderRaisesEvent()
    {
        var p = new FakeProvider("a");
        var bus = new EventBus();
        var received = 0;
        bus.Subscribe(_ => Interlocked.Increment(ref received));
        var host = Host([p], bus);
        host.StartAll();

        p.Fault();
        await WaitFor(() => p.StartCount == 2);
        p.Fault();
        await WaitFor(() => p.StartCount == 3);
        p.Raise();
        Assert.Equal(1, received);
        p.Fault();
        await WaitFor(() => p.StartCount == 4);
        p.Fault();
        await WaitFor(() => p.StartCount == 5);

        Assert.False(host.IsDisabled(p));
    }

    [Fact]
    public async Task Host_ProviderStartThrows_CountsAsFailure()
    {
        var p = new FakeProvider("a") { ThrowOnStart = () => true };
        var host = Host([p]);

        host.StartAll();

        await WaitFor(() => host.IsDisabled(p));
        await Settle();
        Assert.Equal(3, p.StartCount);
    }

    [Fact]
    public async Task Host_FaultOnOneProvider_DoesNotAffectOthers()
    {
        var bad = new FakeProvider("bad");
        var good = new FakeProvider("good");
        var bus = new EventBus();
        var received = 0;
        bus.Subscribe(_ => Interlocked.Increment(ref received));
        var host = Host([bad, good], bus);
        host.StartAll();

        bad.Fault();
        bad.Fault();
        await WaitFor(() => bad.StartCount >= 2);
        good.Raise();

        Assert.Equal(1, good.StartCount);
        Assert.False(host.IsDisabled(good));
        Assert.Equal(1, received);
    }

    [Fact]
    public async Task Host_UnavailableProvider_IsNotRestarted()
    {
        var p = new FakeProvider("a");
        var host = Host([p]);
        host.StartAll();
        p.StatusValue = ProviderStatus.Unavailable;

        p.Fault();
        await Settle();

        Assert.Equal(1, p.StartCount);
    }
}
