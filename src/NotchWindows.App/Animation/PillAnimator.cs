using System.Windows;
using System.Windows.Controls;
using Size = System.Windows.Size;
using System.Windows.Media.Animation;
using NotchWindows.Core;

namespace NotchWindows.App.Animation;

public sealed class PillAnimator
{
    public static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ContentFadeInDelay = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan ContentFadeInDuration = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan CollapseDuration = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan ContentFadeOutDuration = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan CrossfadeDuration = TimeSpan.FromMilliseconds(120);

    private readonly Border _shape;
    private readonly FrameworkElement _content;
    private readonly AppSettings _settings;
    private Storyboard? _active;

    public PillAnimator(Border shape, FrameworkElement content, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(content);
        _shape = shape;
        ArgumentNullException.ThrowIfNull(settings);
        _content = content;
        _settings = settings;
    }

    // Idle comes from settings; Compact and Expanded are fixed.
    public Size SizeFor(IslandState state) => state switch
    {
        IslandState.Idle => new Size(_settings.IdleBarWidth, _settings.IdleBarHeight),
        IslandState.Compact => new Size(260, 32),
        _ => new Size(380, 96),
    };

    // Sets the pill to a state without animating (initial layout).
    public void Snap(IslandState state)
    {
        _active?.Stop(_shape);
        _active = null;
        var size = SizeFor(state);
        _shape.BeginAnimation(FrameworkElement.WidthProperty, null);
        _shape.BeginAnimation(FrameworkElement.HeightProperty, null);
        _shape.Width = size.Width;
        _shape.Height = size.Height;
        _content.BeginAnimation(UIElement.OpacityProperty, null);
        _content.Opacity = state == IslandState.Expanded ? 1 : 0;
    }

    public void Animate(IslandState from, IslandState to)
    {
        if (from == to)
            return;

        var source = SizeFor(from);
        var target = SizeFor(to);
        var growing = target.Width * target.Height > source.Width * source.Height;
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
        var sb = new Storyboard();

        if (to == IslandState.Expanded)
        {
            AddSize(sb, target, ExpandDuration, TimeSpan.Zero, easeOut);
            AddOpacity(sb, 1, ContentFadeInDuration, ContentFadeInDelay);
        }
        else if (from == IslandState.Expanded)
        {
            // Content fades out first, then the shape shrinks.
            AddOpacity(sb, 0, ContentFadeOutDuration, TimeSpan.Zero);
            AddSize(sb, target, CollapseDuration, ContentFadeOutDuration, easeIn);
        }
        else if (growing)
        {
            AddSize(sb, target, ExpandDuration, TimeSpan.Zero, easeOut);
        }
        else
        {
            AddSize(sb, target, CollapseDuration, TimeSpan.Zero, easeIn);
        }

        // No From values and SnapshotAndReplace: an interrupted animation continues from its current value.
        _active = sb;
        sb.Begin(_shape, HandoffBehavior.SnapshotAndReplace, true);
    }

    // Event swap while Expanded: shape stays; content fades out, swap runs, content fades in (120 ms total).
    public void Crossfade(Action swap)
    {
        ArgumentNullException.ThrowIfNull(swap);

        var half = TimeSpan.FromTicks(CrossfadeDuration.Ticks / 2);
        var fadeOut = new DoubleAnimation(0, half);
        fadeOut.Completed += (_, _) =>
        {
            swap();
            _content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, half), HandoffBehavior.SnapshotAndReplace);
        };
        _content.BeginAnimation(UIElement.OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    private void AddSize(Storyboard sb, Size target, TimeSpan duration, TimeSpan begin, IEasingFunction ease)
    {
        var w = new DoubleAnimation(target.Width, duration) { BeginTime = begin, EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        Storyboard.SetTarget(w, _shape);
        Storyboard.SetTargetProperty(w, new PropertyPath(FrameworkElement.WidthProperty));
        var h = new DoubleAnimation(target.Height, duration) { BeginTime = begin, EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        Storyboard.SetTarget(h, _shape);
        Storyboard.SetTargetProperty(h, new PropertyPath(FrameworkElement.HeightProperty));
        sb.Children.Add(w);
        sb.Children.Add(h);
    }

    private void AddOpacity(Storyboard sb, double to, TimeSpan duration, TimeSpan begin)
    {
        var o = new DoubleAnimation(to, duration) { BeginTime = begin, FillBehavior = FillBehavior.HoldEnd };
        Storyboard.SetTarget(o, _content);
        Storyboard.SetTargetProperty(o, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(o);
    }
}
