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
    // Bumped on every state change and Configure: a crossfade swap from an older generation is dropped.
    private int _swapGen;
    private AppSettings _settings = new();
    private readonly DispatcherTimer _hideTimer;

    // One view instance per kind. Notification and Timer have no view in this phase.
    private readonly VolumeView _volumeView = new();
    private readonly BrightnessView _brightnessView = new();
    private readonly MediaView _mediaView = new();
    private readonly BatteryView _batteryView = new();

    // Compact: one line of ambient status (media glyph and title).
    private readonly TextBlock _compactView = new()
    {
        Foreground = System.Windows.Media.Brushes.White,
        FontSize = 12,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public PillView()
    {
        InitializeComponent();
        _animator = new PillAnimator(Shape, ContentHost, new AppSettings());
        _animator.Snap(IslandState.Idle);
        _hideTimer = new DispatcherTimer { Interval = PillAnimator.CollapseDuration + TimeSpan.FromMilliseconds(50) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); ApplyIdleVisibility(); };
        DataContextChanged += OnDataContextChanged;
        Shape.MouseEnter += (_, _) => _vm?.SetPointerOver(true);
        Shape.MouseLeave += (_, _) => _vm?.SetPointerOver(false);
        Unloaded += (_, _) => { _hideTimer.Stop(); Detach(); };
    }

    // Idle bar size comes from settings. Call before setting DataContext.
    public void Configure(AppSettings settings)
    {
        _settings = settings;
        _swapGen++;
        _swapPending = false;
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
        _swapGen++;
        _swapPending = false;
        if (_shown == IslandState.Compact)
            ShowCompact(_vm);
        else
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
            // A pending crossfade belongs to the old state.
            _swapGen++;
            _swapPending = false;
            var gen = _swapGen;
            // Leaving Expanded: keep the old content so it does not flash while it fades out.
            if (state == IslandState.Expanded)
            {
                ShowContent(current);
                MarkLatency(current);
            }
            // Compact from Idle: content is hidden, swap now. From Expanded: swap once the old content faded out.
            else if (state == IslandState.Compact && _shown == IslandState.Idle)
            {
                ShowCompact(vm);
            }
            Action? contentHidden = state == IslandState.Compact && _shown == IslandState.Expanded
                ? () => { if (gen == _swapGen && _vm is { } latest) ShowCompact(latest); }
                : null;
            // Show before animating; when collapsing to a hidden idle, hide after the animation.
            Shape.Visibility = Visibility.Visible;
            _animator.Animate(_shown, state, contentHidden);
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
                var gen = _swapGen;
                _animator.Crossfade(() =>
                {
                    if (gen != _swapGen)
                        return;
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
        else if (state == IslandState.Compact && ContentHost.Content == _compactView)
        {
            // Ambient changed while Compact (e.g. next track). Not while the old content is still fading out.
            ShowCompact(vm);
        }
        _shown = state;
        _shownKind = kind;
        // Play/pause can change without a new event (e.g. after a click): keep the glyph in sync.
        if (ContentHost.Content == _mediaView)
            _mediaView.SetPlaying(IsMediaPlaying(vm));
    }

    private static bool IsMediaPlaying(IslandViewModel vm) =>
        vm.Ambient.TryGetValue(IslandKind.Media, out var a) && a is MediaSnapshot { IsPlaying: true };

    // Shows the ambient line for Compact. UI thread only.
    private void ShowCompact(IslandViewModel vm)
    {
        _shownEvent = null;
        _compactView.Text = vm.Ambient.TryGetValue(IslandKind.Media, out var a) && a is MediaSnapshot media
            ? (string.IsNullOrEmpty(media.Title) ? "♪" : $"♪  {media.Title}")
            : "";
        ContentHost.Content = _compactView;
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
                if (_vm is { } vm)
                    _mediaView.SetPlaying(IsMediaPlaying(vm));
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
