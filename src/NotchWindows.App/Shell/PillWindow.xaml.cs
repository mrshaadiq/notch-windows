using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using NotchWindows.Core;
using NotchWindows.Native;

namespace NotchWindows.App.Shell;

public partial class PillWindow : Window
{
    public PillWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        PlaceTopCenter();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        User32.MakeNonActivatingToolWindow(new WindowInteropHelper(this).Handle);
        PlaceTopCenter();
    }

    private void OnDpiChanged(object sender, System.Windows.DpiChangedEventArgs e) => PlaceTopCenter();

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(PlaceTopCenter);

    public void PlaceTopCenter()
    {
        Left = PillPlacement.CenterLeft(0, SystemParameters.PrimaryScreenWidth, Width);
        Top = 0;
    }
}
