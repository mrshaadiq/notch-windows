using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NotchWindows.Providers;

namespace NotchWindows.Native;

/// <summary>Master volume of the default render endpoint, via IAudioEndpointVolume (NAudio).</summary>
public sealed class NAudioVolumeSource : IVolumeSource, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDevice _device;
    private readonly AudioEndpointVolume _endpoint;
    private int _disposed;

    public NAudioVolumeSource()
    {
        _enumerator = new MMDeviceEnumerator();
        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _endpoint = _device.AudioEndpointVolume;
            _endpoint.OnVolumeNotification += OnVolumeNotification;
        }
        catch
        {
            _device?.Dispose();
            _enumerator.Dispose();
            throw;
        }
    }

    public float Level => _endpoint.MasterVolumeLevelScalar;
    public bool IsMuted => _endpoint.Mute;
    public event Action? Changed;

    private void OnVolumeNotification(AudioVolumeNotificationData data) => Changed?.Invoke();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _endpoint.OnVolumeNotification -= OnVolumeNotification;
        _endpoint.Dispose();
        _device.Dispose();
        _enumerator.Dispose();
    }
}
