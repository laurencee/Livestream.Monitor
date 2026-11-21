using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Direct2D.Widgets;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class ThemeShould
{
    [Theory]
    [InlineData(MetroThemeAccentColour.Orange, 0xFFFFFFFFu)]
    [InlineData(MetroThemeAccentColour.Yellow, 0xFF000000u)]
    public void UseTheOriginalForegroundForAccentSurfaces(MetroThemeAccentColour accent, uint expected)
    {
        var settings = new Settings { MetroThemeAccentColour = accent };
        var palette = Theme.Create(settings);

        Assert.Equal(expected, palette.AccentText);
        Assert.Equal(expected, palette.SelectionText);
        Assert.Equal(expected, palette.TitleText);
    }

    [Theory]
    [InlineData(0f, 0xFF252525u)]
    [InlineData(1f, 0xFFFA6800u)]
    [InlineData(0.8f, 0xFFCF5B07u)]
    [InlineData(0.4f, 0xFF7A4016u)]
    public void CompositeAccentOpacityOverTheWindowBackground(float opacity, uint expected)
    {
        Assert.Equal(expected, Theme.Blend(0xFF252525, 0xFFFA6800, opacity));
    }
}
