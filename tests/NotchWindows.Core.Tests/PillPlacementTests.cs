namespace NotchWindows.Core.Tests;

public class PillPlacementTests
{
    [Fact]
    public void CenterLeft_CentersPillOnScreen()
    {
        Assert.Equal(870, PillPlacement.CenterLeft(0, 1920, 180));
    }

    [Fact]
    public void CenterLeft_NonZeroScreenLeft_OffsetsCorrectly()
    {
        Assert.Equal(-1920 + 870, PillPlacement.CenterLeft(-1920, 1920, 180));
    }
}
