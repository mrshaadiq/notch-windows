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
    // Bumped by Animate, Snap and Crossfade: a stale fade-out's Completed must not swap or fade in.
    private int _crossfadeToken;

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
        _crossfadeToken++;
        _active?.Stop(_shape);
        _active = null;
        var size = SizeFor(state);
        _shape.BeginAnimation(FrameworkElement.WidthProperty, null);
        _shape.BeginAnimation(FrameworkElement.HeightProperty, null);
        _shape.Width = size.Width;
        _shape.Height = size.Height;
        _content.BeginAnimation(UIElement.OpacityProperty, null);
        // Compact shows the ambient line, so only Idle hides the content.
        _content.Opacity = state == IslandState.Idle ? 0 : 1;
    }

    // contentHidden runs once the content has faded out on Expanded -> Compact, so the caller can
    // swap in the ambient content before it fades back in. Skipped if superseded.
    public void Animate(IslandState from, IslandState to, Action? contentHidden = null)
    {
        if (from == to)
            return;

        var token = ++_crossfadeToken;
        var source = SizeFor(from);
        var target = SizeFor(to);
        var growing = target.Width * target.Height > source.Width * source.Height;
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
        var sb = new Storyboard();

        if (to == IslandState.Expanded)
        {
            AddSize(sb, target, ExpandDuration, TimeSpan.Zero, easeOut);
            if (from == IslandState.Compact)
                AddOpacityFromZero(sb, ContentFadeInDuration, ContentFadeInDelay); // ambient line is visible; hide it first
            else
                AddOpacity(sb, 1, ContentFadeInDuration, ContentFadeInDelay);
        }
        else if (from == IslandState.Expanded)
        {
            // Content fades out first, then the shape shrinks; Compact fades its ambient line back in.
            var fadeOut = AddOpacity(sb, 0, ContentFadeOutDuration, TimeSpan.Zero);
            AddSize(sb, target, CollapseDuration, ContentFadeOutDuration, easeIn);
            if (to == IslandState.Compact)
            {
                if (contentHidden is not null)
                    fadeOut.Completed += (_, _) => { if (token == _crossfadeToken) contentHidden(); };
                AddOpacity(sb, 1, ContentFadeInDuration, ContentFadeOutDuration + CollapseDuration);
            }
        }
        else
        {
            // Idle <-> Compact: the Idle size is configurable, so either can be the larger one.
            AddSize(sb, target, growing ? ExpandDuration : CollapseDuration, TimeSpan.Zero, growing ? easeOut : easeIn);
            if (to == IslandState.Compact)
                AddOpacity(sb, 1, ContentFadeInDuration, ContentFadeInDelay);
            else
                AddOpacity(sb, 0, ContentFadeOutDuration, TimeSpan.Zero);
        }

        // No From values and SnapshotAndReplace: an interrupted animation continues from its current value.
        _active = sb;
        sb.Begin(_shape, HandoffBehavior.SnapshotAndReplace, true);
    }

    // Event swap while Expanded: shape stays; content fades out, swap runs, content fades in (120 ms total).
    public void Crossfade(Action swap)
    {
        ArgumentNullException.ThrowIfNull(swap);

        var token = ++_crossfadeToken;
        var half = TimeSpan.FromTicks(CrossfadeDuration.Ticks / 2);
        var fadeOut = new DoubleAnimation(0, half);
        fadeOut.Completed += (_, _) =>
        {
            if (token != _crossfadeToken)
                return;
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

    private DoubleAnimation AddOpacity(Storyboard sb, double to, TimeSpan duration, TimeSpan begin)
    {
        var o = new DoubleAnimation(to, duration) { BeginTime = begin, FillBehavior = FillBehavior.HoldEnd };
        Storyboard.SetTarget(o, _content);
        Storyboard.SetTargetProperty(o, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(o);
        return o;
    }

    // Opacity 0 at once, held through the delay, then fades in to 1.
    private void AddOpacityFromZero(Storyboard sb, TimeSpan duration, TimeSpan delay)
    {
        var o = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        o.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        o.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
        o.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay + duration)));
        Storyboard.SetTarget(o, _content);
        Storyboard.SetTargetProperty(o, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(o);
    }
}
