using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using NotchWindows.App.Animation;
using NotchWindows.App.ViewModels;
using NotchWindows.Core;

namespace NotchWindows.App.Shell;

public partial class PillView : UserControl
{
    private readonly PillAnimator _animator;
    private IslandViewModel? _vm;
    private IslandState _shown = IslandState.Idle;
    private IslandKind? _shownKind;

    public PillView()
    {
        InitializeComponent();
        _animator = new PillAnimator(Shape, ContentText);
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
    }

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
            ContentText.Text = Describe(vm);
            _animator.Animate(_shown, state);
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
