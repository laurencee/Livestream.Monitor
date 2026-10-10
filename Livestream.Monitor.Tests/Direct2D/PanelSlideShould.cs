using Livestream.Monitor.Direct2D.Widgets;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class PanelSlideShould
{
    [Fact]
    public void AdvanceBetweenSubMillisecondTimestamps()
    {
        var slide = new PanelSlide();
        slide.SetOpen(true, now: 0);
        slide.Tick(0.1);
        var first = slide.VisibleFraction;
        slide.Tick(0.9);

        Assert.True(slide.VisibleFraction > first);
    }

    [Fact]
    public void SlideOpenAndClosedAndStopRequestingFramesAtTheEndpoints()
    {
        var slide = new PanelSlide();
        slide.SetOpen(true, now: 0);
        slide.Tick(PanelSlide.DurationMilliseconds / 2d);
        Assert.InRange(slide.VisibleFraction, 0.01f, 0.99f);
        Assert.True(slide.IsAnimating);

        slide.Tick(PanelSlide.DurationMilliseconds);
        Assert.Equal(1, slide.VisibleFraction);
        Assert.False(slide.IsAnimating);
        slide.SetOpen(false, PanelSlide.DurationMilliseconds);
        slide.Tick(PanelSlide.DurationMilliseconds * 2);
        Assert.Equal(0, slide.VisibleFraction);
        Assert.False(slide.IsAnimating);
    }

    [Fact]
    public void ReverseWithoutJumpingToAnEndpoint()
    {
        var slide = new PanelSlide();
        slide.SetOpen(true, now: 0);
        var halfway = PanelSlide.DurationMilliseconds / 2d;
        slide.Tick(halfway);
        var beforeReverse = slide.VisibleFraction;

        slide.SetOpen(false, halfway);
        Assert.Equal(beforeReverse, slide.VisibleFraction);
        slide.Tick(PanelSlide.DurationMilliseconds);
        Assert.InRange(slide.VisibleFraction, 0, beforeReverse);
        Assert.True(slide.IsAnimating);
    }

    [Fact]
    public void StopImmediatelyWhenClosingBeforeAnyOpeningMovement()
    {
        var slide = new PanelSlide();
        slide.SetOpen(true, now: 0);
        slide.SetOpen(false, now: 0);

        Assert.Equal(0, slide.VisibleFraction);
        Assert.False(slide.IsAnimating);
    }
}
