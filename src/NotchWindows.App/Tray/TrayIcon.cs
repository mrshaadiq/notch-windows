using System.Drawing;
using System.Windows.Forms;

namespace NotchWindows.App.Tray;

// Owns the notification-area icon and its context menu. Calls back into the App for toggle and exit.
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _idleItem;
    private readonly Action<bool> _onIdleBarToggled;
    private readonly Action _onExit;
    private bool _idleBarEnabled;
    private bool _disposed;

    public TrayIcon(bool idleBarEnabled, Action<bool> onIdleBarToggled, Action onExit)
    {
        ArgumentNullException.ThrowIfNull(onIdleBarToggled);
        ArgumentNullException.ThrowIfNull(onExit);
        _idleBarEnabled = idleBarEnabled;
        _onIdleBarToggled = onIdleBarToggled;
        _onExit = onExit;

        _idleItem = new ToolStripMenuItem(IdleLabel(idleBarEnabled));
        _idleItem.Click += OnIdleClick;
        var exitItem = new ToolStripMenuItem("Keluar");
        exitItem.Click += OnExitClick;

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_idleItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Notch",
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    private static string IdleLabel(bool enabled) => $"Idle bar: {(enabled ? "Aktif" : "Nonaktif")}";

    private void OnIdleClick(object? sender, EventArgs e)
    {
        _idleBarEnabled = !_idleBarEnabled;
        _idleItem.Text = IdleLabel(_idleBarEnabled);
        _onIdleBarToggled(_idleBarEnabled);
    }

    private void OnExitClick(object? sender, EventArgs e) => _onExit();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _idleItem.Click -= OnIdleClick;
        _icon.Visible = false; // remove from the notification area immediately
        _icon.Dispose();
        _menu.Dispose();
    }
}
