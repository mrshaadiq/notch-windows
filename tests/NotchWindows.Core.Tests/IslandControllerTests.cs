using NotchWindows.Core;

namespace NotchWindows.Core.Tests;

public class IslandControllerTests
{
    private static (IslandController Controller, ManualClock Clock) Create()
    {
        var clock = new ManualClock();
        return (new IslandController(new IslandQueue(clock), clock), clock);
    }

    private static IslandEvent Event(TimeSpan? duration = null) =>
        new(IslandKind.Notification, 1, duration ?? TimeSpan.FromSeconds(3));

    [Fact]
    public void State_NoEventNoAmbient_IsIdle()
    {
        var (c, _) = Create();
        Assert.Equal(IslandState.Idle, c.State);
    }

    [Fact]
    public void State_AmbientOnly_IsCompact()
    {
        var (c, _) = Create();
        c.ApplyAmbient(new AmbientUpdate(IslandKind.Media, "song"));
        Assert.Equal(IslandState.Compact, c.State);
    }

    [Fact]
    public void State_EventWhileAmbient_IsExpanded()
    {
        var (c, clock) = Create();
        c.ApplyAmbient(new AmbientUpdate(IslandKind.Media, "song"));
        c.Publish(Event());
        Assert.Equal(IslandState.Expanded, c.State);

        clock.Advance(TimeSpan.FromSeconds(4));
        c.Tick();
        Assert.Equal(IslandState.Compact, c.State);
    }

    [Fact]
    public void StateChanged_RaisedOnlyWhenStateChanges()
    {
        var (c, _) = Create();
        var raised = new List<IslandState>();
        c.StateChanged += (_, s) => raised.Add(s);

        c.ApplyAmbient(new AmbientUpdate(IslandKind.Media, "a"));
        c.ApplyAmbient(new AmbientUpdate(IslandKind.Media, "b"));
        c.Publish(Event());
        c.Publish(new IslandEvent(IslandKind.Volume, 0, TimeSpan.FromSeconds(1)));

        Assert.Equal([IslandState.Compact, IslandState.Expanded], raised);
    }

    [Fact]
    public void ApplyAmbient_NullPayload_ClearsKind()
    {
        var (c, _) = Create();
        c.ApplyAmbient(new AmbientUpdate(IslandKind.Media, "song"));
        Assert.True(c.Ambient.ContainsKey(IslandKind.Media));

        c.ApplyAmbient(new AmbientUpdate(IslandKind.Media, null));
        Assert.False(c.Ambient.ContainsKey(IslandKind.Media));
        Assert.Equal(IslandState.Idle, c.State);
    }

    [Fact]
    public void SetPointerOver_True_PausesAutoCollapse()
    {
        var (c, clock) = Create();
        c.Publish(Event());
        c.SetPointerOver(true);
        clock.Advance(TimeSpan.FromSeconds(10));
        c.Tick();
        Assert.Equal(IslandState.Expanded, c.State);

        c.SetPointerOver(false);
        clock.Advance(TimeSpan.FromSeconds(4));
        c.Tick();
        Assert.Equal(IslandState.Idle, c.State);
    }
}
