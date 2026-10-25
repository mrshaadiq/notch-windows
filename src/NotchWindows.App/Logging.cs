using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;

namespace NotchWindows.App;

public static class Logging
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NotchWindows", "logs");

    public static ILoggerFactory Create()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(LogDirectory, "notch-.log"),
                rollingInterval: RollingInterval.Day,
                shared: true)
            .CreateLogger();

        return new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger, dispose: true);
    }
}

// Forwards System.Diagnostics.Trace output (e.g. AppSettingsStore's TraceWarning) to Serilog.
public sealed class SerilogTraceListener(Serilog.ILogger logger) : System.Diagnostics.TraceListener
{
    public override void Write(string? message) => Emit(System.Diagnostics.TraceEventType.Verbose, message);

    public override void WriteLine(string? message) => Emit(System.Diagnostics.TraceEventType.Verbose, message);

    public override void TraceEvent(System.Diagnostics.TraceEventCache? eventCache, string source,
        System.Diagnostics.TraceEventType eventType, int id, string? message) => Emit(eventType, message);

    public override void TraceEvent(System.Diagnostics.TraceEventCache? eventCache, string source,
        System.Diagnostics.TraceEventType eventType, int id, string? format, params object?[]? args) =>
        Emit(eventType, format is not null && args is { Length: > 0 }
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args)
            : format);

    private void Emit(System.Diagnostics.TraceEventType type, string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        var level = type switch
        {
            System.Diagnostics.TraceEventType.Critical => Serilog.Events.LogEventLevel.Fatal,
            System.Diagnostics.TraceEventType.Error => Serilog.Events.LogEventLevel.Error,
            System.Diagnostics.TraceEventType.Warning => Serilog.Events.LogEventLevel.Warning,
            System.Diagnostics.TraceEventType.Information => Serilog.Events.LogEventLevel.Information,
            _ => Serilog.Events.LogEventLevel.Debug,
        };
        logger.Write(level, "[Trace] {TraceMessage}", message);
    }
}
