using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Livestream.Monitor.Tests.TestFramework;
using Livestream.Monitor.WPF.Core.UI;
using MahApps.Metro.Controls;
using Xunit;

namespace Livestream.Monitor.Tests.Ui;

public class TileWrapPanelShould
{
    [Fact]
    public Task EnableVerticalScrollingOnlyWhenTilesOverflow() => StaThread.Run(() =>
    {
        var tiles = new TileWrapPanel();
        var scrollViewer = new ScrollViewer
        {
            Content = tiles,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        const double TileHeight = 120;
        const double TileMargin = 3;
        const int FittingTileCount = 10;
        const int OverflowingTileCount = 15;
        var size = new Size(805, 252);

        AddTiles(FittingTileCount);
        Layout();
        Assert.Equal(Visibility.Collapsed, scrollViewer.ComputedVerticalScrollBarVisibility);
        Assert.Equal(0, scrollViewer.ScrollableHeight);

        AddTiles(OverflowingTileCount);
        Layout();
        Assert.Equal(Visibility.Visible, scrollViewer.ComputedVerticalScrollBarVisibility);
        Assert.True(scrollViewer.ScrollableHeight > 0);

        tiles.Children.RemoveRange(FittingTileCount, OverflowingTileCount - FittingTileCount);
        Layout();
        Assert.Equal(Visibility.Collapsed, scrollViewer.ComputedVerticalScrollBarVisibility);
        Assert.Equal(0, scrollViewer.ScrollableHeight);
        return Task.CompletedTask;

        void AddTiles(int count)
        {
            while (tiles.Children.Count < count)
                tiles.Children.Add(new Tile { Width = double.NaN, Height = TileHeight, Margin = new Thickness(TileMargin) });
        }

        void Layout()
        {
            scrollViewer.Measure(size);
            scrollViewer.Arrange(new Rect(size));
            scrollViewer.UpdateLayout();
        }
    });
}
