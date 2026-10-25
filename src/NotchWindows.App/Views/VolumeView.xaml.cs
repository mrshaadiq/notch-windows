using NotchWindows.Providers;

namespace NotchWindows.App.Views;

public partial class VolumeView : System.Windows.Controls.UserControl
{
    public VolumeView()
    {
        InitializeComponent();
    }

    /// <summary>Shows the snapshot. Must be called on the UI thread.</summary>
    public void SetSnapshot(VolumeInfo? info)
    {
        if (info is null)
        {
            LabelText.Text = "";
            PercentText.Text = "";
            FillScale.ScaleX = 0;
            return;
        }

        LabelText.Text = info.IsMuted ? "Bisu" : "Volume";
        PercentText.Text = $"{info.Percent}%";
        FillScale.ScaleX = info.Percent / 100.0;
        // Muted keeps the level visible but dimmed.
        Fill.Opacity = info.IsMuted ? 0.35 : 1;
    }
}
