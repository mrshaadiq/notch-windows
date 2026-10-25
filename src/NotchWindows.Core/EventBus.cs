namespace NotchWindows.Core;

public sealed class EventBus(Action<Action>? dispatch = null)
{
    private readonly object _gate = new();
    private Action<IslandEvent>[] _handlers = [];
    private Action<AmbientUpdate>[] _ambientHandlers = [];

    public void Subscribe(Action<IslandEvent> handler)
    {
        lock (_gate) _handlers = [.. _handlers, handler];
    }

    public void SubscribeAmbient(Action<AmbientUpdate> handler)
    {
        lock (_gate) _ambientHandlers = [.. _ambientHandlers, handler];
    }

    public void Publish(IslandEvent e)
    {
        Action<IslandEvent>[] snapshot;
        lock (_gate) snapshot = _handlers;
        foreach (var h in snapshot)
            Run(() => h(e));
    }

    public void PublishAmbient(AmbientUpdate u)
    {
        Action<AmbientUpdate>[] snapshot;
        lock (_gate) snapshot = _ambientHandlers;
        foreach (var h in snapshot)
            Run(() => h(u));
    }

    private void Run(Action action)
    {
        if (dispatch is null) action();
        else dispatch(action);
    }
}
