using System.IO;
using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NotchWindows.Core;
using NotchWindows.Native;

namespace NotchWindows.App;

public partial class App : Application
{
    private ILoggerFactory? _loggerFactory;
    private ILogger? _logger;
    private Shell.PillWindow? _pill;
    private Tray.TrayIcon? _tray;
    private AppSettings _settings = new();
    private DispatcherTimer? _fullscreenTimer;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NotchWindows", "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _loggerFactory = Logging.Create();
        _logger = _loggerFactory.CreateLogger<App>();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _logger.LogInformation("Notch started");

        _settings = AppSettingsStore.Load(SettingsPath);

        _pill = new Shell.PillWindow();
        _pill.ApplySettings(_settings);
        _pill.Show();

        _tray = new Tray.TrayIcon(_settings.IdleBarEnabled, SetIdleBarEnabled, ExitApp);

        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
        _fullscreenTimer.Start();
    }

    private void CheckFullscreen()
    {
        if (_pill is null) return;
        bool shouldHide = _settings.HideInFullscreen && FullscreenDetector.IsBusy();
        if (shouldHide && _pill.IsVisible)
            _pill.Hide();
        else if (!shouldHide && !_pill.IsVisible)
            _pill.Show();
    }

    private void SetIdleBarEnabled(bool enabled)
    {
        _settings = _settings with { IdleBarEnabled = enabled };
        _pill?.ApplySettings(_settings);
        try
        {
            AppSettingsStore.Save(SettingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to save settings to {Path}", SettingsPath);
        }
    }

    private void ExitApp()
    {
        DisposeTray();
        Shutdown();
    }

    private void DisposeTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Notch exiting");
        _fullscreenTimer?.Stop();
        _fullscreenTimer = null;
        DisposeTray();
        _loggerFactory?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled dispatcher exception");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled domain exception (terminating: {IsTerminating})", e.IsTerminating);
    }
}
