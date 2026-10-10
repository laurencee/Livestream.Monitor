using System;
using System.Drawing;
using Livestream.Monitor.Direct2D.Rendering;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class PixelSnapShould
{
    private const float Precision = 0.0001f;

    [Theory]
    [InlineData(1.25f, 1)]
    [InlineData(1.5f, 1)]
    [InlineData(1.75f, 2)]
    public void KeepOneLogicalPixelLinesTheSameWholePixelWidthWhereverPlaced(float scale, int expectedPixels)
    {
        for (var x = 0f; x < 20; x += 0.5f)
        {
            var snapped = PixelSnap.Bounds(new RectangleF(x, y: 3, width: 1, height: 22), scale);

            Assert.Equal(expectedPixels, snapped.Width * scale, Precision);
            Assert.Equal(MathF.Round(snapped.X * scale), snapped.X * scale, Precision);
        }
    }

    [Theory]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void LeaveNoGapOrOverlapBetweenAdjacentAreas(float scale)
    {
        var top = PixelSnap.Bounds(new RectangleF(x: 0, y: 65, width: 300, height: 26), scale);
        var below = PixelSnap.Bounds(new RectangleF(x: 0, y: 91, width: 300, height: 25), scale);

        Assert.Equal(top.Bottom, below.Top, Precision);
        Assert.Equal(MathF.Round(top.Bottom * scale), top.Bottom * scale, Precision);
    }

    [Theory]
    [InlineData(1f, 1.5f, 1)]
    [InlineData(2f, 1.5f, 3)]
    [InlineData(0.5f, 1.5f, 1)]
    [InlineData(3f, 1.25f, 4)]
    public void RoundThicknessToWholeDevicePixels(float thickness, float scale, int expectedPixels)
    {
        Assert.Equal(expectedPixels, PixelSnap.Thickness(thickness, scale) * scale, Precision);
    }
}
