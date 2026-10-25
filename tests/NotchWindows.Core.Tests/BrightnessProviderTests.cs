using NotchWindows.Providers;

namespace NotchWindows.Core.Tests;

public class BrightnessProviderTests
{
    private sealed class FakeBrightnessSource : IBrightnessSource
    {
        public int? Value { get; set; }
        public Exception? Throw { get; set; }
        public int Reads { get; private set; }
        public int? Read()
        {
            Reads++;
            if (Throw is not null) throw Throw;
            return Value;
        }
    }

    private static (BrightnessProvider Provider, FakeBrightnessSource Source, List<IslandEvent> Events) Create(int? initial)
    {
        var source = new FakeBrightnessSource { Value = initial };
        var provider = new BrightnessProvider(source, new ManualClock());
        var events = new List<IslandEvent>();
        provider.Raised += events.Add;
        provider.Start();
        return (provider, source, events);
    }

    [Fact]
    public void Poll_Unsupported_SetsUnavailable()
    {
        var (provider, source, events) = Create(null);
        provider.Poll();
        Assert.Equal(ProviderStatus.Unavailable, provider.Status);
        Assert.Empty(events);
        var reads = source.Reads;
        provider.Poll();
        Assert.Equal(reads, source.Reads);
    }

    [Fact]
    public void Poll_UnchangedValue_RaisesNothing()
    {
        var (provider, _, events) = Create(50);
        provider.Poll();
        provider.Poll();
        provider.Poll();
        Assert.Empty(events);
        Assert.Equal(ProviderStatus.Running, provider.Status);
    }

    [Fact]
    public void Poll_ChangedValue_RaisesBrightnessEvent()
    {
        var (provider, source, events) = Create(50);
        provider.Poll();
        source.Value = 70;
        provider.Poll();
        var e = Assert.Single(events);
        Assert.Equal(IslandKind.Brightness, e.Kind);
        Assert.Equal(EventPriority.Brightness, e.Priority);
        Assert.Equal(TimeSpan.FromSeconds(2), e.Duration);
        Assert.Equal("brightness", e.Key);
        Assert.Equal(70, Assert.IsType<BrightnessInfo>(e.Payload).Percent);
    }

    [Fact]
    public void Poll_NotifiesOnceWhileValueKeepsChanging()
    {
        var (provider, source, events) = Create(10);
        provider.Poll();
        source.Value = 20;
        provider.Poll();
        provider.Poll();
        source.Value = 30;
        provider.Poll();
        provider.Poll();
        Assert.Equal(new[] { 20, 30 }, events.Select(x => ((BrightnessInfo)x.Payload!).Percent));
    }

    [Fact]
    public void Poll_SourceThrows_RaisesFaultedAndDoesNotEscape()
    {
        var (provider, source, _) = Create(10);
        Exception? fault = null;
        provider.Faulted += ex => fault = ex;
        source.Throw = new InvalidOperationException("wmi");
        provider.Poll();
        Assert.IsType<InvalidOperationException>(fault);
    }

    [Fact]
    public void Poll_AfterStop_DoesNothing()
    {
        var (provider, source, events) = Create(10);
        provider.Poll();
        provider.Stop();
        source.Value = 90;
        provider.Poll();
        Assert.Empty(events);
        Assert.Equal(ProviderStatus.Stopped, provider.Status);
    }
}
