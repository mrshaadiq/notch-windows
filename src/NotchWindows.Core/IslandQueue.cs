namespace NotchWindows.Core;

public sealed class IslandQueue(IClock clock, int capacity = 5)
{
    private static readonly TimeSpan MinRequeueRemaining = TimeSpan.FromSeconds(1);

    // Remaining == null means sticky. Seq orders entries by arrival (FIFO / oldest).
    private sealed record Entry(IslandEvent Event, TimeSpan? Remaining, long Seq);

    private readonly List<Entry> _queue = new();
    private long _seq;
    private DateTimeOffset? _pausedAt;

    public IslandEvent? Current { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    // Entries waiting in the queue, excluding Current.
    public int Count => _queue.Count;

    // While paused, time is frozen at the moment of pausing.
    private DateTimeOffset Now => _pausedAt ?? clock.Now;

    public void Enqueue(IslandEvent e)
    {
        if (e.Key is not null)
            _queue.RemoveAll(x => x.Event.Key == e.Key);

        if (Current is null)
        {
            Start(e, e.Duration);
        }
        else if (e.Key is not null && e.Key == Current.Key)
        {
            Start(e, e.Duration);
        }
        else if (e.Priority > Current.Priority)
        {
            var remaining = ExpiresAt is { } exp ? exp - Now : (TimeSpan?)null;
            if (remaining is null || remaining > MinRequeueRemaining)
                _queue.Add(new Entry(Current, remaining, _seq++));
            Start(e, e.Duration);
        }
        else
        {
            _queue.Add(new Entry(e, e.Duration, _seq++));
        }

        TrimToCapacity();
    }

    public void Tick()
    {
        if (_pausedAt is not null)
            return;
        if (Current is not null && ExpiresAt is { } exp && exp <= clock.Now)
            Current = null;
        if (Current is null)
            PromoteNext();
    }

    public void Dismiss()
    {
        Current = null;
        ExpiresAt = null;
        PromoteNext();
    }

    public void SetPaused(bool paused)
    {
        if (paused)
        {
            _pausedAt ??= clock.Now;
        }
        else if (_pausedAt is { } at)
        {
            if (ExpiresAt is { } exp)
                ExpiresAt = exp + (clock.Now - at);
            _pausedAt = null;
        }
    }

    private void Start(IslandEvent e, TimeSpan? remaining)
    {
        Current = e;
        ExpiresAt = remaining is { } r ? Now + r : null;
    }

    private void PromoteNext()
    {
        if (_queue.Count == 0)
        {
            Current = null;
            ExpiresAt = null;
            return;
        }

        var next = _queue.OrderByDescending(x => x.Event.Priority).ThenBy(x => x.Seq).First();
        _queue.Remove(next);
        Start(next.Event, next.Remaining);
    }

    // Capacity includes Current, so the queue holds at most capacity - 1 entries.
    private void TrimToCapacity()
    {
        var max = Math.Max(capacity - 1, 0);
        while (_queue.Count > max)
        {
            var drop = _queue.OrderBy(x => x.Event.Priority).ThenBy(x => x.Seq).First();
            _queue.Remove(drop);
        }
    }
}
