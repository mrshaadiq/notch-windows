namespace NotchWindows.Core;

public static class PillPlacement
{
    public static double CenterLeft(double screenLeft, double screenWidth, double pillWidth)
        => screenLeft + (screenWidth - pillWidth) / 2;
}
