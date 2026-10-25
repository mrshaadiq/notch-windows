using Windows.Media.Control;
using Windows.Storage.Streams;
using NotchWindows.Providers;

namespace NotchWindows.Native;

/// <summary>
/// Current media session via SMTC (GlobalSystemMediaTransportControlsSessionManager).
/// Initialization is asynchronous and never throws; with no session or no SMTC, Current is null.
/// </summary>
public sealed class SmtcMediaSource : IMediaSessionSource, IDisposable
{
    private readonly object _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaSnapshot? _current;
    private int _version;
    private int _disposed;

    public SmtcMediaSource()
    {
        _ = InitAsync();
    }

    public MediaSnapshot? Current { get { lock (_gate) return _current; } }
    public event Action? Changed;

    private async Task InitAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            lock (_gate)
            {
                if (_disposed == 1) return;
                _manager = manager;
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            }
            AttachSession(manager.GetCurrentSession());
            await RefreshAsync();
        }
        catch
        {
            // SMTC unavailable: behave as "no session".
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        try
        {
            AttachSession(sender.GetCurrentSession());
            _ = RefreshAsync();
        }
        catch
        {
            // Treat as no change; next event will refresh.
        }
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        lock (_gate)
        {
            if (_disposed == 1) return;
            DetachSession();
            _session = session;
            if (session != null)
            {
                session.PlaybackInfoChanged += OnSessionChanged;
                session.MediaPropertiesChanged += OnSessionChanged;
            }
        }
    }

    // Caller holds _gate.
    private void DetachSession()
    {
        if (_session == null) return;
        _session.PlaybackInfoChanged -= OnSessionChanged;
        _session.MediaPropertiesChanged -= OnSessionChanged;
        _session = null;
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        int version = Interlocked.Increment(ref _version);
        MediaSnapshot? snapshot = null;
        try
        {
            GlobalSystemMediaTransportControlsSession? session;
            lock (_gate) session = _session;
            if (session != null)
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var playing = session.GetPlaybackInfo()?.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var thumb = props.Thumbnail != null ? await ReadThumbnailAsync(props.Thumbnail) : null;
                snapshot = new MediaSnapshot(props.Title ?? "", props.Artist ?? "", playing, thumb);
            }
        }
        catch
        {
            snapshot = null;
        }

        lock (_gate)
        {
            if (_disposed == 1 || version != Volatile.Read(ref _version)) return; // superseded by a newer refresh
            _current = snapshot;
        }
        Changed?.Invoke();
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 4 * 1024 * 1024) return null;
            var buffer = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(buffer);
            return buffer;
        }
        catch
        {
            return null;
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate) session = _session;
        if (session == null) return;
        try { await session.TryTogglePlayPauseAsync(); }
        catch { /* session vanished; ignore */ }
    }

    public async Task SkipNextAsync()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate) session = _session;
        if (session == null) return;
        try { await session.TrySkipNextAsync(); }
        catch { /* session vanished; ignore */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        lock (_gate)
        {
            DetachSession();
            if (_manager != null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
            _current = null;
        }
    }
}
