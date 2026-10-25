namespace NotchWindows.Core;

// Duration == null means sticky.
public sealed record IslandEvent(
    IslandKind Kind,
    int Priority,
    TimeSpan? Duration,
    string? Key = null,
    object? Payload = null);
