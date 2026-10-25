namespace NotchWindows.Providers;

public sealed record MediaSnapshot(string Title, string Artist, bool IsPlaying, byte[]? Thumbnail);

public interface IMediaSessionSource
{
    /// <summary>Latest cached state of the current media session, or null when no session exists.</summary>
    MediaSnapshot? Current { get; }
    event Action? Changed;
    Task TogglePlayPauseAsync();
    Task SkipNextAsync();
}
