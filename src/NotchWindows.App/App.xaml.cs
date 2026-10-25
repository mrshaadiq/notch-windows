using System.Diagnostics;
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
    private Bootstrap? _bootstrap;
    private SerilogTraceListener? _traceListener;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NotchWindows", "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _loggerFactory = Logging.Create();
        _logger = _loggerFactory.CreateLogger<App>();

        // Route Trace (e.g. AppSettingsStore load warnings) to Serilog. Before settings load.
        _traceListener = new SerilogTraceListener(Serilog.Log.Logger);
        Trace.Listeners.Add(_traceListener);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _logger.LogInformation("Notch started");

        _settings = AppSettingsStore.Load(SettingsPath);

        _pill = new Shell.PillWindow();
        // Attaches the view model with the loaded settings, shows the pill, then starts providers.
        _bootstrap = Bootstrap.Start(this, _pill, _settings, _loggerFactory);

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
        // Providers stop before their sources are disposed (Bootstrap.Dispose).
        _bootstrap?.Dispose();
        _bootstrap = null;
        if (_traceListener is not null)
        {
            Trace.Listeners.Remove(_traceListener);
            _traceListener = null;
        }
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
