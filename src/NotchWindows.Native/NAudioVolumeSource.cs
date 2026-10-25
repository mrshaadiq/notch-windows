using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NotchWindows.Providers;

namespace NotchWindows.Native;

/// <summary>Master volume of the default render endpoint, via IAudioEndpointVolume (NAudio).</summary>
public sealed class NAudioVolumeSource : IVolumeSource, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly DeviceNotificationClient _notifications;
    private readonly object _gate = new();
    // Null while no default render device exists; rebound on the next default-device change.
    private MMDevice? _device;
    private AudioEndpointVolume? _endpoint;
    private int _disposed;

    public NAudioVolumeSource()
    {
        _enumerator = new MMDeviceEnumerator();
        _notifications = new DeviceNotificationClient(this);
        try
        {
            // No render device at startup: throw, so the provider is not registered.
            Bind(_enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia));
            _enumerator.RegisterEndpointNotificationCallback(_notifications);
        }
        catch
        {
            Unbind();
            _enumerator.Dispose();
            throw;
        }
    }

    public float Level
    {
        get { lock (_gate) return _endpoint?.MasterVolumeLevelScalar ?? 0f; }
    }

    public bool IsMuted
    {
        get { lock (_gate) return _endpoint?.Mute ?? false; }
    }

    public event Action? Changed;

    private void OnVolumeNotification(AudioVolumeNotificationData data) => Changed?.Invoke();

    // Caller holds _gate (or is the constructor).
    private void Bind(MMDevice device)
    {
        _device = device;
        _endpoint = device.AudioEndpointVolume;
        _endpoint.OnVolumeNotification += OnVolumeNotification;
    }

    // Caller holds _gate (or is the constructor).
    private void Unbind()
    {
        if (_endpoint is not null)
        {
            _endpoint.OnVolumeNotification -= OnVolumeNotification;
            _endpoint.Dispose();
            _endpoint = null;
        }
        _device?.Dispose();
        _device = null;
    }

    private void OnDefaultDeviceChanged()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) == 1) return;
            Unbind();
            try
            {
                Bind(_enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia));
            }
            catch
            {
                // No default render device: stay unbound until the next default-device change.
                Unbind();
                return;
            }
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _enumerator.UnregisterEndpointNotificationCallback(_notifications);
        lock (_gate) Unbind();
        _enumerator.Dispose();
    }

    private sealed class DeviceNotificationClient(NAudioVolumeSource owner) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow != DataFlow.Render || role != Role.Multimedia) return;
            // Rebind off the callback thread: MMDevice objects must not be released inside the callback.
            ThreadPool.QueueUserWorkItem(_ => owner.OnDefaultDeviceChanged());
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
