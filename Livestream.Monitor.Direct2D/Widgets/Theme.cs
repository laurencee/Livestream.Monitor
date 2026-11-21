using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;

namespace Livestream.Monitor.Direct2D.Widgets;

internal static class Theme
{
    internal const uint Black = 0xFF000000;
    internal const uint White = 0xFFFFFFFF;

    // MahApps 2.4.11 colors and brush opacities used by the application palette:
    // https://github.com/MahApps/MahApps.Metro/blob/2.4.11/src/MahApps.Metro/Styles/Themes/GeneratorParameters.json
    public static ThemePalette Create(Settings settings)
    {
        var isDark = settings.MetroThemeBaseColour == MetroThemeBaseColour.BaseDark;
        var background = ThemePalette.GetBackground(isDark);
        var accent = Accent(settings.MetroThemeAccentColour);
        var accentText = settings.MetroThemeAccentColour == MetroThemeAccentColour.Yellow ? Black : White;
        var highlight = Highlight(accent);
        const float SelectionOpacity = 0.8f;
        const float RowHoverOpacity = 0.6f;
        const float TileOpacity = 0.4f;
        var selection = Blend(background, accent, SelectionOpacity);
        var rowHover = Blend(background, accent, RowHoverOpacity);
        var tile = Blend(background, accent, TileOpacity);
        var menuAccent = ContrastAccent(accent, ThemePalette.GetContextMenuHover(isDark));
        var choiceAccent = ContrastAccent(isDark ? accent : highlight, ThemePalette.GetPanel(isDark));

        return new ThemePalette(
            IsDark: isDark,
            Accent: accent,
            AccentText: accentText,
            Highlight: highlight,
            Selection: selection,
            RowHover: rowHover,
            Tile: tile,
            MenuAccent: menuAccent,
            ChoiceAccent: choiceAccent
        );
    }

    private static uint Accent(MetroThemeAccentColour color) => color switch
    {
        MetroThemeAccentColour.Red => 0xFFE51400,
        MetroThemeAccentColour.Green => 0xFF60A917,
        MetroThemeAccentColour.Blue => 0xFF0078D7,
        MetroThemeAccentColour.Purple => 0xFF6459DF,
        MetroThemeAccentColour.Orange => 0xFFFA6800,
        MetroThemeAccentColour.Lime => 0xFFA4C400,
        MetroThemeAccentColour.Emerald => 0xFF008A00,
        MetroThemeAccentColour.Teal => 0xFF00ABA9,
        MetroThemeAccentColour.Cyan => 0xFF1BA1E2,
        MetroThemeAccentColour.Cobalt => 0xFF0050EF,
        MetroThemeAccentColour.Indigo => 0xFF6A00FF,
        MetroThemeAccentColour.Violet => 0xFFAA00FF,
        MetroThemeAccentColour.Pink => 0xFFF472D0,
        MetroThemeAccentColour.Magenta => 0xFFD80073,
        MetroThemeAccentColour.Crimson => 0xFFA20025,
        MetroThemeAccentColour.Amber => 0xFFF0A30A,
        MetroThemeAccentColour.Yellow => 0xFFFEDE06,
        MetroThemeAccentColour.Brown => 0xFF825A2C,
        MetroThemeAccentColour.Olive => 0xFF6D8764,
        MetroThemeAccentColour.Steel => 0xFF647687,
        MetroThemeAccentColour.Mauve => 0xFF76608A,
        MetroThemeAccentColour.Taupe => 0xFF87794E,
        MetroThemeAccentColour.Sienna => 0xFFA0522D,
        _ => 0xFF0078D7,
    };

    private static uint Highlight(uint accent)
    {
        // MahApps 2.4.11 highlight brushes truncate each accent channel at this brightness.
        const float HighlightBrightness = 0.74f;
        const uint ChannelMask = 0xFF;
        const int RedShift = 16;
        const int GreenShift = 8;
        var red = (uint)(((accent >> RedShift) & ChannelMask) * HighlightBrightness);
        var green = (uint)(((accent >> GreenShift) & ChannelMask) * HighlightBrightness);
        var blue = (uint)((accent & ChannelMask) * HighlightBrightness);
        return Black | (red << RedShift) | (green << GreenShift) | blue;
    }

    public static uint ContrastingText(uint background)
    {
        // At this luminance black and white have equal contrast against the background.
        const double EqualContrastLuminance = 0.179128784747792;
        return Luminance(background) >= EqualContrastLuminance ? Black : White;
    }

    public static uint ContrastAccent(uint accent, uint surface)
    {
        const double MinimumContrast = 3;
        const double LuminanceOffset = 0.05;
        const float ShadeStep = 0.25f;
        var surfaceLuminance = Luminance(surface);
        var shade = ContrastingText(surface);
        var color = accent;

        // Retain the accent hue when its base colour is too close to the surrounding surface.
        while (true)
        {
            var luminance = Luminance(color);
            var contrast = (Math.Max(luminance, surfaceLuminance) + LuminanceOffset) /
                           (Math.Min(luminance, surfaceLuminance) + LuminanceOffset);
            if (contrast >= MinimumContrast) return color;

            color = Blend(color, shade, ShadeStep);
        }
    }

    private static double Luminance(uint color)
    {
        const uint ChannelMask = 0xFF;
        const int RedShift = 16;
        const int GreenShift = 8;
        const double RedWeight = 0.2126;
        const double GreenWeight = 0.7152;
        const double BlueWeight = 0.0722;
        var red = LinearChannel((color >> RedShift) & ChannelMask);
        var green = LinearChannel((color >> GreenShift) & ChannelMask);
        var blue = LinearChannel(color & ChannelMask);
        return RedWeight * red + GreenWeight * green + BlueWeight * blue;

        static double LinearChannel(uint channel)
        {
            const double MaxChannel = 255;
            const double LinearCutoff = 0.04045;
            const double LinearScale = 12.92;
            const double SrgbOffset = 0.055;
            const double SrgbExponent = 2.4;
            var srgb = channel / MaxChannel;
            return srgb <= LinearCutoff ? srgb / LinearScale : Math.Pow((srgb + SrgbOffset) / (1 + SrgbOffset), SrgbExponent);
        }
    }

    /// <summary>Composites opaque RGB colors for both native controls and Direct2D surfaces.</summary>
    public static uint Blend(uint background, uint foreground, float opacity)
    {
        const uint ChannelMask = 0xFF;
        const int RedShift = 16;
        const int GreenShift = 8;
        var red = BlendChannel(RedShift);
        var green = BlendChannel(GreenShift);
        var blue = BlendChannel(0);
        return Black | (red << RedShift) | (green << GreenShift) | blue;

        uint BlendChannel(int shift)
        {
            var backgroundChannel = (background >> shift) & ChannelMask;
            var foregroundChannel = (foreground >> shift) & ChannelMask;
            var mixed = backgroundChannel * (1 - opacity) + foregroundChannel * opacity;
            return (uint)MathF.Round(mixed);
        }
    }
}

internal sealed record ThemePalette(
    bool IsDark,
    uint Accent,
    uint AccentText,
    uint Highlight,
    uint Selection,
    // The grid uses the 60% accent for hover, 80% for a focused selection, and 40% for an unfocused selection.
    uint RowHover,
    uint Tile,
    uint MenuAccent,
    uint ChoiceAccent)
{
    private const uint Gray5 = 0xFFB9B9B9;
    private const uint Gray6 = 0xFFCCCCCC;
    private const uint MediumGray = 0xFF767676;
    private const uint DarkGray = 0xFF595959;
    private const uint AccentBrushMask = 0xCCFFFFFF;

    // The data grid's line above and rule below its column headers.
    private const uint TableBorderGray = 0xFFA0A0A0;

    // The combo box list border as rendered, the same in both themes.
    private const uint ListBorderGray = 0xFFA59F93;

    // MahApps uses #7F7F7F for both themes; on the dark background that is 3.8:1, below the 4.5:1 WCAG minimum for
    // small text, so dark themes use a lighter grey (5.4:1).
    private const uint DarkMutedText = 0xFF9A9A9A;
    private const uint LightMutedText = 0xFF7F7F7F;

    // MahApps Gray7, the tooltip border.
    private const uint DarkGray7 = 0xFF7E7E7E;
    private const uint LightGray7 = 0xFFD8D8D9;

    // MahApps WindowTitle.NonActive and Border.NonActive, the same gray in both themes.
    private const uint NonActiveGray = 0xFF808080;

    // Dark control borders.
    private const uint DarkControlBorder = 0xFF7E7E7E;
    private const uint DarkControlBorderHover = 0xFF898989;
    private const uint DarkComboHover = 0xFF616161;
    // MahApps Gray9 composited over the light theme's white background.
    private const uint LightComboHover = 0xFFEBEBEB;

    private const uint DarkMenuSelection = 0xFF3C3C3C;

    internal uint Background => GetBackground(IsDark);
    internal uint Panel => GetPanel(IsDark);
    internal uint Text => IsDark ? Theme.White : Theme.Black;
    internal uint MutedText => IsDark ? DarkMutedText : LightMutedText;
    internal uint Border => Gray6;
    internal uint ControlBorder => IsDark ? DarkControlBorder : 0xFF888888;
    internal uint ControlBorderHover => IsDark ? DarkControlBorderHover : DarkGray;
    internal uint ControlDisabled => IsDark ? 0xFF393939 : 0xFFFBFBFB;
    internal uint ControlDisabledBorder => IsDark ? 0xFF535353 : 0xFFE3E3E3;
    internal uint ControlDisabledText => IsDark ? 0xFFA0A0A0 : 0xFF808080;
    internal uint InputBorder => MediumGray;
    internal uint InputBorderHover => IsDark ? 0xFF999999 : DarkGray;
    internal uint InputBorderFocus => IsDark ? Gray5 : Theme.Black;
    internal uint InputDisabled => IsDark ? 0xFF333333 : 0xFFF1F1F1;
    internal uint InputDisabledBorder => IsDark ? 0xFF535353 : 0xFFB8B8B8;
    internal uint InputDisabledText => IsDark ? 0xFF969696 : MediumGray;
    internal uint ComboHover => IsDark ? DarkComboHover : LightComboHover;
    internal uint ComboHoverBorder => IsDark ? Gray5 : DarkGray;
    internal uint ComboOpenBorder => IsDark ? Gray6 : Theme.Black;
    internal uint Input => Background;
    internal uint Control => IsDark ? 0xFF2F2F2F : 0xFFF7F7F7;
    internal uint Hover => IsDark ? 0xFF454545 : 0xFFE0E0E0;
    internal uint Pressed => IsDark ? 0xFF6D6D6D : LightGray7;
    internal uint AccentBrush => Accent & AccentBrushMask;
    internal uint SelectionText => AccentText;

    // MahApps draws the title bar and border with the same 80% accent as the selection.
    internal uint TitleBar => Selection;
    internal uint InactiveChrome => NonActiveGray;
    internal uint TitleText => AccentText;
    internal uint HeaderRule => Gray5;
    internal uint TableBorder => TableBorderGray;
    internal uint ToolTipBorder => IsDark ? DarkGray7 : LightGray7;
    internal uint ListBorder => ListBorderGray;
    internal uint ContextMenuBorder => IsDark ? Gray6 : 0xE0717070;
    internal uint MenuSelection => IsDark ? DarkMenuSelection : 0xFFF0F0F0;
    internal uint ContextMenuHover => GetContextMenuHover(IsDark);
    internal uint ContextMenuDisabledText => IsDark ? 0xFF7E7E7E : LightMutedText;
    internal uint ChoiceBorder => IsDark ? Gray6 : 0xFF707070;
    internal uint ChoiceBorderHover => IsDark ? 0xFFE5E5E5 : DarkGray;
    internal uint ChoiceDisabledBorder => IsDark ? DarkMutedText : Gray5;
    internal uint ChoiceDisabledText => IsDark ? DarkMutedText : 0x66000000;

    internal static uint GetBackground(bool isDark) => isDark ? 0xFF252525 : Theme.White;
    internal static uint GetPanel(bool isDark) => isDark ? 0xFF2B2B2B : Theme.White;
    internal static uint GetContextMenuHover(bool isDark) => isDark ? DarkMenuSelection : 0xFFDEDEDE;
}
