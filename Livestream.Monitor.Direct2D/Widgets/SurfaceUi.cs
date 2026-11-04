using System.Drawing;
using Livestream.Monitor.Direct2D.Automation;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Windows;

namespace Livestream.Monitor.Direct2D.Widgets;

/// <summary>
/// Theme-aware drawn widgets, with native editing retained for text selection, clipboard and IME. Each drawn frame also
/// records the widgets, text and open menu as a UI Automation tree.
/// </summary>
internal sealed class SurfaceUi(DesktopWindow owner, List<DrawCommand> commands)
{
    private const float FontSize = 12;
    private const float InputInset = 4;

    // Placeholder and typed text start this far inside an input box, as in the WPF (MahApps) text boxes.
    private const float InputTextInset = 8;
    private const float PlaceholderOpacity = 0.6f;

    // Menu sizes follow the WPF (MahApps) menus. A combo box or drop-down list pads its rows inside its border and
    // never ticks a choice; a context menu uses larger text and rows, keeps a column for ticks and leaves room after
    // the text.
    private static readonly MenuStyle ListMenuStyle = new(RowHeight: 22, TextInset: 4, EndPadding: 8, Inset: 2);
    private static readonly MenuStyle ContextMenuStyle = new(RowHeight: 30.5f, TextInset: 32, EndPadding: 37, Inset: 1, TextSize: 14);

    // On the automation group stack in place of a group that is covered or scrolled out of view.
    private const int HiddenGroup = -2;

    // Button texts drawn as centred glyphs rather than labels.
    private static readonly string[] IconTexts = ["›", "‹", "◀", "▶", "↑", "↓", "×", "+", "↻", "⚙", "▴", "▲", "▼"];
    private readonly List<Widget> widgets = [];
    private readonly Dictionary<string, TextInput> inputs = [];
    private readonly HashSet<string> radios = [];
    private readonly List<ToolTipRegion> toolTips = [];
    private readonly List<AutomationNode> automation = [];
    private readonly Dictionary<string, int> automationKeys = [];
    private readonly Stack<int> automationGroups = new();
    private AutomationTree automationTree = AutomationTree.Empty;
    private string? hovered;
    private string? pressed;
    private string? focused;
    private MenuState? menu;
    private RectangleF viewport;
    private RectangleF? clip;
    private RectangleF? occlusion;
    private PointF pointer;
    private bool overlay;

    public ThemePalette Palette { get; private set; } = null!;

    /// <summary>False while another window is active, when the title bar and border are drawn gray as in WPF.</summary>
    public bool IsWindowActive { get; set; } = true;

    public uint TitleBarColor => IsWindowActive ? Palette.TitleBar : Palette.InactiveChrome;

    /// <summary>True while a widget or text input has keyboard focus, so a list's selection is shown inactive.</summary>
    public bool HasFocusedWidget => focused != null || HasFocusedInput;
    public bool HasMenu => menu != null;

    /// <summary>The automation tree of the last drawn frame; read by UI Automation from its own threads.</summary>
    public AutomationTree Automation => Volatile.Read(ref automationTree);
    public bool HasFocusedInput => inputs.Values.Any(input => Win32.GetFocus() == input.Handle);

    /// <summary>
    /// The id of the focused text input, or of the widget last focused while the window holds keyboard focus, for
    /// <see cref="Focus"/> to restore; null for none.
    /// </summary>
    public string? FocusedId
    {
        get
        {
            var focus = Win32.GetFocus();
            foreach (var (id, input) in inputs)
                if (input.Handle == focus)
                    return id;

            return focus == owner.Handle ? focused : null;
        }
    }

    public Action? Invalidate { get; init; }

    /// <summary>Measures the width of text drawn at a font size, so drop-downs fit their choices.</summary>
    public required Func<string, float, float> MeasureWidth { get; init; }

    /// <summary>Measures wrapped text with the same width, size and weight used to draw it.</summary>
    public required Func<string, float, float, FontWeight, float> MeasureWrapped { get; init; }

    /// <summary>The tallest a combo box or drop-down list may be; WPF's default is a third of the primary screen height.</summary>
    public float ListMaximumHeight { get; set; } = float.MaxValue;

    public void Begin(ThemePalette palette, RectangleF bounds, RectangleF? coveredArea = null)
    {
        Palette = palette;
        viewport = bounds;
        occlusion = coveredArea;
        overlay = false;
        clip = null;
        ClearFrame();
        owner.ApplyControlTheme(palette);
        if (menu != null) LayoutMenu(menu);
    }

    public void Overlay() => overlay = true;

    public void ModalOverlay()
    {
        overlay = true;
        menu = null;
        ClearFrame();
    }

    // Forgets the widgets drawn so far, so only those drawn next can be clicked, focused or read.
    private void ClearFrame()
    {
        widgets.Clear();
        radios.Clear();
        toolTips.Clear();
        automation.Clear();
        automationKeys.Clear();
        automationGroups.Clear();
        foreach (var input in inputs.Values) input.Visible = false;
    }

    public void PushClip(RectangleF bounds)
    {
        clip = bounds;
        commands.Add(new DrawCommand { Kind = DrawKind.PushClip, Bounds = bounds });
    }

    public void PopClip()
    {
        clip = null;
        commands.Add(new DrawCommand { Kind = DrawKind.PopClip });
    }

    public void End()
    {
        if (menu != null) DrawMenu();

        foreach (var input in inputs.Values)
        {
            var visible = input is { Visible: true, ShowPlaceholder: false } &&
                          (menu == null || !input.Bounds.IntersectsWith(menu.Bounds));

            var inner = InputBounds(input.Bounds);
            owner.PlaceInput(input.Handle, inner, visible, input.Enabled);
        }

        Volatile.Write(ref automationTree, new AutomationTree(automation.ToArray()));
    }

    /// <summary>Starts a group, such as a list row or tile, whose elements become its automation children.</summary>
    public void BeginGroup(
        string id,
        AutomationRole role,
        string name,
        RectangleF bounds,
        AutomationStates states = AutomationStates.None)
    {
        var group = AddNode(id, role, name, bounds, states);
        automationGroups.Push(group < 0 ? HiddenGroup : group);
    }

    public void EndGroup() => automationGroups.Pop();

    public void Fill(RectangleF bounds, uint color, float radius = 0) => commands.Add(new DrawCommand
    {
        Kind = DrawKind.Fill,
        Bounds = bounds,
        Color = color,
        Radius = radius,
    });

    public void Border(RectangleF bounds, uint color, float thickness = 1, float radius = 0) => commands.Add(new DrawCommand
    {
        Kind = DrawKind.Stroke,
        Bounds = bounds,
        Color = color,
        Radius = radius,
        Thickness = thickness,
    });

    /// <summary>Draws a text row sized to its content and advances y past it.</summary>
    public RectangleF TextRow(
        string text,
        RectangleF body,
        ref float y,
        float spacing,
        uint? color = null,
        float size = FontSize,
        FontWeight weight = FontWeight.Normal)
    {
        if (string.IsNullOrWhiteSpace(text)) return RectangleF.Empty;

        var height = MeasureWrapped(text, body.Width, size, weight);
        var bounds = body with { Y = y, Height = height };

        Text(text, bounds, color, size, weight, wrap: true);
        y += height + spacing;
        return bounds;
    }

    public void Text(
        string text,
        RectangleF bounds,
        uint? color = null,
        float size = FontSize,
        FontWeight weight = FontWeight.Normal,
        TextAlign alignment = TextAlign.Leading,
        bool wrap = false,
        bool trim = true)
    {
        if (!string.IsNullOrWhiteSpace(text)) AddNode(automationId: "", AutomationRole.Text, text, bounds);
        Label(text, bounds, color, size, weight, alignment, wrap, trim);
    }

    /// <summary>Text that names a widget or group, which already carries it for UI Automation.</summary>
    public void Label(
        string text,
        RectangleF bounds,
        uint? color = null,
        float size = FontSize,
        FontWeight weight = FontWeight.Normal,
        TextAlign alignment = TextAlign.Leading,
        bool wrap = false,
        bool trim = true)
    {
        var foreground = color ?? Palette.Text;
        commands.Add(new DrawCommand
        {
            Kind = wrap ? DrawKind.WrappedText : DrawKind.Text,
            Bounds = bounds,
            Color = foreground,
            Text = text,
            Style = new TextStyle(Size: size, Weight: weight, Align: alignment, Trim: trim),
        });
    }

    public void Image(D2DBitmap? bitmap, RectangleF bounds, int revision = 0)
    {
        if (bitmap == null) return;

        commands.Add(new DrawCommand
        {
            Kind = DrawKind.Bitmap,
            Bitmap = bitmap,
            Revision = revision,
            Bounds = bounds,
        });
    }

    public void Icon(
        string text,
        RectangleF bounds,
        uint color,
        float size = FontSize)
    {
        commands.Add(new DrawCommand
        {
            Kind = DrawKind.Icon,
            Bounds = bounds,
            Color = color,
            Text = text,
            Style = new TextStyle(Size: size, Align: TextAlign.Center),
        });
    }

    /// <summary>A filled isosceles triangle in the bounds, pointing up or down.</summary>
    public void Triangle(RectangleF bounds, uint color, bool up)
    {
        var kind = up ? DrawKind.TriangleUp : DrawKind.TriangleDown;
        commands.Add(new DrawCommand { Kind = kind, Bounds = bounds, Color = color });
    }

    // The drop-down arrow of a WPF (MahApps) combo box, centred in the area.
    private void DropDownArrow(RectangleF area, uint color)
    {
        const float ArrowWidth = 7;
        const float ArrowHeight = 4;
        var left = area.X + (area.Width - ArrowWidth) / 2;
        var top = area.Y + (area.Height - ArrowHeight) / 2;
        Triangle(new RectangleF(x: left, y: top, width: ArrowWidth, height: ArrowHeight), color, up: false);
    }

    public bool IsContentPoint(PointF position) =>
        viewport.Contains(position) && menu == null && (!occlusion.HasValue || !occlusion.Value.Contains(position));

    public bool IsPointerOver(RectangleF bounds) =>
        bounds.Contains(pointer) && (overlay || IsContentPoint(pointer)) && menu == null &&
        (!clip.HasValue || clip.Value.Contains(pointer));

    private void Line(
        PointF from,
        PointF to,
        uint color,
        float thickness = 1)
    {
        var bounds = new RectangleF(x: from.X, y: from.Y, width: to.X - from.X, height: to.Y - from.Y);
        commands.Add(new DrawCommand { Kind = DrawKind.Line, Bounds = bounds, Color = color, Thickness = thickness });
    }

    // An X of two strokes centred in the bounds; drawn geometry, unlike a glyph, centres exactly.
    private void Cross(
        RectangleF bounds,
        float size,
        float thickness,
        uint color)
    {
        var left = bounds.X + (bounds.Width - size) / 2;
        var top = bounds.Y + (bounds.Height - size) / 2;
        var upperLeft = new PointF(left, top);
        var lowerRight = new PointF(left + size, top + size);
        var lowerLeft = new PointF(left, top + size);
        var upperRight = new PointF(left + size, top);
        Line(upperLeft, lowerRight, color, thickness);
        Line(lowerLeft, upperRight, color, thickness);
    }

    public void CaptionButton(
        string id,
        RectangleF bounds,
        Action action,
        bool close)
    {
        // As in WPF, the caption buttons cannot take the focus, so clicking one leaves it where it was.
        Button(id, text: "", bounds, action, chrome: true, name: close ? "Close" : "Minimize", focusable: false);

        // The MahApps Windows 10 caption glyphs: a 10-pixel cross and a 10-pixel line, both 1 pixel thick, centred.
        const float IconSize = 10;
        const float CloseStroke = 1;
        const float MinimizeHeight = 1;

        if (close)
        {
            // Colours from MahApps' Windows 10 close button resources; which is hover and which pressed is inferred.
            const uint CloseHover = 0xFFE81123;
            const uint ClosePressed = 0xFFF1707A;
            var over = IsPointerOver(bounds);
            if (over) Fill(bounds, pressed == id ? ClosePressed : CloseHover);

            const uint CloseHighlightedText = 0xFFFFFFFF;
            Cross(bounds, IconSize, CloseStroke, over ? CloseHighlightedText : Palette.TitleText);
        }
        else
        {
            var left = bounds.X + (bounds.Width - IconSize) / 2;
            var icon = new RectangleF(
                x: left,
                y: bounds.Y + (bounds.Height - MinimizeHeight) / 2,
                width: IconSize,
                height: MinimizeHeight);

            Fill(icon, Palette.TitleText);
        }
    }

    public void RightChevronButton(
        string id,
        RectangleF bounds,
        Action action,
        string name)
    {
        Button(id, text: "", bounds, action, name: name);
        const float Width = 7;
        const float HalfHeight = 6;
        const float Stroke = 2.3f;
        var center = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var top = new PointF(center.X - Width / 2, center.Y - HalfHeight);
        var tip = center with { X = center.X + Width / 2 };
        var bottom = new PointF(center.X - Width / 2, center.Y + HalfHeight);
        Line(top, tip, Palette.Text, Stroke);
        Line(tip, bottom, Palette.Text, Stroke);
    }

    public void Spinner(RectangleF bounds, double now)
    {
        const int DotCount = 8;
        const float Radius = 16;
        const float DotSize = 4;
        const int PeriodMilliseconds = 1000;
        const float MinimumOpacity = 0.2f;
        var center = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var phase = (float)(now % PeriodMilliseconds / PeriodMilliseconds);

        for (var i = 0; i < DotCount; i++)
        {
            var angle = (phase + i / (float)DotCount) * MathF.Tau;
            var opacity = MinimumOpacity + (1 - MinimumOpacity) * i / (DotCount - 1);
            var color = Theme.Blend(Palette.Background, Palette.Accent, opacity);
            var dot = new RectangleF(
                x: center.X + MathF.Cos(angle) * Radius - DotSize / 2,
                y: center.Y + MathF.Sin(angle) * Radius - DotSize / 2,
                width: DotSize,
                height: DotSize);

            commands.Add(new DrawCommand { Kind = DrawKind.Ellipse, Bounds = dot, Color = color });
        }
    }

    public void Expander(
        string id,
        string text,
        bool expanded,
        RectangleF bounds,
        Action<bool> setExpanded)
    {
        const float Diameter = 18;
        const float Stroke = 1;
        const float ArrowInset = 5;
        const float ArrowHalfHeight = 2;
        const float LabelGap = 6;
        var circle = new RectangleF(
            x: bounds.X,
            y: bounds.Y + (bounds.Height - Diameter) / 2,
            width: Diameter,
            height: Diameter);

        var color = IsPointerOver(bounds) ? Palette.Accent : Palette.Text;
        commands.Add(new DrawCommand { Kind = DrawKind.Ellipse, Bounds = circle, Color = color });
        var inner = RectangleF.Inflate(circle, -Stroke, -Stroke);
        commands.Add(new DrawCommand { Kind = DrawKind.Ellipse, Bounds = inner, Color = Palette.Panel });
        var left = circle.X + ArrowInset;
        var right = circle.Right - ArrowInset;
        var center = circle.Y + Diameter / 2;
        var edge = expanded ? center + ArrowHalfHeight : center - ArrowHalfHeight;
        var tip = expanded ? center - ArrowHalfHeight : center + ArrowHalfHeight;
        var middle = new PointF(circle.X + Diameter / 2, tip);
        Line(new PointF(left, edge), middle, color);
        Line(middle, new PointF(right, edge), color);
        var labelOffset = Diameter + LabelGap;
        var label = bounds with { X = bounds.X + labelOffset, Width = bounds.Width - labelOffset };
        Label(text, label, size: 14);
        Add(id, bounds, action: () => setExpanded(!expanded), enabled: true);
        var states = AutomationStates.Expandable | (expanded ? AutomationStates.Expanded : AutomationStates.None);
        AddNode(
            id,
            AutomationRole.Button,
            text,
            bounds,
            states,
            activate: () => SetIfAvailable(id, setExpanded, value: true),
            collapse: () => SetIfAvailable(id, setExpanded, value: false));
    }

    /// <summary>
    /// A MahApps button with rounded corners. A <paramref name="chrome"/> button is a window command drawn on the title
    /// bar; a <paramref name="flat"/> button is square.
    /// </summary>
    public void Button(
        string id,
        string text,
        RectangleF bounds,
        Action action,
        bool enabled = true,
        bool chrome = false,
        uint? foreground = null,
        bool accent = false,
        FontWeight weight = FontWeight.Normal,
        float size = FontSize,
        string? toolTip = null,
        string? name = null,
        bool flat = false,
        bool focusable = true)
    {
        const float CornerRadius = 3;
        var radius = flat ? 0 : CornerRadius;
        var over = IsPointerOver(bounds);
        var background = pressed == id ? Palette.Pressed : over ? Palette.Hover : Palette.Control;
        if (accent) background = over ? Theme.Blend(Palette.Accent, Palette.AccentText, 0.15f) : Palette.Accent;

        if (chrome)
        {
            if (over) Fill(bounds, Theme.Blend(TitleBarColor, Palette.TitleText, 0.18f));
        }
        else
        {
            // As in WPF, a pressed or focused button has a border in the text colour.
            var border = pressed == id || focused == id ? Palette.Text
                : over ? Palette.ControlBorderHover
                : Palette.ControlBorder;

            Fill(bounds, background, radius);
            Border(bounds, enabled ? border : Theme.Blend(Palette.Background, Palette.ControlBorder, 0.35f), radius: radius);
        }

        // WPF window commands are drawn at half opacity until the pointer is over them.
        const float CommandTextOpacity = 0.5f;
        var commandText = over ? Palette.TitleText : Theme.Blend(TitleBarColor, Palette.TitleText, CommandTextOpacity);
        var color = enabled
            ? foreground ?? (chrome ? commandText : accent ? Palette.AccentText : Palette.Text)
            : Palette.MutedText;

        var textBounds = RectangleF.Inflate(bounds, -3, 0);

        if (text == "▾") DropDownArrow(textBounds, color);
        else if (Array.IndexOf(IconTexts, text) >= 0) Icon(text, textBounds, color, size);
        else Label(text, textBounds, color, size: size, weight: weight, alignment: TextAlign.Center);

        Add(id, bounds, action, enabled, focusable);
        if (enabled) ToolTip(id, bounds, toolTip);
        var states = WidgetStates(id, enabled) | (focusable ? AutomationStates.None : AutomationStates.Unfocusable);
        AddNode(
            id,
            AutomationRole.Button,
            name ?? text,
            bounds,
            states,
            toolTip,
            activate: () => InvokeWidget(id));
    }

    /// <summary>A clickable area drawn by the caller, such as a column header, named as the caller gives.</summary>
    public void Hit(
        string id,
        RectangleF bounds,
        Action action,
        string name)
    {
        Add(id, bounds, action, enabled: true);
        var states = WidgetStates(id, enabled: true);
        AddNode(id, AutomationRole.Button, name, bounds, states, activate: () => InvokeWidget(id));
    }

    public void Check(
        string id,
        string text,
        bool value,
        RectangleF bounds,
        Action<bool> changed,
        bool enabled = true,
        string? toolTip = null)
    {
        const float BoxSize = 13;
        var box = new RectangleF(
            x: bounds.X,
            y: bounds.Y + (bounds.Height - BoxSize) / 2,
            width: BoxSize,
            height: BoxSize
        );

        Fill(box, value ? Palette.Accent : Palette.Input);
        Border(box, enabled ? Palette.Border : Palette.MutedText);
        if (value) Icon(text: "✓", bounds: box, color: Palette.AccentText);
        var textBounds = bounds with { X = bounds.X + 19, Width = bounds.Width - 19 };
        Label(text, textBounds, enabled ? Palette.Text : Palette.MutedText);
        Add(id, bounds, action: () => changed(!value), enabled);
        if (enabled) ToolTip(id, bounds, toolTip);
        var states = WidgetStates(id, enabled) | (value ? AutomationStates.Checked : AutomationStates.None);
        AddNode(id, AutomationRole.CheckBox, text, bounds, states, toolTip, activate: () => InvokeWidget(id));
    }

    public void Radio(
        string id,
        string text,
        bool value,
        RectangleF bounds,
        Action<bool> changed,
        bool enabled = true)
    {
        const float Diameter = 16;
        var circle = new RectangleF(
            x: bounds.X + 1,
            y: bounds.Y + (bounds.Height - Diameter) / 2,
            width: Diameter,
            height: Diameter
        );

        var outline = new DrawCommand
        {
            Kind = DrawKind.Ellipse,
            Bounds = circle,
            Color = enabled ? Palette.Border : Palette.MutedText,
        };

        commands.Add(outline);
        var inner = RectangleF.Inflate(circle, -1, -1);
        commands.Add(new DrawCommand { Kind = DrawKind.Ellipse, Bounds = inner, Color = Palette.Input });

        if (value)
        {
            var dot = RectangleF.Inflate(circle, -4, -4);
            commands.Add(new DrawCommand { Kind = DrawKind.Ellipse, Bounds = dot, Color = Palette.Accent });
        }

        var textBounds = bounds with { X = bounds.X + Diameter + 7, Width = bounds.Width - Diameter - 7 };
        Label(text, textBounds, enabled ? Palette.Text : Palette.MutedText);
        if (focused == id) Border(RectangleF.Inflate(bounds, -1, -1), Palette.Accent);
        Add(id, bounds, action: () => changed(true), enabled);
        if (enabled) radios.Add(id);
        var states = WidgetStates(id, enabled) | (value ? AutomationStates.Selected : AutomationStates.None);
        AddNode(id, AutomationRole.RadioButton, text, bounds, states, activate: () => InvokeWidget(id));
    }

    public void Combo(
        string id,
        string text,
        IReadOnlyList<string> choices,
        int selectedIndex,
        RectangleF bounds,
        Action<int> changed,
        bool enabled = true,
        bool keepOpen = false)
    {
        // As in WPF, an open list keeps the pointer-over fill, and a focused box keeps the text box's focus border.
        var open = menu?.Id == id;
        var over = IsPointerOver(bounds);
        var border = open ? Palette.ComboOpenBorder
            : over ? Palette.ComboHoverBorder
            : focused == id ? Palette.InputBorderHover
            : Palette.InputBorder;

        Fill(bounds, open || over ? Palette.ComboHover : Palette.Input);
        Border(bounds, border);
        var textBounds = bounds with { X = bounds.X + 6, Width = bounds.Width - 23 };
        var arrowBounds = bounds with { X = bounds.Right - 20, Width = 17 };
        var color = enabled ? Palette.Text : Palette.MutedText;
        Label(text, textBounds, color);
        DropDownArrow(arrowBounds, color);
        var expanded = menu?.Id == id ? AutomationStates.Expanded : AutomationStates.None;
        AddNode(
            id,
            AutomationRole.ComboBox,
            name: "",
            bounds,
            WidgetStates(id, enabled) | expanded,
            value: text,
            activate: () => OpenMenu(id),
            collapse: () => CloseMenu(id));
        Add(
            id,
            bounds,
            () =>
            {
                if (menu?.Id == id)
                {
                    menu = null;
                    return;
                }

                var items = ToMenuChoices(choices);
                menu = new MenuState(
                    id: id,
                    anchor: bounds,
                    choices: items,
                    selected: selectedIndex,
                    changed: changed,
                    keepOpen: keepOpen,
                    kind: MenuKind.ComboBox,
                    width: MenuWidth(MenuKind.ComboBox, items, bounds.Width)
                );
            },
            enabled
        );
    }

    /// <summary>Shows the text as a tooltip while the pointer rests on the bounds; empty text shows none.</summary>
    public void ToolTip(string id, RectangleF bounds, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsAvailable(bounds)) return;

        var area = clip.HasValue ? RectangleF.Intersect(bounds, clip.Value) : bounds;
        if (area is { Width: > 0, Height: > 0 }) toolTips.Add(new ToolTipRegion(id, area, text));
    }

    /// <summary>The topmost tooltip region under the position, or null while a menu is open.</summary>
    public ToolTipRegion? ToolTipAt(PointF position)
    {
        if (menu != null)
        {
            var index = MenuIndexAt(menu, position);
            if (index < 0 || menu.Choices[index] is not { Enabled: true, ToolTip: { Length: > 0 } text }) return null;

            var row = MenuRow(menu, index - menu.Offset);
            return new ToolTipRegion($"menu.{menu.Id}.{index}", row, text);
        }

        for (var i = toolTips.Count - 1; i >= 0; i--)
            if (toolTips[i].Bounds.Contains(position))
                return toolTips[i];

        return null;
    }

    public string Value(string id) => inputs.TryGetValue(id, out var input) ? input.Value : "";

    public void SetValue(string id, string value)
    {
        var input = GetInput(id);
        if (input.Value == value) return;

        input.Value = value;
        Win32.SetWindowText(input.Handle, value);
        Invalidate?.Invoke();
    }

    public void Input(
        string id,
        string placeholder,
        RectangleF bounds,
        Action<string>? changed = null,
        Action? enter = null,
        bool enabled = true,
        bool clear = false,
        string? toolTip = null,
        float trailing = 0,
        string? name = null)
    {
        // A trailing area stays inside the box's frame but outside its text: an editable combo box's arrow goes there.
        var textBounds = bounds with { Width = bounds.Width - trailing };
        var input = GetInput(id);
        input.Changed = changed;
        input.Enter = enter;
        input.Enabled = enabled;
        input.Bounds = textBounds;
        input.Visible = IsAvailable(bounds) && (!clip.HasValue || clip.Value.Contains(bounds));
        input.ShowPlaceholder = input.Value.Length == 0 && placeholder.Length != 0 &&
                                Win32.GetFocus() != input.Handle;

        // Focusing a clipped input would show its native control outside the clip.
        Add(id, textBounds, action: () => FocusInput(input), enabled: enabled && input.Visible);
        if (enabled) ToolTip(id, bounds, toolTip);
        var inputFocus = Win32.GetFocus() == input.Handle ? AutomationStates.Focused : AutomationStates.None;
        AddNode(
            id,
            AutomationRole.Edit,
            name ?? placeholder,
            textBounds,
            WidgetStates(id, enabled) | inputFocus,
            toolTip,
            input.Value,
            activate: () => InvokeWidget(id),
            setText: text => SetValue(id, text));

        Fill(bounds, Palette.Input);
        var active = Win32.GetFocus() == input.Handle || IsPointerOver(bounds);
        Border(bounds, active ? Palette.InputBorderHover : Palette.InputBorder);

        if (input.ShowPlaceholder)
        {
            var watermark = textBounds with { X = textBounds.X + InputTextInset, Width = textBounds.Width - InputTextInset * 2 };
            var color = Theme.Blend(Palette.Input, Palette.Text, PlaceholderOpacity);
            Label(placeholder, watermark, color);
        }

        if (clear && input.Value.Length != 0)
        {
            const float ClearWidth = 24;
            const float ClearIconSize = 10;
            const float ClearStroke = 2;
            input.Bounds = textBounds with { Width = textBounds.Width - ClearWidth };
            var clearBounds = textBounds with { X = textBounds.Right - ClearWidth, Width = ClearWidth };
            Cross(clearBounds, ClearIconSize, ClearStroke, Palette.MutedText);
            var clearId = id + ".clear";
            Add(clearId, clearBounds, action: () => SetValue(id, ""), enabled);
            AddNode(clearId, AutomationRole.Button, name: "Clear", clearBounds, activate: () => InvokeWidget(clearId));
        }

        // Draw the value underneath so it stays visible while the native control is hidden or not yet shown.
        if (!input.ShowPlaceholder && input.Value.Length != 0)
        {
            var area = owner.ControlArea(InputBounds(input.Bounds));

            // Text is centred, so trim one side by twice the baseline difference to align it with the native control.
            // This keeps the text inside the control's area.
            var centredAscent = (area.Height - Painter.LineHeight(FontSize)) / 2 + Painter.Ascent(FontSize);
            var shift = owner.MeasureControlAscent() - centredAscent;
            var valueBounds = shift > 0
                ? area with { Y = area.Y + shift * 2, Height = area.Height - shift * 2 }
                : area with { Height = area.Height + shift * 2 };

            Label(input.Value, valueBounds, Palette.Text, trim: false);
        }
    }

    public bool MouseMove(PointF position)
    {
        var moved = pointer != position;
        pointer = position;
        if (menu is { Scrolls: true } scrolling && scrolling.Bar.MouseMove(position)) return true;

        var next = HitAt(position)?.Id;
        if (next == hovered) return menu != null && moved;

        hovered = next;
        return true;
    }

    public bool MouseDown(PointF position)
    {
        pointer = position;

        if (menu != null)
        {
            if (menu.Scrolls && menu.Bar.MouseDown(position, Environment.TickCount64))
            {
                // The bar scrolled or started a drag; the menu stays open.
            }
            else if (menu.Bounds.Contains(position))
            {
                // As in WPF, clicking a disabled item leaves the menu open.
                Choose(menu, MenuIndexAt(menu, position));
            }
            else
            {
                menu = null;
            }

            return true;
        }

        var widget = HitAt(position);
        pressed = widget?.Id;
        if (widget is { Focusable: false }) return true;

        focused = widget?.Id;
        if (widget == null) return false;

        Win32.SetFocus(owner.Handle);
        return true;
    }

    public bool MouseUp(PointF position)
    {
        if (menu is { Scrolls: true } scrolling) scrolling.Bar.MouseUp();
        var widget = HitAt(position);
        var activate = widget != null && widget.Id == pressed;
        pressed = null;
        if (!activate) return false;

        widget!.Action();
        return true;
    }

    public bool Key(int key, bool shift)
    {
        if (!HasFocusedInput && focused != null && radios.Contains(focused) &&
            key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            var options = widgets.Where(widget => radios.Contains(widget.Id)).ToArray();
            var index = Array.FindIndex(options, widget => widget.Id == focused);
            var direction = key is VirtualKey.Left or VirtualKey.Up ? -1 : 1;
            var next = options[(index + direction + options.Length) % options.Length];
            focused = next.Id;
            next.Action();
            return true;
        }

        if (key == VirtualKey.A && Win32.IsKeyDown(VirtualKey.Control) && HasFocusedInput)
        {
            var focusedInput = Win32.GetFocus();
            Win32.SendMessage(focusedInput, Win32.EmSetSelection, wParam: 0, lParam: -1);
            return true;
        }

        if (key == VirtualKey.Escape && menu != null)
        {
            menu = null;
            return true;
        }

        if (menu is { } active && key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Enter)
        {
            if (key == VirtualKey.Enter)
            {
                Choose(active, active.Selected);
            }
            else
            {
                active.Selected = Math.Clamp(
                    active.Selected + (key == VirtualKey.Up ? -1 : 1),
                    0,
                    Math.Max(0, active.Choices.Count - 1)
                );

                var visible = Math.Max(1, active.VisibleRows);
                active.Offset = Math.Clamp(active.Offset, Math.Max(0, active.Selected - visible + 1), active.Selected);
            }

            return true;
        }

        if (key == VirtualKey.Enter)
        {
            var input = inputs.Values.FirstOrDefault(value => value.Handle == Win32.GetFocus());

            if (input?.Enter != null)
            {
                input.Enter();
                return true;
            }
        }

        if (key == VirtualKey.Tab)
        {
            var currentInput = inputs.FirstOrDefault(entry => entry.Value.Handle == Win32.GetFocus());
            if (currentInput.Value != null) focused = currentInput.Key;
            var stops = widgets.FindAll(widget => widget.Focusable);
            var index = stops.FindIndex(widget => widget.Id == focused);
            var count = stops.Count;
            if (count == 0) return false;

            index = (index + (shift ? count - 1 : 1)) % count;
            focused = stops[index].Id;

            if (inputs.TryGetValue(focused, out var nextInput)) FocusInput(nextInput);
            else Win32.SetFocus(owner.Handle);

            return true;
        }

        // Drawn widgets take keys only while the window itself has keyboard focus, not a native control such as a text
        // input or a dialog's message.
        if (Win32.GetFocus() != owner.Handle) return false;

        if (key is VirtualKey.Enter or VirtualKey.Space && focused != null)
        {
            var widget = widgets.Find(value => value.Id == focused);
            widget?.Action();
            return widget != null;
        }

        return false;
    }

    public bool ScrollMenu(int direction)
    {
        if (menu == null) return false;

        var visible = Math.Max(1, menu.VisibleRows);
        menu.Offset = Math.Clamp(menu.Offset - direction, 0, Math.Max(0, menu.Choices.Count - visible));
        return true;
    }

    public bool ContainsHit(PointF position) => menu != null || HitAt(position) != null;

    /// <summary>Repeats a held menu scroll bar step; true when the menu scrolled.</summary>
    public bool Tick(long now) => menu is { Scrolls: true } scrolling && scrolling.Bar.Repeat(now);

    public bool IsMenuScrolling => menu is { Scrolls: true, Bar.IsInteracting: true };

    /// <summary>Ends a held menu scroll bar press, such as when the window loses the mouse capture.</summary>
    public void EndMenuScroll()
    {
        if (menu is { Scrolls: true } scrolling) scrolling.Bar.MouseUp();
    }

    public void CloseMenu() => menu = null;

    // Automation may ask to open or close a menu again before the next frame shows the first request, so opening and
    // closing change nothing when already done.
    private void OpenMenu(string id)
    {
        if (menu?.Id != id) InvokeWidget(id);
    }

    private void CloseMenu(string id)
    {
        if (menu?.Id == id) menu = null;
    }

    // Runs a widget's action as a click would, when it is still drawn, enabled and uncovered; another widget's open
    // menu closes first.
    private void InvokeWidget(string id)
    {
        if (widgets.Find(candidate => candidate.Id == id) is not { } widget) return;

        if (menu?.Id != id) menu = null;
        widget.Action();
    }

    // Sets a widget's state as automation asks, when it is still drawn and uncovered; repeating it changes nothing.
    private void SetIfAvailable(string id, Action<bool> set, bool value)
    {
        if (widgets.Exists(widget => widget.Id == id)) set(value);
    }

    // Chooses an enabled menu item; a menu that does not stay open closes first.
    private void Choose(MenuState active, int index)
    {
        if (index < 0 || index >= active.Choices.Count || !active.Choices[index].Enabled) return;

        if (!active.KeepOpen) menu = null;
        active.Selected = index;
        active.Changed(index);
    }

    private AutomationStates WidgetStates(string id, bool enabled)
    {
        var states = enabled ? AutomationStates.None : AutomationStates.Disabled;
        return focused == id ? states | AutomationStates.Focused : states;
    }

    // Records an element for UI Automation and returns its index, or -1 when it is covered or scrolled out of view.
    private int AddNode(
        string automationId,
        AutomationRole role,
        string name,
        RectangleF bounds,
        AutomationStates states = AutomationStates.None,
        string? helpText = null,
        string? value = null,
        Action? activate = null,
        Action<string>? setText = null,
        Action? collapse = null)
    {
        // A covered group hides its elements too, rather than leaving them without their row or tile.
        var parent = automationGroups.Count == 0 ? -1 : automationGroups.Peek();
        if (parent == HiddenGroup || !IsAvailable(bounds)) return -1;

        var visible = clip.HasValue ? RectangleF.Intersect(bounds, clip.Value) : bounds;
        if (visible.Width <= 0 || visible.Height <= 0) return -1;

        // Elements without an id, such as text, are keyed by their group and role in drawing order, so text that
        // changes, such as a viewer count, keeps its key and runtime id.
        var scope = parent < 0 ? "" : automation[parent].Key;
        var baseKey = automationId.Length != 0 ? automationId : $"{scope}/{role}";
        var occurrence = automationKeys.GetValueOrDefault(baseKey);
        automationKeys[baseKey] = occurrence + 1;
        var key = occurrence == 0 ? baseKey : $"{baseKey}#{occurrence}";

        var node = new AutomationNode(
            Key: key,
            AutomationId: automationId,
            Role: role,
            Name: name,
            Bounds: visible,
            Parent: parent,
            States: states,
            HelpText: helpText,
            Value: value,
            Activate: activate,
            SetText: setText,
            Collapse: collapse);

        automation.Add(node);
        return automation.Count - 1;
    }

    /// <summary>Leaves keyboard focus on the window, with no widget or text input focused.</summary>
    public void ClearFocus()
    {
        focused = null;
        Win32.SetFocus(owner.Handle);
        Invalidate?.Invoke();
    }

    public void Focus(string id)
    {
        focused = id;

        if (inputs.TryGetValue(id, out var input)) FocusInput(input);
        else Win32.SetFocus(owner.Handle);

        Invalidate?.Invoke();
    }

    /// <summary>
    /// The arrow of an editable combo box: a text input whose trailing area is <paramref name="bounds"/>, inside the
    /// input's <paramref name="frame"/>. The arrow area takes the combo box fill, and the frame the combo box border,
    /// while pointed or while the list known by <paramref name="menuId"/> is open, so the pair looks like a combo box.
    /// </summary>
    public void DropDownButton(
        string id,
        RectangleF bounds,
        RectangleF frame,
        Action action,
        string menuId,
        bool enabled = true,
        string? name = null)
    {
        var open = menu?.Id == menuId;
        var over = IsPointerOver(bounds);
        var inner = new RectangleF(x: bounds.X, y: bounds.Y + 1, width: bounds.Width - 1, height: bounds.Height - 2);
        Fill(inner, open || over ? Palette.ComboHover : Palette.Input);
        if (open || over) Border(frame, open ? Palette.ComboOpenBorder : Palette.ComboHoverBorder);

        DropDownArrow(RectangleF.Inflate(bounds, -3, 0), enabled ? Palette.Text : Palette.MutedText);
        Add(id, bounds, action, enabled);
        AddNode(
            id,
            AutomationRole.Button,
            name ?? "",
            bounds,
            WidgetStates(id, enabled),
            activate: () => InvokeWidget(id));
    }

    /// <summary>Lists choices below the control, as wide as the larger of the control and the widest choice unless the list scrolls.</summary>
    public void DropDown(
        string id,
        RectangleF control,
        IReadOnlyList<string> choices,
        Action<int> chosen) => ShowMenu(id, control, ToMenuChoices(choices), chosen, MenuKind.DropDown);

    /// <summary>Shows a context menu at the position.</summary>
    public void Popup(
        string id,
        PointF position,
        IReadOnlyList<MenuChoice> choices,
        Action<int> chosen)
    {
        var anchor = new RectangleF(x: position.X, y: position.Y, width: 0, height: 0);
        ShowMenu(id, anchor, choices, chosen, MenuKind.ContextMenu);
    }

    private void ShowMenu(
        string id,
        RectangleF anchor,
        IReadOnlyList<MenuChoice> choices,
        Action<int> chosen,
        MenuKind kind)
    {
        menu = new MenuState(
            id: id,
            anchor: anchor,
            choices: choices,
            selected: -1,
            changed: chosen,
            keepOpen: false,
            kind: kind,
            width: MenuWidth(kind, choices, anchor.Width)
        );

        Invalidate?.Invoke();
    }

    // Like a WPF menu, a menu is as wide as its widest choice and at least as wide as its control.
    private float MenuWidth(MenuKind kind, IReadOnlyList<MenuChoice> choices, float controlWidth)
    {
        var style = StyleOf(kind);
        var widest = 0f;
        foreach (var choice in choices) widest = Math.Max(widest, MeasureWidth(choice.Text, style.TextSize));

        return Math.Max(controlWidth, style.TextInset + widest + style.EndPadding);
    }

    private static MenuStyle StyleOf(MenuKind kind) => kind switch
    {
        MenuKind.ComboBox or MenuKind.DropDown => ListMenuStyle,
        MenuKind.ContextMenu => ContextMenuStyle,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private TextInput GetInput(string id)
    {
        if (inputs.TryGetValue(id, out var existing)) return existing;

        var input = new TextInput();
        input.Handle = owner.CreateTextInput(() =>
        {
            input.Value = Win32.GetText(input.Handle);
            input.Changed?.Invoke(input.Value);
            Invalidate?.Invoke();
        });

        inputs.Add(id, input);
        return input;
    }

    private void FocusInput(TextInput input)
    {
        var bounds = InputBounds(input.Bounds);
        owner.PlaceInput(input.Handle, bounds, visible: true, input.Enabled);
        Win32.SetFocus(input.Handle);
        Invalidate?.Invoke();
    }

    private static RectangleF InputBounds(RectangleF bounds)
    {
        var height = Math.Min(bounds.Height - InputInset * 2, Painter.LineHeight(FontSize));
        return new RectangleF(
            x: bounds.X + InputTextInset,
            y: bounds.Y + (bounds.Height - height) / 2,
            width: bounds.Width - InputTextInset * 2,
            height: height);
    }

    private void Add(
        string id,
        RectangleF bounds,
        Action action,
        bool enabled,
        bool focusable = true)
    {
        if (!enabled || !IsAvailable(bounds)) return;

        var hit = clip.HasValue ? RectangleF.Intersect(bounds, clip.Value) : bounds;
        if (hit is { Width: > 0, Height: > 0 }) widgets.Add(new Widget(id, hit, action, focusable));
    }

    private bool IsAvailable(RectangleF bounds) =>
        overlay || !occlusion.HasValue || !occlusion.Value.IntersectsWith(bounds);

    private Widget? HitAt(PointF position)
    {
        if (menu != null) return null;

        for (var i = widgets.Count - 1; i >= 0; i--)
            if (widgets[i].Bounds.Contains(position))
                return widgets[i];

        return null;
    }

    private void LayoutMenu(MenuState active)
    {
        const int MaximumVisibleMenuRows = 10;
        const float EdgeMargin = 5;
        var insets = 2 * active.Style.Inset;

        // A list is limited to WPF's combo box list height and to the larger space below or above its control, which it
        // does not cover; a context menu shows up to ten rows anywhere in the window. Either shows whole rows only.
        var limit = active.IsList ? ListMaximumHeight : MaximumVisibleMenuRows * active.RowHeight + insets;
        var spaceBelow = viewport.Bottom - active.Anchor.Bottom - 1;
        var spaceAbove = active.Anchor.Top - viewport.Top - EdgeMargin;
        var space = active.IsList ? Math.Max(spaceBelow, spaceAbove) : viewport.Height - 2 * EdgeMargin;
        var fittingRows = (int)((Math.Min(limit, space) - insets) / active.RowHeight);
        var rows = Math.Min(active.Choices.Count, Math.Max(1, fittingRows));

        // Once more rows fit, as when the window grows, the first shown choice moves up rather than leaving rows empty.
        active.Offset = Math.Clamp(active.Offset, 0, active.Choices.Count - rows);

        // As in WPF, a list that overflows gets a scroll bar beside its choices and stays as wide as its control, clipping
        // longer choices; a context menu widens by the bar instead.
        active.Scrolls = active.Choices.Count > rows;
        var barWidth = active.Scrolls ? VerticalScrollBar.Width : 0;
        var naturalWidth = active is { Scrolls: true, IsList: true } ? active.Anchor.Width : active.Width + barWidth;
        var width = Math.Min(naturalWidth, viewport.Width - 4);
        var height = rows * active.RowHeight + insets;
        var top = active.Anchor.Bottom + 1;
        if (top + height > viewport.Bottom) top = Math.Max(EdgeMargin, active.Anchor.Top - height);
        var left = Math.Min(active.Anchor.Left, viewport.Right - width - 2);
        active.Bounds = new RectangleF(x: left, y: top, width: width, height: height);
    }

    private void DrawMenu()
    {
        // The menu is drawn above everything, including covered areas.
        overlay = true;
        var active = menu!;
        var left = active.Bounds.Left;
        var width = active.Bounds.Width;
        var rows = active.VisibleRows;
        var style = active.Style;
        var barWidth = active.Scrolls ? VerticalScrollBar.Width : 0;
        var textWidth = width - style.TextInset - style.EndPadding - barWidth;
        Fill(active.Bounds, active.IsList ? Palette.Background : Palette.Panel);
        Border(active.Bounds, active.IsList ? Palette.ListBorder : Palette.Border);

        if (active.Scrolls)
        {
            var barBounds = new RectangleF(
                x: active.Bounds.Right - style.Inset - barWidth,
                y: active.Bounds.Top + style.Inset,
                width: barWidth,
                height: active.Bounds.Height - 2 * style.Inset);

            active.Bar.Update(barBounds, extent: active.Choices.Count, viewport: rows, offset: active.Offset);
            active.Bar.Draw(this);
        }

        // A combo box's choices are its own children; a popup menu is an element of its own.
        var container = active.IsComboBox
            ? automation.FindIndex(node => node.Key == active.Id)
            : AddNode(active.Id, AutomationRole.Menu, name: "", active.Bounds);

        automationGroups.Push(container);

        for (var i = 0; i < rows; i++)
        {
            var index = active.Offset + i;
            if (index >= active.Choices.Count) break;

            var choice = active.Choices[index];
            var row = MenuRow(active, i);
            var selected = choice.Enabled && index == active.Selected;
            var over = choice.Enabled && row.Contains(pointer);

            // A list fills its selected choice with the selection color and the pointed row with the tile color, as
            // the WPF combo box list does; a context menu highlights the pointed or keyboard-selected item alike.
            if (active.IsList)
            {
                if (selected) Fill(row, Palette.Selection);
                else if (over) Fill(row, Palette.Tile);
            }
            else if (selected || over)
            {
                Fill(row, Palette.Hover);
            }

            var checkBounds = row with { X = left + 5, Width = 19 };
            var textBounds = row with { X = left + style.TextInset, Width = textWidth };
            var color = !choice.Enabled ? Palette.MutedText : active.IsList && selected ? Palette.SelectionText : Palette.Text;

            // A checked item's tick is centred in its own column, so labels line up whether or not an item is ticked,
            // and uses the accent, as WPF menu items do.
            if (choice.Checked) Icon(text: "✓", checkBounds, Palette.Accent);

            // As in WPF, a list clips a choice longer than its control; a context menu is as wide as its longest item.
            Label(choice.Text, textBounds, color, style.TextSize, trim: !active.IsList);
            AddChoiceNode(active, index, row);
        }

        automationGroups.Pop();
    }

    private void AddChoiceNode(MenuState active, int index, RectangleF row)
    {
        var choice = active.Choices[index];
        var states = choice.Enabled ? AutomationStates.None : AutomationStates.Disabled;
        if (choice.Checked) states |= AutomationStates.Checked;
        if (choice.Checkable) states |= AutomationStates.Checkable;
        if (active.IsComboBox && index == active.Selected) states |= AutomationStates.Selected;
        var role = active.IsComboBox ? AutomationRole.ListItem : AutomationRole.MenuItem;

        AddNode(
            $"{active.Id}.choice.{index}",
            role,
            choice.Text,
            row,
            states,
            choice.ToolTip,
            activate: ChooseIfOpen);

        // The menu may have closed or been replaced by the time the action runs.
        void ChooseIfOpen()
        {
            if (menu == active) Choose(active, index);
        }
    }

    private static RectangleF MenuRow(MenuState active, int visibleRow) => new(
        x: active.Bounds.Left + active.Style.Inset,
        y: active.Bounds.Top + active.Style.Inset + visibleRow * active.RowHeight,
        width: active.Bounds.Width - 2 * active.Style.Inset - (active.Scrolls ? VerticalScrollBar.Width : 0),
        height: active.RowHeight);

    /// <summary>The choice index under the position, or -1 outside the choices.</summary>
    private static int MenuIndexAt(MenuState active, PointF position)
    {
        if (!active.Bounds.Contains(position)) return -1;
        if (active.Scrolls && position.X >= active.Bar.Bounds.Left) return -1;

        var row = (int)Math.Floor((position.Y - active.Bounds.Y - active.Style.Inset) / active.RowHeight);
        if (row < 0 || row >= active.VisibleRows) return -1;

        var index = active.Offset + row;
        return index < active.Choices.Count ? index : -1;
    }

    private static MenuChoice[] ToMenuChoices(IReadOnlyList<string> choices)
    {
        var items = new MenuChoice[choices.Count];
        for (var i = 0; i < items.Length; i++) items[i] = new MenuChoice(choices[i]);

        return items;
    }

    /// <summary>A clickable widget; one that is not focusable leaves the focus where it was and is no tab stop.</summary>
    private sealed record Widget(string Id, RectangleF Bounds, Action Action, bool Focusable);

    internal sealed record ToolTipRegion(string Id, RectangleF Bounds, string Text);

    /// <summary>A menu item: a disabled item is drawn muted and cannot be chosen.</summary>
    internal readonly record struct MenuChoice(
        string Text,
        bool Enabled = true,
        bool Checked = false,
        string? ToolTip = null,
        bool Checkable = false);

    private sealed class TextInput
    {
        public nint Handle;
        public string Value = "";
        public bool ShowPlaceholder;
        public RectangleF Bounds;
        public bool Visible;
        public bool Enabled;
        public Action<string>? Changed;
        public Action? Enter;
    }

    private enum MenuKind
    {
        ComboBox,
        DropDown,
        ContextMenu,
    }

    // Rows start Inset inside the menu bounds on every side: the border and, for a list, the padding within it.
    private readonly record struct MenuStyle(
        float RowHeight,
        float TextInset,
        float EndPadding,
        float Inset,
        float TextSize = FontSize);

    private sealed class MenuState(
        string id,
        RectangleF anchor,
        IReadOnlyList<MenuChoice> choices,
        int selected,
        Action<int> changed,
        bool keepOpen,
        MenuKind kind,
        float width)
    {
        public string Id { get; } = id;
        public RectangleF Anchor { get; } = anchor;
        public IReadOnlyList<MenuChoice> Choices { get; } = choices;
        public int Selected = selected;
        public Action<int> Changed { get; } = changed;
        public bool KeepOpen { get; } = keepOpen;
        public float Width { get; } = width;
        public MenuStyle Style { get; } = StyleOf(kind);
        public float RowHeight => Style.RowHeight;

        /// <summary>True for a combo box, whose choices are its own automation children and whose selected choice is its value.</summary>
        public bool IsComboBox { get; } = kind == MenuKind.ComboBox;

        /// <summary>True for a combo box or drop-down list, which looks like the WPF combo box list rather than a menu.</summary>
        public bool IsList { get; } = kind != MenuKind.ContextMenu;

        /// <summary>The rows the laid-out bounds show at once.</summary>
        public int VisibleRows => (int)Math.Round((Bounds.Height - 2 * Style.Inset) / RowHeight);

        public int Offset;
        public RectangleF Bounds;

        /// <summary>True when the choices overflow the visible rows, so a scroll bar is shown beside them.</summary>
        public bool Scrolls;

        public VerticalScrollBar Bar => field ??= new VerticalScrollBar(offset => Offset = offset);
    }
}
