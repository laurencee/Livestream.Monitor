using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;

namespace Livestream.Monitor.Direct2D.Widgets;

internal static class Theme
{
    private const uint Black = 0xFF000000;
    private const uint White = 0xFFFFFFFF;
    private const uint Gray5 = 0xFFB9B9B9;
    private const uint Gray6 = 0xFFCCCCCC;
    private const uint AccentBrushMask = 0xCCFFFFFF;

    // The WPF data grid's line above and rule below its column headers, measured from the WPF frontend.
    private const uint TableBorderGray = 0xFFA0A0A0;

    // The WPF combo box list border as rendered, the same in both themes.
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

    // Dark control borders measured from the WPF frontend.
    private const uint DarkControlBorder = 0xFF7E7E7E;
    private const uint DarkControlBorderHover = 0xFF898989;
    private const uint DarkInputBorder = 0xFF4F4F4F;
    private const uint DarkInputBorderHover = 0xFF5C5C5C;
    private const uint DarkComboHover = 0xFF616161;
    // MahApps Gray9 composited over the light theme's white background.
    private const uint LightComboHover = 0xFFEBEBEB;
    private const uint DarkComboHoverBorder = 0xFF686868;
    private const uint DarkComboOpenBorder = 0xFF9C9C9C;

    // MahApps 2.4.11 colors and brush opacities used by the original WPF frontend:
    // https://github.com/MahApps/MahApps.Metro/blob/2.4.11/src/MahApps.Metro/Styles/Themes/GeneratorParameters.json
    public static ThemePalette Create(Settings settings)
    {
        var isDark = settings.MetroThemeBaseColour == MetroThemeBaseColour.BaseDark;
        var background = isDark ? 0xFF252525 : White;
        var text = isDark ? White : Black;
        var accent = Accent(settings.MetroThemeAccentColour);
        var accentText = settings.MetroThemeAccentColour == MetroThemeAccentColour.Yellow ? Black : White;
        const float SelectionOpacity = 0.8f;
        const float RowHoverOpacity = 0.6f;
        const float TileOpacity = 0.4f;
        var selection = Blend(background, accent, SelectionOpacity);
        var rowHover = Blend(background, accent, RowHoverOpacity);
        var tile = Blend(background, accent, TileOpacity);

        return new ThemePalette(
            IsDark: isDark,
            Background: background,
            Panel: isDark ? 0xFF2B2B2B : White,
            Text: text,
            MutedText: isDark ? DarkMutedText : LightMutedText,
            Border: Gray6,
            ControlBorder: isDark ? DarkControlBorder : Gray6,
            ControlBorderHover: isDark ? DarkControlBorderHover : Gray6,
            InputBorder: isDark ? DarkInputBorder : Gray6,
            InputBorderHover: isDark ? DarkInputBorderHover : LightMutedText,
            InputBorderFocus: isDark ? DarkInputBorderHover : Black,
            ComboHover: isDark ? DarkComboHover : LightComboHover,
            ComboHoverBorder: isDark ? DarkComboHoverBorder : LightMutedText,
            ComboOpenBorder: isDark ? DarkComboOpenBorder : Black,
            Input: background,
            Control: isDark ? 0xFF2F2F2F : 0xFFF7F7F7,
            Hover: isDark ? 0xFF454545 : 0xFFE0E0E0,
            Highlight: Highlight(accent),
            Pressed: isDark ? DarkGray7 : LightGray7,
            Accent: accent,
            AccentBrush: accent & AccentBrushMask,
            AccentText: accentText,
            Selection: selection,
            SelectionText: accentText,
            RowHover: rowHover,
            Tile: tile,
            TitleBar: selection,
            InactiveChrome: NonActiveGray,
            TitleText: accentText,
            HeaderRule: Gray5,
            TableBorder: TableBorderGray,
            ToolTipBorder: isDark ? DarkGray7 : LightGray7,
            ListBorder: ListBorderGray,
            ContextMenuBorder: isDark ? Gray6 : 0xE0717070,
            ContextMenuHover: isDark ? 0xFF313131 : 0xFFDEDEDE,
            ContextMenuDisabledText: isDark ? 0xFF7E7E7E : LightMutedText,
            ChoiceBorder: isDark ? Gray6 : Gray5,
            ChoiceBorderHover: isDark ? Gray6 : LightMutedText,
            ChoiceDisabledText: isDark ? DarkMutedText : 0x66000000
        );
    }

    public static uint Accent(MetroThemeAccentColour color) => color switch
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
        const uint ChannelMask = 0xFF;
        const int RedShift = 16;
        const int GreenShift = 8;
        const double RedWeight = 0.2126;
        const double GreenWeight = 0.7152;
        const double BlueWeight = 0.0722;
        var red = LinearChannel((background >> RedShift) & ChannelMask);
        var green = LinearChannel((background >> GreenShift) & ChannelMask);
        var blue = LinearChannel(background & ChannelMask);
        var luminance = RedWeight * red + GreenWeight * green + BlueWeight * blue;

        // At this luminance black and white have equal contrast against the background.
        const double EqualContrastLuminance = 0.179128784747792;
        return luminance >= EqualContrastLuminance ? Black : White;

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
    uint Background,
    uint Panel,
    uint Text,
    uint MutedText,
    uint Border,
    uint ControlBorder,
    uint ControlBorderHover,
    uint InputBorder,
    uint InputBorderHover,
    uint InputBorderFocus,
    uint ComboHover,
    uint ComboHoverBorder,
    uint ComboOpenBorder,
    uint Input,
    uint Control,
    uint Hover,
    uint Highlight,
    uint Pressed,
    uint Accent,
    uint AccentBrush,
    uint AccentText,
    uint Selection,
    uint SelectionText,

    // The data grid's row under the pointer, the 60% accent. A selected row uses Selection (80%) while the grid has
    // keyboard focus in the active window, and Tile (40%) with the normal text colour otherwise.
    uint RowHover,
    uint Tile,

    // The window title bar and border: MahApps draws them with the same 80% accent as the selection.
    uint TitleBar,

    // The title bar and border of an inactive window.
    uint InactiveChrome,
    uint TitleText,
    uint HeaderRule,
    uint TableBorder,
    uint ToolTipBorder,

    // The border of a combo box list and a drop-down list.
    uint ListBorder,

    uint ContextMenuBorder,
    uint ContextMenuHover,
    uint ContextMenuDisabledText,
    uint ChoiceBorder,
    uint ChoiceBorderHover,
    uint ChoiceDisabledText
);
