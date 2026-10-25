using System.Runtime.InteropServices;
using Microsoft.Win32;
using NotchWindows.Providers;

namespace NotchWindows.Native;

public sealed class PowerStatusSource : IPowerStatusSource, IDisposable
{
    private const byte NoBatteryFlag = 128;
    private const byte UnknownPercent = 255;
    private const byte AcOnline = 1;

    // SYSTEM_POWER_STATUS (winbase.h): four BYTEs then two DWORDs, 12 bytes total.
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private readonly object _gate = new();
    private Action? _changed;
    private bool _subscribed;
    private bool _disposed;

    public event Action? Changed
    {
        add
        {
            lock (_gate)
            {
                _changed += value;
                if (!_subscribed && !_disposed)
                {
                    SystemEvents.PowerModeChanged += OnPowerModeChanged;
                    _subscribed = true;
                }
            }
        }
        remove
        {
            lock (_gate)
            {
                _changed -= value;
                if (_changed is null && _subscribed)
                {
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                    _subscribed = false;
                }
            }
        }
    }

    public PowerSnapshot Read()
    {
        if (!GetSystemPowerStatus(out var s))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        var hasBattery = (s.BatteryFlag & NoBatteryFlag) == 0;
        int? percent = s.BatteryLifePercent == UnknownPercent ? null : Math.Clamp((int)s.BatteryLifePercent, 0, 100);
        return new PowerSnapshot(hasBattery, percent, s.ACLineStatus == AcOnline);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // StatusChange fires on plug or unplug; Resume re-syncs after sleep.
        if (e.Mode is PowerModes.StatusChange or PowerModes.Resume)
            RaiseChanged();
    }

    private void RaiseChanged()
    {
        Action? handler;
        lock (_gate) handler = _changed;
        handler?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            if (_subscribed)
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                _subscribed = false;
            }
            _changed = null;
        }
    }
}
