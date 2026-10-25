using NotchWindows.Providers;

namespace NotchWindows.Core.Tests;

public class MediaProviderTests
{
    private sealed class FakeMediaSource : IMediaSessionSource
    {
        public MediaSnapshot? Current { get; set; }
        public event Action? Changed;
        public void Fire() => Changed?.Invoke();
        public Task TogglePlayPauseAsync() => Task.CompletedTask;
        public Task SkipNextAsync() => Task.CompletedTask;
    }

    private static (MediaProvider Provider, FakeMediaSource Source, List<IslandEvent> Events, List<AmbientUpdate> Ambient, List<Exception> Faults) Create()
    {
        var source = new FakeMediaSource();
        var provider = new MediaProvider(source);
        var events = new List<IslandEvent>();
        var ambient = new List<AmbientUpdate>();
        var faults = new List<Exception>();
        provider.Raised += events.Add;
        provider.AmbientChanged += ambient.Add;
        provider.Faulted += faults.Add;
        provider.Start();
        return (provider, source, events, ambient, faults);
    }

    [Fact]
    public void NoSession_ClearsAmbient()
    {
        var (_, source, events, ambient, faults) = Create();
        source.Current = null;
        source.Fire();
        var update = Assert.Single(ambient);
        Assert.Equal(IslandKind.Media, update.Kind);
        Assert.Null(update.Payload);
        Assert.Empty(events);
        Assert.Empty(faults);
    }

    [Fact]
    public void Playing_SetsAmbientWithSnapshot()
    {
        var (_, source, _, ambient, _) = Create();
        var snap = new MediaSnapshot("Song", "Artist", true, null);
        source.Current = snap;
        source.Fire();
        var update = Assert.Single(ambient);
        Assert.Equal(IslandKind.Media, update.Kind);
        Assert.Equal(snap, update.Payload);
    }

    [Fact]
    public void Paused_ClearsAmbient()
    {
        var (_, source, _, ambient, _) = Create();
        source.Current = new MediaSnapshot("Song", "Artist", false, null);
        source.Fire();
        Assert.Null(Assert.Single(ambient).Payload);
    }

    [Fact]
    public void TrackChange_RaisesMediaEvent()
    {
        var (_, source, events, _, _) = Create();
        source.Current = new MediaSnapshot("A", "X", true, null);
        source.Fire();
        source.Current = new MediaSnapshot("B", "X", true, null);
        source.Fire();
        Assert.Equal(2, events.Count);
        var e = events[1];
        Assert.Equal(IslandKind.Media, e.Kind);
        Assert.Equal(EventPriority.Media, e.Priority);
        Assert.Equal(TimeSpan.FromMilliseconds(2500), e.Duration);
        Assert.Equal("media-track", e.Key);
        Assert.Equal("B", Assert.IsType<MediaSnapshot>(e.Payload).Title);
    }

    [Fact]
    public void SameTrack_NoTransientEvent()
    {
        var (_, source, events, _, _) = Create();
        source.Current = new MediaSnapshot("A", "X", true, null);
        source.Fire();
        source.Current = new MediaSnapshot("A", "X", false, null);
        source.Fire();
        Assert.Single(events);
    }

    [Fact]
    public void NullTitleAndArtist_UseEmptyStrings()
    {
        var (_, source, events, ambient, _) = Create();
        source.Current = new MediaSnapshot(null!, null!, true, null);
        source.Fire();
        var snap = Assert.IsType<MediaSnapshot>(Assert.Single(ambient).Payload);
        Assert.Equal("", snap.Title);
        Assert.Equal("", snap.Artist);
        Assert.DoesNotContain(events, e => ((MediaSnapshot)e.Payload!).Title == "null");
    }

    [Fact]
    public void Stop_UnsubscribesFromSource()
    {
        var (provider, source, events, ambient, _) = Create();
        provider.Stop();
        source.Current = new MediaSnapshot("A", "X", true, null);
        source.Fire();
        Assert.Empty(events);
        Assert.Empty(ambient);
        Assert.Equal(ProviderStatus.Stopped, provider.Status);
    }
}
