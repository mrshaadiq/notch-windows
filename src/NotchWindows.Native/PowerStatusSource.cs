using System.Runtime.InteropServices;
using Microsoft.Win32;
using NotchWindows.Providers;

namespace NotchWindows.Native;

public sealed class PowerStatusSource : IPowerStatusSource, IDisposable
{
    private const byte NoBatteryFlag = 128;
    private const byte UnknownBatteryFlag = 255;
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
    private PowerSnapshot? _last;

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

        lock (_gate)
        {
            _last = Interpret(s.ACLineStatus, s.BatteryFlag, s.BatteryLifePercent, _last);
            return _last;
        }
    }

    // BatteryFlag 255 means "unknown" (seen briefly on resume): keep the previous reading rather
    // than reporting "no battery" (255 has the 128 bit set). Without one, assume a battery whose level is unknown.
    internal static PowerSnapshot Interpret(byte acLineStatus, byte batteryFlag, byte batteryLifePercent, PowerSnapshot? previous)
    {
        if (batteryFlag == UnknownBatteryFlag)
            return previous ?? new PowerSnapshot(true, null, acLineStatus == AcOnline);

        var hasBattery = (batteryFlag & NoBatteryFlag) == 0;
        int? percent = batteryLifePercent == UnknownPercent ? null : Math.Clamp((int)batteryLifePercent, 0, 100);
        return new PowerSnapshot(hasBattery, percent, acLineStatus == AcOnline);
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
