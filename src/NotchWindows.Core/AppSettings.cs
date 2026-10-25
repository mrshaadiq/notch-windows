namespace NotchWindows.Core;

public sealed record AppSettings(
    bool IdleBarEnabled = true,
    double IdleBarWidth = 180,
    double IdleBarHeight = 28,
    bool HideInFullscreen = true);
