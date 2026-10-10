using System.Drawing;
using Livestream.Monitor.Direct2D.Windows.Output;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class OutputWindowShould
{
    [Fact]
    public void GrowTheOutputPopupWithItsWrappedContent()
    {
        var maximumSize = OutputWindow.MaximumSize(new SizeF(1200, 800));
        var shortOutput = OutputWindow.CalculateSize(320, 20, maximumSize);
        var longOutput = OutputWindow.CalculateSize(320, 200, maximumSize);

        Assert.True(shortOutput.Height < longOutput.Height);
        Assert.True(shortOutput.Height < 200);
        Assert.Equal(shortOutput.Width, longOutput.Width);
    }

    // The WPF output viewport is at most 700 wide and 500 high, below a 30-pixel caption and above a 23-pixel footer;
    // the window is also shown at most 75% as wide as the app and no taller than it. The size the output asks for does
    // not depend on the app, so output that arrived while the app was small is shown in full once the app is enlarged.
    [Theory]
    [InlineData(2000, 2000, 700, 553)]
    [InlineData(800, 400, 600, 400)]
    public void CapTheOutputWindowAtTheWpfLimitsAndTheAppSize(
        float appWidth,
        float appHeight,
        float expectedWidth,
        float expectedHeight)
    {
        var requestedSize = OutputWindow.RequestedSize(SizeF.Empty, contentWidth: 1200, contentHeight: 1200);
        var size = OutputWindow.ShownSize(requestedSize, noticeHeight: 0, new SizeF(appWidth, appHeight));

        Assert.Equal(new SizeF(expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void KeepItsSizeWhenLaterOutputNeedsLess()
    {
        var requestedSize = OutputWindow.RequestedSize(SizeF.Empty, contentWidth: 600, contentHeight: 300);

        Assert.Equal(requestedSize, OutputWindow.RequestedSize(requestedSize, contentWidth: 100, contentHeight: 50));
    }
}
