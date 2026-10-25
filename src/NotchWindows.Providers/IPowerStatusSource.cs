namespace NotchWindows.Providers;

/// <summary>Percent is null when the OS reports the level as unknown.</summary>
public sealed record PowerSnapshot(bool HasBattery, int? Percent, bool IsCharging);

public interface IPowerStatusSource
{
    PowerSnapshot Read();
    event Action? Changed;
}
