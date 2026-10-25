namespace NotchWindows.Core;

public enum ProviderStatus { Stopped, Running, Unavailable, Disabled }

public interface IEventProvider
{
    string Name { get; }
    ProviderStatus Status { get; }
    event Action<IslandEvent>? Raised;
    event Action<AmbientUpdate>? AmbientChanged;
    event Action<Exception>? Faulted;
    void Start();
    void Stop();
}
