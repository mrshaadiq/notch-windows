using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;
using NotchWindows.App.Animation;
using NotchWindows.App.ViewModels;
using NotchWindows.Core;

namespace NotchWindows.App.Shell;

public partial class PillView : UserControl
{
    private PillAnimator _animator;
    private IslandViewModel? _vm;
    private IslandState _shown = IslandState.Idle;
    private IslandKind? _shownKind;
    private AppSettings _settings = new();
    private readonly DispatcherTimer _hideTimer;

    public PillView()
    {
        InitializeComponent();
        _animator = new PillAnimator(Shape, ContentText, new AppSettings());
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
        _animator = new PillAnimator(Shape, ContentText, settings);
        _animator.Snap(_shown);
        _hideTimer.Stop();
        ApplyIdleVisibility();
    }

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
        ContentText.Text = Describe(_vm);
        _animator.Snap(_shown);
        ApplyIdleVisibility();
    }

    private void Detach()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelChanged;
        _vm = null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // State, Ambient and Current are raised together, Current last; react once, on Current.
        if (_vm is null || e.PropertyName != nameof(IslandViewModel.Current))
            return;

        var vm = _vm;
        var state = vm.State;
        var kind = vm.Current?.Kind;
        if (state != _shown)
        {
            // Leaving Expanded: keep the old text so it does not flash while the content fades out.
            if (state == IslandState.Expanded)
                ContentText.Text = Describe(vm);
            // Show before animating; when collapsing to a hidden idle, hide after the animation.
            Shape.Visibility = Visibility.Visible;
            _animator.Animate(_shown, state);
            _hideTimer.Stop();
            if (state == IslandState.Idle && !_settings.IdleBarEnabled)
                _hideTimer.Start();
        }
        else if (state == IslandState.Expanded && kind != _shownKind)
        {
            _animator.Crossfade(() => ContentText.Text = Describe(vm));
        }
        _shown = state;
        _shownKind = kind;
    }

    // Placeholder content until per-kind views exist.
    private static string Describe(IslandViewModel vm) =>
        $"{vm.State} / {vm.Current?.Kind.ToString() ?? "none"}";
}
