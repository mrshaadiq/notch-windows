using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace NotchWindows.App;

public partial class App : Application
{
    private ILoggerFactory? _loggerFactory;
    private ILogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _loggerFactory = Logging.Create();
        _logger = _loggerFactory.CreateLogger<App>();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _logger.LogInformation("Notch started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Notch exiting");
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
