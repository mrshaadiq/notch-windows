using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using NotchWindows.App.ViewModels;
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

    // The window owns the view model: it is disposed (StateChanged unsubscribed) when the window closes.
    public void Attach(IslandViewModel viewModel, AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Pill.Configure(settings ?? new AppSettings());
        Pill.DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    // Applies changed settings (idle bar on/off, size) without restarting.
    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Pill.Configure(settings);
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
