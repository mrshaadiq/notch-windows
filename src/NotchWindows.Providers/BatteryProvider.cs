using NotchWindows.Core;

namespace NotchWindows.Providers;

public sealed class BatteryProvider(IPowerStatusSource source, IClock clock) : IEventProvider
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IClock _clock = clock;
    private readonly object _gate = new();
    private Timer? _timer;
    private int _status = (int)ProviderStatus.Stopped;
    private bool? _lastCharging;

    public string Name => "Battery";
    public ProviderStatus Status => (ProviderStatus)Volatile.Read(ref _status);

    public event Action<IslandEvent>? Raised;
    public event Action<AmbientUpdate>? AmbientChanged { add { } remove { } }
    public event Action<Exception>? Faulted;

    public void Start()
    {
        lock (_gate)
        {
            if (Status != ProviderStatus.Stopped) return;
            _lastCharging = null;
            Volatile.Write(ref _status, (int)ProviderStatus.Running);
            source.Changed += OnSourceChanged;
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
            source.Changed -= OnSourceChanged;
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
                var snapshot = source.Read();
                if (!snapshot.HasBattery)
                {
                    // No battery is a normal state, not a fault.
                    Volatile.Write(ref _status, (int)ProviderStatus.Unavailable);
                    source.Changed -= OnSourceChanged;
                    DisposeTimer();
                    return;
                }
                var previous = _lastCharging;
                _lastCharging = snapshot.IsCharging;
                // The first reading is a baseline; percentage-only changes never raise.
                if (previous is not null && previous != snapshot.IsCharging)
                {
                    evt = new IslandEvent(IslandKind.Battery, EventPriority.Battery, Duration, "battery", snapshot);
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
            if (evt is not null) Raised?.Invoke(evt);
            if (fault is not null && Status == ProviderStatus.Running) Faulted?.Invoke(fault);
        }
        catch
        {
            // Never let a handler exception escape on the timer thread.
        }
    }

    private void OnSourceChanged() => Poll();

    private void DisposeTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
