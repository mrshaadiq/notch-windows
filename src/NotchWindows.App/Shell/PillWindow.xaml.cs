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
#if DEBUG
        AddLatencyOverlay();
#endif
    }

    /// <summary>Supplies the media control actions (play/pause, next) to the media view.</summary>
    public void ConfigureMedia(Func<Task> togglePlayPause, Func<Task> skipNext)
    {
        ArgumentNullException.ThrowIfNull(togglePlayPause);
        ArgumentNullException.ThrowIfNull(skipNext);
        Pill.ConfigureMedia(togglePlayPause, skipNext);
    }

#if DEBUG
    // Debug-only text overlay showing the last measured event-to-expand latency.
    private void AddLatencyOverlay()
    {
        if (Content is not PillView)
            return; // already added
        var text = new System.Windows.Controls.TextBlock
        {
            Foreground = System.Windows.Media.Brushes.Yellow,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0, 0, 0)),
            FontSize = 10,
            Padding = new Thickness(3, 0, 3, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Text = "latency: -",
        };
        var grid = new System.Windows.Controls.Grid();
        Content = null;
        grid.Children.Add(Pill);
        grid.Children.Add(text);
        Content = grid;

        Action<IslandKind, double> onMeasured = (kind, ms) =>
            text.Text = $"{kind}: {ms:F1} ms{(ms > 100 ? " (>100)" : "")}";
        Diagnostics.LatencyProbe.Measured += onMeasured;
        Closed += (_, _) => Diagnostics.LatencyProbe.Measured -= onMeasured;
    }
#endif

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
