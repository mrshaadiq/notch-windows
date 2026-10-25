using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NotchWindows.Providers;

namespace NotchWindows.App.Views;

public partial class MediaView : System.Windows.Controls.UserControl
{
    private Func<Task>? _togglePlayPause;
    private Func<Task>? _skipNext;
    private int _renderVersion;

    public MediaView()
    {
        InitializeComponent();
    }

    /// <summary>Supplies the control actions (e.g. SmtcMediaSource.TogglePlayPauseAsync / SkipNextAsync).</summary>
    public void Configure(Func<Task> togglePlayPause, Func<Task> skipNext)
    {
        _togglePlayPause = togglePlayPause;
        _skipNext = skipNext;
    }

    /// <summary>Shows the snapshot. Must be called on the UI thread.</summary>
    public void SetSnapshot(MediaSnapshot? snapshot)
    {
        TitleText.Text = snapshot?.Title ?? "";
        ArtistText.Text = snapshot?.Artist ?? "";
        PlayPauseButton.Content = snapshot is { IsPlaying: true } ? "⏸" : "▶";
        _ = SetThumbnailAsync(snapshot?.Thumbnail);
    }

    private async Task SetThumbnailAsync(byte[]? bytes)
    {
        int version = Interlocked.Increment(ref _renderVersion);
        if (bytes is not { Length: > 0 })
        {
            ThumbnailImage.Source = null;
            ThumbnailImage.Visibility = Visibility.Collapsed;
            return;
        }

        // Decode off the UI thread; a frozen BitmapImage can cross threads.
        BitmapImage? image = await Task.Run(() =>
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 80;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        });

        if (version != Volatile.Read(ref _renderVersion)) return; // superseded
        ThumbnailImage.Source = image;
        ThumbnailImage.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e) => await InvokeAsync(_togglePlayPause);
    private async void OnNextClick(object sender, RoutedEventArgs e) => await InvokeAsync(_skipNext);

    private static async Task InvokeAsync(Func<Task>? action)
    {
        if (action == null) return;
        try { await action(); }
        catch { /* control failures must not crash the UI */ }
    }
}
