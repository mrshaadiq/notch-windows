using NotchWindows.Providers;

namespace NotchWindows.Core.Tests;

public class VolumeProviderTests
{
    private sealed class FakeVolumeSource : IVolumeSource
    {
        public float Level { get; set; }
        public bool IsMuted { get; set; }
        public event Action? Changed;
        public void Fire() => Changed?.Invoke();
    }

    private static (VolumeProvider Provider, FakeVolumeSource Source, List<IslandEvent> Events) Create()
    {
        var source = new FakeVolumeSource();
        var provider = new VolumeProvider(source, new ManualClock());
        var events = new List<IslandEvent>();
        provider.Raised += events.Add;
        provider.Start();
        return (provider, source, events);
    }

    [Fact]
    public void Level_Zero_ReportsZeroPercent()
    {
        var (_, source, events) = Create();
        source.Level = 0f;
        source.Fire();
        var info = Assert.IsType<VolumeInfo>(Assert.Single(events).Payload);
        Assert.Equal(0, info.Percent);
        Assert.False(info.IsMuted);
    }

    [Fact]
    public void Level_One_ReportsHundredPercent()
    {
        var (_, source, events) = Create();
        source.Level = 1f;
        source.Fire();
        var info = Assert.IsType<VolumeInfo>(Assert.Single(events).Payload);
        Assert.Equal(100, info.Percent);
    }

    [Fact]
    public void Mute_KeepsLevelAndReportsMuted()
    {
        var (_, source, events) = Create();
        source.Level = 0.42f;
        source.IsMuted = true;
        source.Fire();
        var info = Assert.IsType<VolumeInfo>(Assert.Single(events).Payload);
        Assert.Equal(42, info.Percent);
        Assert.True(info.IsMuted);
    }

    [Theory]
    [InlineData(-0.5f, 0)]
    [InlineData(1.7f, 100)]
    [InlineData(float.NaN, 0)]
    public void Level_OutOfRange_IsClamped(float level, int expected)
    {
        var (_, source, events) = Create();
        source.Level = level;
        source.Fire();
        var info = Assert.IsType<VolumeInfo>(Assert.Single(events).Payload);
        Assert.Equal(expected, info.Percent);
    }

    [Fact]
    public void Changed_RaisesVolumeEventWithSameKey()
    {
        var (_, source, events) = Create();
        source.Level = 0.3f;
        source.Fire();
        source.Level = 0.6f;
        source.Fire();
        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(IslandKind.Volume, e.Kind);
            Assert.Equal(EventPriority.Volume, e.Priority);
            Assert.Equal(TimeSpan.FromSeconds(2), e.Duration);
            Assert.Equal("volume", e.Key);
        });
    }

    [Fact]
    public void Stop_UnsubscribesFromSource()
    {
        var (provider, source, events) = Create();
        provider.Stop();
        source.Fire();
        Assert.Empty(events);
        Assert.Equal(ProviderStatus.Stopped, provider.Status);
    }
}
