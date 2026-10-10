using System.Collections.Generic;
using System.Drawing;
using Livestream.Monitor.Direct2D.Widgets;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class VerticalScrollBarShould
{
    // A 216-pixel bar: 16-pixel arrow buttons at each end around a 184-pixel track.
    private static readonly RectangleF Area = new(x: 0, y: 0, width: VerticalScrollBar.Width, height: 216);

    // Beyond the longest Windows keyboard repeat delay (one second).
    private const long AfterRepeatDelay = 1001;

    [Theory]
    [InlineData(1, 4, 3)]
    [InlineData(16, 64, 48)]
    public void StepOneLineOnPressAndRepeatOnlyAfterTheDelay(int lineSize, int firstPosition, int secondPosition)
    {
        // Rows step by one; the tile pages' pixel offsets step by WPF's 16-pixel scroll viewer line.
        var positions = new List<int>();
        var bar = new VerticalScrollBar(positions.Add);
        var offset = 5 * lineSize;
        bar.Update(Area, extent: 100 * lineSize, viewport: 10 * lineSize, offset, lineSize);

        Assert.True(bar.MouseDown(new PointF(7, 8), now: 0));
        Assert.False(bar.Repeat(now: 1));
        Assert.True(bar.Repeat(now: AfterRepeatDelay));
        Assert.Equal([firstPosition, secondPosition], positions);
    }

    [Fact]
    public void PageTowardsThePointerUntilTheThumbReachesIt()
    {
        var positions = new List<int>();
        var bar = new VerticalScrollBar(positions.Add);
        bar.Update(Area, extent: 100, viewport: 10, offset: 0);
        var belowThumb = new PointF(7, 150);

        bar.MouseDown(belowThumb, now: 0);
        bar.Update(Area, extent: 100, viewport: 10, offset: 70);

        Assert.False(bar.Repeat(now: AfterRepeatDelay));
        Assert.Equal([10], positions);
    }

    [Fact]
    public void DragTheThumbInProportionToItsTravel()
    {
        var positions = new List<int>();
        var bar = new VerticalScrollBar(positions.Add);
        bar.Update(Area, extent: 100, viewport: 10, offset: 0);

        // The thumb, at its 25-pixel minimum, travels 159 pixels over the 90 scrollable positions.
        bar.MouseDown(new PointF(7, 20), now: 0);
        bar.MouseMove(new PointF(7, 20 + 79.5f));
        bar.MouseUp();

        Assert.Equal([45], positions);
        Assert.False(bar.IsInteracting);
    }

    [Fact]
    public void StayHiddenAndIgnorePressesWhileTheContentFits()
    {
        var bar = new VerticalScrollBar(_ => Assert.Fail("A hidden bar must not scroll."));
        bar.Update(Area, extent: 10, viewport: 10, offset: 0);

        Assert.False(bar.IsVisible);
        Assert.False(bar.MouseDown(new PointF(7, 8), now: 0));
    }
}
