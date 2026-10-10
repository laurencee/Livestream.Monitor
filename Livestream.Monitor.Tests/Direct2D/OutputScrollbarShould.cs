using System;
using System.Collections.Generic;
using System.Drawing;
using Livestream.Monitor.Direct2D.Windows.Output;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class OutputScrollbarShould
{
    private static readonly RectangleF Bounds = new(x: 0, y: 0, width: OutputScrollbar.Width, height: 200);

    [Fact]
    public void RepeatArrowScrollingUntilReleased()
    {
        var positions = new List<int>();
        var scrollbar = new OutputScrollbar(positions.Add);
        scrollbar.Update(Bounds, lines: 100, visible: 20, first: 40);

        Assert.True(scrollbar.MouseDown(new PointF(8, 5)));
        Assert.True(scrollbar.IsInteracting);
        var afterRepeatDelay = Environment.TickCount64 + 1000;
        scrollbar.Repeat(afterRepeatDelay);
        scrollbar.MouseUp();
        scrollbar.Repeat(afterRepeatDelay + 1000);

        Assert.Equal(new[] { 39, 38 }, positions);
        Assert.False(scrollbar.IsInteracting);
    }

    [Fact]
    public void PageThroughTheTrackWithoutPassingTheLastPage()
    {
        var positions = new List<int>();
        var scrollbar = new OutputScrollbar(positions.Add);
        scrollbar.Update(Bounds, lines: 100, visible: 20, first: 70);

        Assert.True(scrollbar.MouseDown(new PointF(8, 181)));
        scrollbar.MouseUp();
        scrollbar.Update(Bounds, lines: 100, visible: 20, first: 80);
        scrollbar.MouseDown(new PointF(8, 195));

        Assert.Equal(new[] { 80 }, positions);
    }

    [Fact]
    public void DragTheThumbToBothEndsAndStopWhenReleased()
    {
        var positions = new List<int>();
        var scrollbar = new OutputScrollbar(positions.Add);
        scrollbar.Update(Bounds, lines: 100, visible: 20, first: 40);
        scrollbar.MouseDown(new PointF(8, 100));

        scrollbar.MouseMove(new PointF(8, 300));
        scrollbar.MouseMove(new PointF(8, -100));
        scrollbar.MouseUp();
        scrollbar.MouseMove(new PointF(8, 300));

        Assert.Equal(new[] { 80, 0 }, positions);
        Assert.False(scrollbar.IsInteracting);
    }

    [Fact]
    public void LeaveContentThatFitsInTheViewportAtTheStart()
    {
        var positions = new List<int>();
        var scrollbar = new OutputScrollbar(positions.Add);
        scrollbar.Update(Bounds, lines: 3, visible: 20, first: 0);

        Assert.True(scrollbar.MouseDown(new PointF(8, 195)));
        scrollbar.Repeat(Environment.TickCount64 + 1000);
        Assert.False(scrollbar.MouseDown(new PointF(30, 195)));
        Assert.False(scrollbar.IsInteracting);
        Assert.Empty(positions);
    }
}
