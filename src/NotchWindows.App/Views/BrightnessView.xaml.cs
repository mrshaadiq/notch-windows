using NotchWindows.Providers;

namespace NotchWindows.App.Views;

public partial class BrightnessView : System.Windows.Controls.UserControl
{
    public BrightnessView()
    {
        InitializeComponent();
    }

    /// <summary>Shows the snapshot. Must be called on the UI thread.</summary>
    public void SetSnapshot(BrightnessInfo? info)
    {
        PercentText.Text = info is null ? "" : $"{info.Percent}%";
        FillScale.ScaleX = info is null ? 0 : info.Percent / 100.0;
    }
}
