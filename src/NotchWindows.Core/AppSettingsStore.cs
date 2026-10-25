using System.Diagnostics;
using System.Text.Json;

namespace NotchWindows.Core;

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return Validate(JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Trace.TraceWarning($"Failed to load settings from '{path}': {ex.Message}");
            return new AppSettings();
        }
    }

    // Max idle bar size is the pill window size (PillWindow.xaml: 400 x 104).
    private const double MaxIdleBarWidth = 400;
    private const double MaxIdleBarHeight = 104;

    // Hand-edited files can hold any number; the animator must never get a size it cannot apply.
    private static AppSettings Validate(AppSettings s)
    {
        var defaults = new AppSettings();
        return s with
        {
            IdleBarWidth = Sanitize(s.IdleBarWidth, defaults.IdleBarWidth, MaxIdleBarWidth),
            IdleBarHeight = Sanitize(s.IdleBarHeight, defaults.IdleBarHeight, MaxIdleBarHeight),
        };
    }

    private static double Sanitize(double value, double fallback, double max) =>
        !double.IsFinite(value) || value <= 0 ? fallback : Math.Min(value, max);

    public static void Save(string path, AppSettings settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
