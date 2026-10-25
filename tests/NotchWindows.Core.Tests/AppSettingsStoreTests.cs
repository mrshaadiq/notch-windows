namespace NotchWindows.Core.Tests;

public class AppSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notch-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "sub", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        Assert.Equal(new AppSettings(), AppSettingsStore.Load(FilePath));
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json !!");

        Assert.Equal(new AppSettings(), AppSettingsStore.Load(FilePath));
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var s = new AppSettings(false, 200, 32, false);

        AppSettingsStore.Save(FilePath, s);

        Assert.Equal(s, AppSettingsStore.Load(FilePath));
    }

    [Fact]
    public void Load_NegativeIdleBarWidth_ReturnsDefaultWidth()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ \"IdleBarWidth\": -5, \"IdleBarHeight\": 0 }");

        var loaded = AppSettingsStore.Load(FilePath);

        Assert.Equal(180, loaded.IdleBarWidth);
        Assert.Equal(28, loaded.IdleBarHeight);
    }

    [Fact]
    public void Load_OversizedIdleBar_ClampsToWindowSize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ \"IdleBarWidth\": 5000, \"IdleBarHeight\": 1e300 }");

        var loaded = AppSettingsStore.Load(FilePath);

        Assert.Equal(400, loaded.IdleBarWidth);
        Assert.Equal(104, loaded.IdleBarHeight);
    }
}
