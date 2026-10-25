using NotchWindows.Core;

namespace NotchWindows.Core.Tests;

public class EventPriorityTests
{
    [Fact]
    public void Timer_IsHighest_And_Media_IsLowest()
    {
        Assert.True(EventPriority.Timer > EventPriority.Notification);
        Assert.True(EventPriority.Notification > EventPriority.Battery);
        Assert.True(EventPriority.Battery > EventPriority.Volume);
        Assert.Equal(EventPriority.Volume, EventPriority.Brightness);
        Assert.True(EventPriority.Volume > EventPriority.Media);
    }
}
