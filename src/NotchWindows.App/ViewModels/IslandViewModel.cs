using System.ComponentModel;
using System.Windows.Threading;
using NotchWindows.Core;

namespace NotchWindows.App.ViewModels;

// Must be constructed on the UI thread (captures its Dispatcher unless one is supplied).
public sealed class IslandViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IslandController _controller;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public IslandViewModel(IslandController controller, Dispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _controller.StateChanged += OnStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IslandState State => _controller.State;

    public IslandEvent? Current => _controller.Current;

    public IReadOnlyDictionary<IslandKind, object?> Ambient => _controller.Ambient;

    // The controller only signals state transitions. Call after Publish/ApplyAmbient/Tick/Dismiss
    // when Current or Ambient may have changed without a state change (e.g. volume -> brightness).
    public void Refresh() => RaiseAll();

    // Mutations go through these wrappers so Refresh always follows the controller call
    // (Current can change without a state change, and the crossfade keys on Current).
    // Call on the UI thread (the EventBus dispatcher and the tick timer both do).
    public void Publish(IslandEvent e)
    {
        _controller.Publish(e);
        Refresh();
    }

    public void ApplyAmbient(AmbientUpdate u)
    {
        _controller.ApplyAmbient(u);
        Refresh();
    }

    public void Tick()
    {
        _controller.Tick();
        Refresh();
    }

    public void Dismiss()
    {
        _controller.Dismiss();
        Refresh();
    }

    // Pointer over the pill pauses auto-collapse.
    public void SetPointerOver(bool over)
    {
        _controller.SetPointerOver(over);
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _controller.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, IslandState state) => RaiseAll();

    private void RaiseAll()
    {
        if (_dispatcher.CheckAccess())
            Raise();
        else
            _dispatcher.BeginInvoke(Raise);
    }

    private void Raise()
    {
        if (_disposed)
            return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ambient)));
        // Current last: views key their reaction on it.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
    }
}
