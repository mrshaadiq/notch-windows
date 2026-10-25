namespace NotchWindows.Core;

public sealed class IslandController
{
    private readonly IslandQueue _queue;
    private readonly Dictionary<IslandKind, object?> _ambient = new();

    public IslandController(IslandQueue queue, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(clock);
        _queue = queue;
    }

    public IslandState State { get; private set; } = IslandState.Idle;

    public IslandEvent? Current => _queue.Current;

    public IReadOnlyDictionary<IslandKind, object?> Ambient => _ambient;

    public event EventHandler<IslandState>? StateChanged;

    public void Publish(IslandEvent e)
    {
        _queue.Enqueue(e);
        Recompute();
    }

    public void ApplyAmbient(AmbientUpdate u)
    {
        if (u.Payload is null)
            _ambient.Remove(u.Kind);
        else
            _ambient[u.Kind] = u.Payload;
        Recompute();
    }

    public void Tick()
    {
        _queue.Tick();
        Recompute();
    }

    public void SetPointerOver(bool over) => _queue.SetPaused(over);

    public void Dismiss()
    {
        _queue.Dismiss();
        Recompute();
    }

    private void Recompute()
    {
        var next = _queue.Current is not null ? IslandState.Expanded
            : _ambient.Count > 0 ? IslandState.Compact
            : IslandState.Idle;
        if (next == State)
            return;
        State = next;
        StateChanged?.Invoke(this, next);
    }
}
