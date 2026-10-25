using NotchWindows.Core;

namespace NotchWindows.Core.Tests;

public sealed class ManualClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => Now += by;
}
