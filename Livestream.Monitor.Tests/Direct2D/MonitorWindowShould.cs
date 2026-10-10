using Livestream.Monitor.Direct2D.Windows.Monitor;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class MonitorWindowShould
{
    [Theory]
    [InlineData(20, 300)]
    [InlineData(250, 0)]
    [InlineData(600, 125)]
    public void KeepKeyboardSelectionVisibleAfterWheelScrolling(int selectedTop, int offset)
    {
        const int ItemHeight = 120;
        const int Viewport = 250;
        var selectedBottom = selectedTop + ItemHeight;
        var next = MonitorWindow.GetVisibleOffset(selectedTop, selectedBottom, offset, Viewport);
        Assert.InRange(selectedTop, next, next + Viewport - ItemHeight);
    }

    [Fact]
    public void KeepTheViewportStationaryWhenSelectionIsAlreadyVisible()
    {
        const int SelectedTop = 125;
        const int SelectedBottom = SelectedTop + 120;
        const int Offset = 100;
        const int Viewport = 250;
        var next = MonitorWindow.GetVisibleOffset(SelectedTop, SelectedBottom, Offset, Viewport);
        Assert.Equal(Offset, next);
    }
}
