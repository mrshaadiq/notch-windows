using NotchWindows.Core;

namespace NotchWindows.Providers;

public sealed record BrightnessInfo(int Percent);

public sealed class BrightnessProvider(IBrightnessSource source, IClock clock) : IEventProvider
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IClock _clock = clock;
    private readonly object _gate = new();
    private Timer? _timer;
    private int _status = (int)ProviderStatus.Stopped;
    private int? _last;

    public string Name => "Brightness";
    public ProviderStatus Status => (ProviderStatus)Volatile.Read(ref _status);

    public event Action<IslandEvent>? Raised;
    public event Action<AmbientUpdate>? AmbientChanged { add { } remove { } }
    public event Action<Exception>? Faulted;

    public void Start()
    {
        lock (_gate)
        {
            if (Status != ProviderStatus.Stopped) return;
            _last = null;
            Volatile.Write(ref _status, (int)ProviderStatus.Running);
            // First tick after one interval keeps Start non-blocking; the timer callback runs on the thread pool.
            _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (Status == ProviderStatus.Stopped) return;
            Volatile.Write(ref _status, (int)ProviderStatus.Stopped);
            DisposeTimer();
        }
    }

    public void Poll()
    {
        Exception? fault = null;
        IslandEvent? evt = null;
        lock (_gate)
        {
            if (Status != ProviderStatus.Running) return;
            try
            {
                var value = source.Read();
                if (value is null)
                {
                    Volatile.Write(ref _status, (int)ProviderStatus.Unavailable);
                    DisposeTimer();
                    return;
                }
                var percent = Math.Clamp(value.Value, 0, 100);
                var previous = _last;
                _last = percent;
                // The first reading is a baseline, not a change.
                if (previous is not null && previous != percent)
                {
                    evt = new IslandEvent(
                        IslandKind.Brightness,
                        EventPriority.Brightness,
                        Duration,
                        "brightness",
                        new BrightnessInfo(percent));
                }
            }
            catch (Exception ex)
            {
                fault = ex;
            }
        }

        // Raise outside the lock so handlers cannot deadlock Stop.
        try
        {
            if (evt is not null && Status == ProviderStatus.Running) Raised?.Invoke(evt);
            if (fault is not null && Status == ProviderStatus.Running) Faulted?.Invoke(fault);
        }
        catch
        {
            // Never let a handler exception escape on the timer thread.
        }
    }

    private void DisposeTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
