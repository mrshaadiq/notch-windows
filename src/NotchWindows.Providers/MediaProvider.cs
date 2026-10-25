using NotchWindows.Core;

namespace NotchWindows.Providers;

public sealed class MediaProvider(IMediaSessionSource source) : IEventProvider
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(2500);

    private readonly object _gate = new();
    private string? _lastTitle;
    private bool _baselined;
    private int _running;

    public string Name => "Media";
    public ProviderStatus Status => Volatile.Read(ref _running) == 1 ? ProviderStatus.Running : ProviderStatus.Stopped;

    public event Action<IslandEvent>? Raised;
    public event Action<AmbientUpdate>? AmbientChanged;
    public event Action<Exception>? Faulted;

    public void Start()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        source.Changed += OnChanged;
        try
        {
            // Baseline: a track already playing at startup is not a "change".
            var current = Normalize(source.Current);
            lock (_gate)
            {
                _lastTitle = current?.Title;
                _baselined = current != null;
            }
            if (current != null) PublishAmbient(current); // nothing to clear when there is no session yet
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _running) == 1) Faulted?.Invoke(ex);
        }
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _running, 0) == 0) return;
        source.Changed -= OnChanged;
        lock (_gate) _baselined = false;
    }

    private void OnChanged()
    {
        if (Volatile.Read(ref _running) == 0) return;
        try
        {
            var current = Normalize(source.Current);
            bool trackChanged;
            lock (_gate)
            {
                // Rule: the first non-null snapshot after Start is the baseline and never raises a
                // track event (SMTC initializes async, so a song already playing arrives via Changed).
                // After a later null gap, a non-null snapshot with a different title raises as before.
                if (current != null && !_baselined)
                {
                    _baselined = true;
                    trackChanged = false;
                }
                else
                {
                    trackChanged = current != null && current.Title != _lastTitle;
                }
                _lastTitle = current?.Title;
            }
            if (trackChanged)
                Raised?.Invoke(new IslandEvent(IslandKind.Media, EventPriority.Media, Duration, "media-track", current));
            PublishAmbient(current);
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _running) == 1) Faulted?.Invoke(ex);
        }
    }

    private void PublishAmbient(MediaSnapshot? current) =>
        AmbientChanged?.Invoke(new AmbientUpdate(IslandKind.Media, current is { IsPlaying: true } ? current : null));

    private static MediaSnapshot? Normalize(MediaSnapshot? s) =>
        s == null ? null : s with { Title = s.Title ?? "", Artist = s.Artist ?? "" };
}
