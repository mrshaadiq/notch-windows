namespace NotchWindows.Core;

public interface IClock
{
    DateTimeOffset Now { get; }
}
