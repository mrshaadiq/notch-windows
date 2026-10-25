namespace NotchWindows.Core.Tests;

public class EventBusTests
{
    private static IslandEvent Ev() => new(IslandKind.Notification, 1, TimeSpan.FromSeconds(1));

    [Fact]
    public void Publish_DeliversToAllSubscribers()
    {
        var bus = new EventBus();
        var a = new List<IslandEvent>();
        var b = new List<IslandEvent>();
        bus.Subscribe(a.Add);
        bus.Subscribe(b.Add);
        var e = Ev();

        bus.Publish(e);

        Assert.Equal([e], a);
        Assert.Equal([e], b);
    }

    [Fact]
    public void Publish_UsesDispatcher()
    {
        var dispatched = new List<Action>();
        var bus = new EventBus(dispatched.Add);
        var received = new List<IslandEvent>();
        bus.Subscribe(received.Add);
        var e = Ev();

        bus.Publish(e);

        Assert.Empty(received);
        var action = Assert.Single(dispatched);
        action();
        Assert.Equal([e], received);
    }

    [Fact]
    public void PublishAmbient_DeliversToAmbientSubscribers()
    {
        var bus = new EventBus();
        var got = new List<AmbientUpdate>();
        var events = new List<IslandEvent>();
        bus.SubscribeAmbient(got.Add);
        bus.Subscribe(events.Add);
        var u = new AmbientUpdate(IslandKind.Media, "x");

        bus.PublishAmbient(u);

        Assert.Equal([u], got);
        Assert.Empty(events);
    }
}
