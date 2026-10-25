namespace NotchWindows.Providers;

public interface IVolumeSource
{
    /// <summary>Master volume scalar, 0.0 to 1.0.</summary>
    float Level { get; }

    bool IsMuted { get; }

    event Action? Changed;
}
