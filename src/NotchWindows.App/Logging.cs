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
