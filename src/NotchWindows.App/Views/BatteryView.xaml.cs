using NotchWindows.Providers;

namespace NotchWindows.App.Views;

public partial class BatteryView : System.Windows.Controls.UserControl
{
    public BatteryView()
    {
        InitializeComponent();
    }

    /// <summary>Shows the snapshot. Must be called on the UI thread.</summary>
    public void SetSnapshot(PowerSnapshot? snapshot)
    {
        StateText.Text = snapshot is null ? "" : snapshot.IsCharging ? "Mengisi daya" : "Baterai";
        // Unknown percentage shows nothing at all.
        PercentText.Text = snapshot?.Percent is int p ? $"{p}%" : "";
    }
}
