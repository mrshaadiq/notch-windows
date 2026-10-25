using NotchWindows.Core;

namespace NotchWindows.Providers;

public sealed record VolumeInfo(int Percent, bool IsMuted);

public sealed class VolumeProvider(IVolumeSource source, IClock clock) : IEventProvider
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);

    private readonly IClock _clock = clock;
    private int _running;

    public string Name => "Volume";
    public ProviderStatus Status => Volatile.Read(ref _running) == 1 ? ProviderStatus.Running : ProviderStatus.Stopped;

    public event Action<IslandEvent>? Raised;
    public event Action<AmbientUpdate>? AmbientChanged { add { } remove { } }
    public event Action<Exception>? Faulted;

    public void Start()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        source.Changed += OnChanged;
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _running, 0) == 0) return;
        source.Changed -= OnChanged;
    }

    private void OnChanged()
    {
        if (Volatile.Read(ref _running) == 0) return;
        try
        {
            var level = source.Level;
            var percent = float.IsNaN(level) ? 0 : (int)Math.Clamp(Math.Round(level * 100.0), 0, 100);
            Raised?.Invoke(new IslandEvent(
                IslandKind.Volume,
                EventPriority.Volume,
                Duration,
                "volume",
                new VolumeInfo(percent, source.IsMuted)));
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _running) == 1) Faulted?.Invoke(ex);
        }
    }
}
