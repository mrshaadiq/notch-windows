using NotchWindows.Native;
using NotchWindows.Providers;

namespace NotchWindows.Core.Tests;

public class PowerStatusSourceTests
{
    [Fact]
    public void Interpret_UnknownFlag_KeepsPreviousReading()
    {
        var previous = new PowerSnapshot(true, 64, false);

        var result = PowerStatusSource.Interpret(acLineStatus: 255, batteryFlag: 255, batteryLifePercent: 255, previous);

        Assert.Same(previous, result);
    }

    [Fact]
    public void Interpret_UnknownFlagWithoutPrevious_IsNotNoBattery()
    {
        var result = PowerStatusSource.Interpret(acLineStatus: 1, batteryFlag: 255, batteryLifePercent: 255, previous: null);

        Assert.True(result.HasBattery);
        Assert.Null(result.Percent);
        Assert.True(result.IsCharging);
    }

    [Fact]
    public void Interpret_NoBatteryFlag_ReportsNoBattery()
    {
        var result = PowerStatusSource.Interpret(acLineStatus: 1, batteryFlag: 128, batteryLifePercent: 255, new PowerSnapshot(true, 50, true));

        Assert.False(result.HasBattery);
    }

    [Fact]
    public void Interpret_KnownFlag_ReadsCurrentValues()
    {
        var result = PowerStatusSource.Interpret(acLineStatus: 0, batteryFlag: 1, batteryLifePercent: 80, new PowerSnapshot(true, 50, true));

        Assert.Equal(new PowerSnapshot(true, 80, false), result);
    }
}
