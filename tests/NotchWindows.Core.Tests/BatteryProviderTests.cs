using NotchWindows.Providers;

namespace NotchWindows.Core.Tests;

public class BatteryProviderTests
{
    private sealed class FakePowerSource : IPowerStatusSource
    {
        public PowerSnapshot Value { get; set; } = new(true, 50, false);
        public int Reads { get; private set; }
        public event Action? Changed;
        public PowerSnapshot Read()
        {
            Reads++;
            return Value;
        }
        public void RaiseChanged() => Changed?.Invoke();
    }

    private static (BatteryProvider Provider, FakePowerSource Source, List<IslandEvent> Events) Create(PowerSnapshot initial)
    {
        var source = new FakePowerSource { Value = initial };
        var provider = new BatteryProvider(source, new ManualClock());
        var events = new List<IslandEvent>();
        provider.Raised += events.Add;
        provider.Start();
        return (provider, source, events);
    }

    [Fact]
    public void NoBattery_SetsUnavailable()
    {
        var (provider, source, events) = Create(new PowerSnapshot(false, null, true));
        provider.Poll();
        Assert.Equal(ProviderStatus.Unavailable, provider.Status);
        Assert.Empty(events);
        var reads = source.Reads;
        provider.Poll();
        Assert.Equal(reads, source.Reads);
    }

    [Fact]
    public void UnknownPercent_StillRaisesChargingEvent()
    {
        var (provider, source, events) = Create(new PowerSnapshot(true, null, false));
        provider.Poll();
        source.Value = new PowerSnapshot(true, null, true);
        provider.Poll();
        var e = Assert.Single(events);
        Assert.Equal(IslandKind.Battery, e.Kind);
        var snap = Assert.IsType<PowerSnapshot>(e.Payload);
        Assert.Null(snap.Percent);
        Assert.True(snap.IsCharging);
    }

    [Fact]
    public void PlugIn_RaisesChargingEvent()
    {
        var (provider, source, events) = Create(new PowerSnapshot(true, 50, false));
        provider.Poll();
        source.Value = new PowerSnapshot(true, 51, true);
        provider.Poll();
        var e = Assert.Single(events);
        Assert.Equal(IslandKind.Battery, e.Kind);
        Assert.Equal(EventPriority.Battery, e.Priority);
        Assert.Equal(TimeSpan.FromSeconds(3), e.Duration);
        Assert.Equal("battery", e.Key);
        Assert.Equal(new PowerSnapshot(true, 51, true), e.Payload);
    }

    [Fact]
    public void Unplug_RaisesChargingEvent()
    {
        var (provider, source, events) = Create(new PowerSnapshot(true, 80, true));
        provider.Poll();
        source.Value = new PowerSnapshot(true, 80, false);
        provider.Poll();
        Assert.Single(events);
    }

    [Fact]
    public void NoChange_RaisesNothing()
    {
        var (provider, _, events) = Create(new PowerSnapshot(true, 50, false));
        provider.Poll();
        provider.Poll();
        provider.Poll();
        Assert.Empty(events);
        Assert.Equal(ProviderStatus.Running, provider.Status);
    }

    [Fact]
    public void Poll_DoesNotRaiseWhenPercentChangesOnly()
    {
        var (provider, source, events) = Create(new PowerSnapshot(true, 50, false));
        provider.Poll();
        source.Value = new PowerSnapshot(true, 49, false);
        provider.Poll();
        Assert.Empty(events);
    }

    [Fact]
    public void SourceChanged_TriggersImmediateRead()
    {
        var (provider, source, events) = Create(new PowerSnapshot(true, 50, false));
        provider.Poll();
        source.Value = new PowerSnapshot(true, 50, true);
        source.RaiseChanged();
        Assert.Single(events);
    }

    [Fact]
    public void PollAfterStop_DoesNothing()
    {
        var (provider, source, events) = Create(new PowerSnapshot(true, 50, false));
        provider.Poll();
        provider.Stop();
        var reads = source.Reads;
        source.Value = new PowerSnapshot(true, 50, true);
        provider.Poll();
        source.RaiseChanged();
        Assert.Equal(reads, source.Reads);
        Assert.Empty(events);
    }

    [Fact]
    public void SourceThrows_RaisesFaulted()
    {
        var source = new ThrowingSource();
        var provider = new BatteryProvider(source, new ManualClock());
        Exception? fault = null;
        provider.Faulted += e => fault = e;
        provider.Start();
        provider.Poll();
        Assert.NotNull(fault);
    }

    private sealed class ThrowingSource : IPowerStatusSource
    {
        public event Action? Changed { add { } remove { } }
        public PowerSnapshot Read() => throw new InvalidOperationException("boom");
    }
}
