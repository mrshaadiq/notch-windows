using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;
using NotchWindows.App.Animation;
using NotchWindows.App.ViewModels;
using NotchWindows.App.Views;
using NotchWindows.Core;
using NotchWindows.Providers;

namespace NotchWindows.App.Shell;

public partial class PillView : UserControl
{
    private PillAnimator _animator;
    private IslandViewModel? _vm;
    private IslandState _shown = IslandState.Idle;
    private IslandKind? _shownKind;
    private IslandEvent? _shownEvent;
    private bool _swapPending;
    private AppSettings _settings = new();
    private readonly DispatcherTimer _hideTimer;

    // One view instance per kind. Notification and Timer have no view in this phase.
    private readonly VolumeView _volumeView = new();
    private readonly BrightnessView _brightnessView = new();
    private readonly MediaView _mediaView = new();
    private readonly BatteryView _batteryView = new();

    public PillView()
    {
        InitializeComponent();
        _animator = new PillAnimator(Shape, ContentHost, new AppSettings());
        _animator.Snap(IslandState.Idle);
        _hideTimer = new DispatcherTimer { Interval = PillAnimator.CollapseDuration + TimeSpan.FromMilliseconds(50) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); ApplyIdleVisibility(); };
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => { _hideTimer.Stop(); Detach(); };
    }

    // Idle bar size comes from settings. Call before setting DataContext.
    public void Configure(AppSettings settings)
    {
        _settings = settings;
        _animator = new PillAnimator(Shape, ContentHost, settings);
        _animator.Snap(_shown);
        _hideTimer.Stop();
        ApplyIdleVisibility();
    }

    /// <summary>Supplies the media control actions to the media view.</summary>
    public void ConfigureMedia(Func<Task> togglePlayPause, Func<Task> skipNext) =>
        _mediaView.Configure(togglePlayPause, skipNext);

    // Idle with the idle bar disabled: nothing is shown.
    private void ApplyIdleVisibility() =>
        Shape.Visibility = _shown == IslandState.Idle && !_settings.IdleBarEnabled
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = e.NewValue as IslandViewModel;
        if (_vm is null)
            return;
        _vm.PropertyChanged += OnViewModelChanged;
        _shown = _vm.State;
        _shownKind = _vm.Current?.Kind;
        ShowContent(_vm.Current);
        _animator.Snap(_shown);
        ApplyIdleVisibility();
    }

    private void Detach()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelChanged;
        _vm = null;
    }

    // Runs on the UI thread: the view model raises PropertyChanged through its dispatcher.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // State, Ambient and Current are raised together, Current last; react once, on Current.
        if (_vm is null || e.PropertyName != nameof(IslandViewModel.Current))
            return;

        var vm = _vm;
        var state = vm.State;
        var current = vm.Current;
        var kind = current?.Kind;
        if (state != _shown)
        {
            // Leaving Expanded: keep the old content so it does not flash while it fades out.
            if (state == IslandState.Expanded)
            {
                ShowContent(current);
                MarkLatency(current);
            }
            // Show before animating; when collapsing to a hidden idle, hide after the animation.
            Shape.Visibility = Visibility.Visible;
            _animator.Animate(_shown, state);
            _hideTimer.Stop();
            if (state == IslandState.Idle && !_settings.IdleBarEnabled)
                _hideTimer.Start();
        }
        else if (state == IslandState.Expanded && !ReferenceEquals(current, _shownEvent))
        {
            if (kind != _shownKind)
            {
                MarkLatency(current);
                _swapPending = true;
                _animator.Crossfade(() =>
                {
                    _swapPending = false;
                    // Render whatever is current when the swap runs; skip if the pill already left Expanded.
                    if (_vm is { } latest && _shown == IslandState.Expanded)
                        ShowContent(latest.Current);
                });
            }
            else if (!_swapPending)
            {
                // Same kind (e.g. volume 40% -> 45%): update in place, no crossfade.
                ShowContent(current);
                MarkLatency(current);
            }
        }
        _shown = state;
        _shownKind = kind;
    }

    // Selects the view for the event's kind and pushes its payload. UI thread only.
    private void ShowContent(IslandEvent? e)
    {
        _shownEvent = e;
        switch (e?.Kind)
        {
            case IslandKind.Volume:
                _volumeView.SetSnapshot(e.Payload as VolumeInfo);
                ContentHost.Content = _volumeView;
                break;
            case IslandKind.Brightness:
                _brightnessView.SetSnapshot(e.Payload as BrightnessInfo);
                ContentHost.Content = _brightnessView;
                break;
            case IslandKind.Media:
                _mediaView.SetSnapshot(e.Payload as MediaSnapshot);
                ContentHost.Content = _mediaView;
                break;
            case IslandKind.Battery:
                _batteryView.SetSnapshot(e.Payload as PowerSnapshot);
                ContentHost.Content = _batteryView;
                break;
            default:
                // No event, or a kind without a view (Notification, Timer): empty fallback.
                ContentHost.Content = null;
                break;
        }
    }

    [Conditional("DEBUG")]
    private static void MarkLatency(IslandEvent? e)
    {
#if DEBUG
        Diagnostics.LatencyProbe.Measure(e);
#endif
    }
}
