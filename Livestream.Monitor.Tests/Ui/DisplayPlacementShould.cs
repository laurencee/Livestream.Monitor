using System.Windows;
using Livestream.Monitor.WPF.Core.UI;
using Xunit;

namespace Livestream.Monitor.Tests.Ui;

public class DisplayPlacementShould
{
    [Theory]
    [InlineData(640)]
    [InlineData(1920)]
    public void KeepTrayAndStreamPopupsInSeparateColumns(double width)
    {
        var workArea = new Rect(x: -width, y: 0, width: width, height: 600);
        var tray = DisplayPlacement.GetNotificationBounds(workArea, new Size(320, 100), rightAligned: false);
        var stream = DisplayPlacement.GetNotificationBounds(workArea, new Size(400, 100), rightAligned: true);

        Assert.True(workArea.Contains(tray));
        Assert.True(workArea.Contains(stream));
        Assert.False(tray.IntersectsWith(stream));
    }
}
