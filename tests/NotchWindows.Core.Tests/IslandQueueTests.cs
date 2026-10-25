using NotchWindows.Core;

namespace NotchWindows.Core.Tests;

public class IslandQueueTests
{
    private static IslandEvent Ev(IslandKind kind, int priority, double? seconds, string? key = null) =>
        new(kind, priority, seconds is null ? null : TimeSpan.FromSeconds(seconds.Value), key);

    [Fact]
    public void Enqueue_WhenEmpty_BecomesCurrent()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 2, "volume"));
        Assert.Equal(IslandKind.Volume, q.Current!.Kind);
        Assert.Equal(0, q.Count);
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(2), q.ExpiresAt);
    }

    [Fact]
    public void Enqueue_HigherPriority_PreemptsCurrent()
    {
        var q = new IslandQueue(new ManualClock());
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 2, "volume"));
        q.Enqueue(Ev(IslandKind.Notification, EventPriority.Notification, 4));
        Assert.Equal(IslandKind.Notification, q.Current!.Kind);
    }

    [Fact]
    public void Enqueue_LowerPriority_WaitsInQueue()
    {
        var q = new IslandQueue(new ManualClock());
        q.Enqueue(Ev(IslandKind.Notification, EventPriority.Notification, 4));
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 2, "volume"));
        Assert.Equal(IslandKind.Notification, q.Current!.Kind);
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Enqueue_Preempted_ReQueuedWhenMoreThanOneSecondRemains()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 5, "volume"));
        clock.Advance(TimeSpan.FromSeconds(1)); // sisa 4 dtk
        q.Enqueue(Ev(IslandKind.Notification, EventPriority.Notification, 4));
        Assert.Equal(1, q.Count);

        clock.Advance(TimeSpan.FromSeconds(4));
        q.Tick();
        Assert.Equal(IslandKind.Volume, q.Current!.Kind);
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(4), q.ExpiresAt); // melanjutkan sisa durasi
    }

    [Fact]
    public void Enqueue_Preempted_DroppedWhenOneSecondOrLessRemains()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        q.Enqueue(new IslandEvent(IslandKind.Volume, EventPriority.Volume, TimeSpan.FromSeconds(2), "volume"));
        clock.Advance(TimeSpan.FromMilliseconds(1500)); // sisa 0,5 dtk
        q.Enqueue(new IslandEvent(IslandKind.Notification, EventPriority.Notification, TimeSpan.FromSeconds(4)));
        Assert.Equal(IslandKind.Notification, q.Current!.Kind);
        Assert.Equal(0, q.Count); // volume dibuang
    }

    [Fact]
    public void Enqueue_PreemptedSticky_IsNeverDropped()
    {
        var q = new IslandQueue(new ManualClock());
        q.Enqueue(Ev(IslandKind.Media, EventPriority.Media, null, "media"));
        q.Enqueue(Ev(IslandKind.Notification, EventPriority.Notification, 4));
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Enqueue_SameKey_ReplacesCurrentAndResetsExpiry()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 2, "volume"));
        clock.Advance(TimeSpan.FromSeconds(1));
        var replacement = Ev(IslandKind.Volume, EventPriority.Volume, 2, "volume");
        q.Enqueue(replacement);
        Assert.Same(replacement, q.Current);
        Assert.Equal(0, q.Count);
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(2), q.ExpiresAt);
    }

    [Fact]
    public void Enqueue_SameKeyInQueue_ReplacesEntry()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        q.Enqueue(Ev(IslandKind.Notification, EventPriority.Notification, 4));
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 2, "volume"));
        var newer = Ev(IslandKind.Volume, EventPriority.Volume, 3, "volume");
        q.Enqueue(newer);
        Assert.Equal(1, q.Count);

        clock.Advance(TimeSpan.FromSeconds(4));
        q.Tick();
        Assert.Same(newer, q.Current);
    }

    [Fact]
    public void Enqueue_OverCapacity_DropsLowestPriority()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock, capacity: 3);
        q.Enqueue(Ev(IslandKind.Timer, EventPriority.Timer, 10, "t"));
        q.Enqueue(Ev(IslandKind.Media, EventPriority.Media, 10, "m"));
        q.Enqueue(Ev(IslandKind.Battery, EventPriority.Battery, 10, "b"));
        q.Enqueue(Ev(IslandKind.Notification, EventPriority.Notification, 10, "n"));
        Assert.Equal(2, q.Count);

        clock.Advance(TimeSpan.FromSeconds(10));
        q.Tick();
        Assert.Equal(IslandKind.Notification, q.Current!.Kind);
        clock.Advance(TimeSpan.FromSeconds(10));
        q.Tick();
        Assert.Equal(IslandKind.Battery, q.Current!.Kind); // media (terendah) sudah dibuang
        clock.Advance(TimeSpan.FromSeconds(10));
        q.Tick();
        Assert.Null(q.Current);
    }

    [Fact]
    public void Enqueue_OverCapacity_TieDropsOldest()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock, capacity: 3);
        q.Enqueue(Ev(IslandKind.Timer, EventPriority.Timer, 10, "t"));
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 10, "first"));
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 10, "second"));
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 10, "third"));
        Assert.Equal(2, q.Count);

        clock.Advance(TimeSpan.FromSeconds(10));
        q.Tick();
        Assert.Equal("second", q.Current!.Key);
    }

    [Fact]
    public void Tick_AfterDuration_PromotesHighestPriority()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        q.Enqueue(Ev(IslandKind.Timer, EventPriority.Timer, 2, "t"));
        q.Enqueue(Ev(IslandKind.Media, EventPriority.Media, 5, "m"));
        q.Enqueue(Ev(IslandKind.Battery, EventPriority.Battery, 5, "b"));
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 5, "v"));

        q.Tick();
        Assert.Equal(IslandKind.Timer, q.Current!.Kind); // belum habis

        clock.Advance(TimeSpan.FromSeconds(2));
        q.Tick();
        Assert.Equal(IslandKind.Battery, q.Current!.Kind);
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void Tick_SamePriority_PromotesFifo()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        q.Enqueue(Ev(IslandKind.Timer, EventPriority.Timer, 1, "t"));
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 5, "a"));
        q.Enqueue(Ev(IslandKind.Brightness, EventPriority.Brightness, 5, "b"));
        clock.Advance(TimeSpan.FromSeconds(1));
        q.Tick();
        Assert.Equal("a", q.Current!.Key);
    }

    [Fact]
    public void Tick_LastEventExpires_ClearsCurrent()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 2, "v"));
        clock.Advance(TimeSpan.FromSeconds(2));
        q.Tick();
        Assert.Null(q.Current);
        Assert.Null(q.ExpiresAt);
    }

    [Fact]
    public void Tick_StickyEvent_NeverExpires()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        q.Enqueue(Ev(IslandKind.Timer, EventPriority.Timer, null, "t"));
        Assert.Null(q.ExpiresAt);
        clock.Advance(TimeSpan.FromHours(10));
        q.Tick();
        Assert.Equal(IslandKind.Timer, q.Current!.Kind);
    }

    [Fact]
    public void Dismiss_RemovesStickyCurrent()
    {
        var q = new IslandQueue(new ManualClock());
        q.Enqueue(Ev(IslandKind.Timer, EventPriority.Timer, null, "t"));
        q.Dismiss();
        Assert.Null(q.Current);
    }

    [Fact]
    public void Dismiss_PromotesNextEntry()
    {
        var q = new IslandQueue(new ManualClock());
        q.Enqueue(Ev(IslandKind.Timer, EventPriority.Timer, null, "t"));
        q.Enqueue(Ev(IslandKind.Volume, EventPriority.Volume, 2, "v"));
        q.Dismiss();
        Assert.Equal(IslandKind.Volume, q.Current!.Kind);
    }

    [Fact]
    public void SetPaused_ExtendsExpiryByPausedDuration()
    {
        var clock = new ManualClock();
        var q = new IslandQueue(clock);
        var start = clock.Now;
        q.Enqueue(Ev(IslandKind.Notification, EventPriority.Notification, 4));
        clock.Advance(TimeSpan.FromSeconds(1));
        q.SetPaused(true);
        clock.Advance(TimeSpan.FromSeconds(10));
        q.Tick();
        Assert.NotNull(q.Current); // tidak habis saat paused
        q.SetPaused(false);
        Assert.Equal(start + TimeSpan.FromSeconds(14), q.ExpiresAt);
        q.Tick();
        Assert.NotNull(q.Current);
    }

    [Fact]
    public void Storm_HundredsOfVolumeEvents_KeepsCountWithinCapacity()
    {
        var q = new IslandQueue(new ManualClock());
        for (var i = 0; i < 500; i++)
            q.Enqueue(new IslandEvent(IslandKind.Brightness, EventPriority.Brightness, TimeSpan.FromSeconds(2), $"k{i}"));
        Assert.True(q.Count <= 4); // kapasitas 5 termasuk Current
    }
}
