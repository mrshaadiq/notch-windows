namespace NotchWindows.Providers;

public interface IBrightnessSource
{
    /// <summary>Current internal panel brightness 0 to 100, or null when brightness control is not supported.</summary>
    int? Read();
}
