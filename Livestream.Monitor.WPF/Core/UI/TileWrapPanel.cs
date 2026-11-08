using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Livestream.Monitor.WPF.Core.UI;

/// <summary>Wraps tiles using the same column count before and after the automatic vertical scrollbar appears.</summary>
public sealed class TileWrapPanel : Panel
{
    private const double PreferredCellWidth = 161; // A 155-DIP tile with a 3-DIP margin on each side.

    private static readonly DependencyProperty ScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(ScrollBarVisibility),
        typeof(Visibility),
        typeof(TileWrapPanel),
        new FrameworkPropertyMetadata(Visibility.Collapsed, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private double widthWithoutScrollBar;
    private int columns;
    private double cellWidth;
    private double rowHeight;

    public TileWrapPanel()
    {
        var visibility = new Binding(nameof(ScrollViewer.ComputedVerticalScrollBarVisibility))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ScrollViewer), 1),
        };

        BindingOperations.SetBinding(this, ScrollBarVisibilityProperty, visibility);
    }

    private Visibility ScrollBarVisibility => (Visibility)GetValue(ScrollBarVisibilityProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        // WPF first measures with the auto scrollbar collapsed, then measures again beside it if the tiles overflow.
        if (ScrollBarVisibility != Visibility.Visible) widthWithoutScrollBar = availableSize.Width;

        var children = InternalChildren;
        var width = Math.Max(widthWithoutScrollBar, availableSize.Width);
        columns = double.IsPositiveInfinity(width) ? Math.Max(1, children.Count) : Math.Max(1, (int)(width / PreferredCellWidth));
        cellWidth = Math.Min(PreferredCellWidth, Math.Floor(availableSize.Width / columns));
        var cellSize = new Size(cellWidth, double.PositiveInfinity);
        rowHeight = 0;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            child.Measure(cellSize);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        var rows = (children.Count + columns - 1) / columns;
        return new Size(Math.Min(columns, children.Count) * cellWidth, rows * rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        for (var i = 0; i < children.Count; i++)
        {
            var row = i / columns;
            var x = i % columns * cellWidth;
            var y = row * rowHeight;
            children[i].Arrange(new Rect(x, y, cellWidth, rowHeight));
        }

        return finalSize;
    }
}
