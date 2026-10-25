namespace NotchWindows.Core;

// Payload == null clears the ambient entry for Kind.
public sealed record AmbientUpdate(IslandKind Kind, object? Payload);
