using System;
using System.Collections.Generic;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Direct2D.Widgets;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class ThemeShould
{
    [Theory]
    [InlineData(MetroThemeAccentColour.Purple, 0xFF4A41A5u)]
    [InlineData(MetroThemeAccentColour.Blue, 0xFF00589Fu)]
    [InlineData(MetroThemeAccentColour.Orange, 0xFFB94C00u)]
    [InlineData(MetroThemeAccentColour.Yellow, 0xFFBBA404u)]
    public void PreserveTheOriginalTileHighlightColors(MetroThemeAccentColour accent, uint expected)
    {
        var settings = new Settings { MetroThemeAccentColour = accent };

        Assert.Equal(expected, Theme.Create(settings).Highlight);
    }

    [Theory]
    [InlineData(MetroThemeAccentColour.Red, 0xFFE51400u)]
    [InlineData(MetroThemeAccentColour.Green, 0xFF60A917u)]
    [InlineData(MetroThemeAccentColour.Blue, 0xFF0078D7u)]
    [InlineData(MetroThemeAccentColour.Purple, 0xFF6459DFu)]
    [InlineData(MetroThemeAccentColour.Orange, 0xFFFA6800u)]
    [InlineData(MetroThemeAccentColour.Lime, 0xFFA4C400u)]
    [InlineData(MetroThemeAccentColour.Emerald, 0xFF008A00u)]
    [InlineData(MetroThemeAccentColour.Teal, 0xFF00ABA9u)]
    [InlineData(MetroThemeAccentColour.Cyan, 0xFF1BA1E2u)]
    [InlineData(MetroThemeAccentColour.Cobalt, 0xFF0050EFu)]
    [InlineData(MetroThemeAccentColour.Indigo, 0xFF6A00FFu)]
    [InlineData(MetroThemeAccentColour.Violet, 0xFFAA00FFu)]
    [InlineData(MetroThemeAccentColour.Pink, 0xFFF472D0u)]
    [InlineData(MetroThemeAccentColour.Magenta, 0xFFD80073u)]
    [InlineData(MetroThemeAccentColour.Crimson, 0xFFA20025u)]
    [InlineData(MetroThemeAccentColour.Amber, 0xFFF0A30Au)]
    [InlineData(MetroThemeAccentColour.Yellow, 0xFFFEDE06u)]
    [InlineData(MetroThemeAccentColour.Brown, 0xFF825A2Cu)]
    [InlineData(MetroThemeAccentColour.Olive, 0xFF6D8764u)]
    [InlineData(MetroThemeAccentColour.Steel, 0xFF647687u)]
    [InlineData(MetroThemeAccentColour.Mauve, 0xFF76608Au)]
    [InlineData(MetroThemeAccentColour.Taupe, 0xFF87794Eu)]
    [InlineData(MetroThemeAccentColour.Sienna, 0xFFA0522Du)]
    public void PreserveTheOriginalMetroAccentColors(MetroThemeAccentColour color, uint expected)
    {
        Assert.Equal(expected, Theme.Accent(color));
    }

    [Fact]
    public void GiveEveryAccentASeparateColor()
    {
        var colors = new HashSet<uint>();
        foreach (var accent in Enum.GetValues<MetroThemeAccentColour>()) Assert.True(colors.Add(Theme.Accent(accent)));
    }

    [Fact]
    public void ApplyBaseThemeChangesWithoutSavingAndKeepExistingSnapshotsUnchanged()
    {
        var settings = new Settings { MetroThemeBaseColour = MetroThemeBaseColour.BaseDark };
        var dark = Theme.Create(settings);
        settings.MetroThemeBaseColour = MetroThemeBaseColour.BaseLight;
        var light = Theme.Create(settings);

        Assert.True(dark.IsDark);
        Assert.False(light.IsDark);
        Assert.Equal(0xFF252525u, dark.Background);
        Assert.Equal(0xFFFFFFFFu, light.Background);
        Assert.Equal(0xFF2B2B2Bu, dark.Panel);
        Assert.Equal(0xFFFFFFFFu, light.Panel);
        Assert.Equal(0xFFFFFFFFu, dark.Text);
        Assert.Equal(0xFF000000u, light.Text);
        Assert.Equal(0xFF9A9A9Au, dark.MutedText);
        Assert.Equal(0xFF7F7F7Fu, light.MutedText);
        Assert.Equal(dark.Background, dark.Input);
        Assert.Equal(light.Background, light.Input);
        Assert.Equal(0xFF2F2F2Fu, dark.Control);
        Assert.Equal(0xFFF7F7F7u, light.Control);
        Assert.Equal(0xFF454545u, dark.Hover);
        Assert.Equal(0xFFE0E0E0u, light.Hover);
        Assert.Equal(0xFF7E7E7Eu, dark.Pressed);
        Assert.Equal(0xFFD8D8D9u, light.Pressed);
        Assert.Equal(0xFFCCCCCCu, light.ControlBorder);
        Assert.Equal(0xFFCCCCCCu, light.ControlBorderHover);
        Assert.Equal(0xFFCCCCCCu, light.InputBorder);
        Assert.Equal(0xFF7F7F7Fu, light.InputBorderHover);
        Assert.Equal(0xFF000000u, light.InputBorderFocus);
        Assert.Equal(0xFF7F7F7Fu, light.ComboHoverBorder);
        Assert.Equal(0xFF000000u, light.ComboOpenBorder);
        Assert.NotEqual(dark.Selection, light.Selection);
        Assert.NotEqual(dark.Tile, light.Tile);
        Assert.Equal(dark.Accent, light.Accent);
    }

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
