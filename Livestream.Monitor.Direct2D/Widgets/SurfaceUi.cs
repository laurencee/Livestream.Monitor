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
    private static readonly MenuStyle ListMenuStyle = new(RowHeight: 22, TextInset: 9, EndPadding: 8, Inset: 2);
    private static readonly MenuStyle ContextMenuStyle = new(RowHeight: 30.5f, TextInset: 32, EndPadding: 37, Inset: 1, TextSize: 14);

    // On the automation group stack in place of a group that is covered or scrolled out of view.
    private const int HiddenGroup = -2;

    // Button texts drawn as centred glyphs rather than labels.
    private static readonly string[] IconTexts = ["›", "‹", "◀", "▶", "↑", "↓", "×", "+", "↻", "⚙", "▴", "▲", "▼"];
    private readonly List<Widget> widgets = [];
    private readonly Dictionary<string, TextInput> inputs = [];

    // For each editable combo box, keyed by the id of its text input: its drop-down button and the list that button shows,
    // as last drawn.
    private readonly Dictionary<string, (string Button, string Menu)> inputDropDowns = [];
    private readonly List<ToolTipRegion> toolTips = [];
    private readonly List<AutomationNode> automation = [];
    private readonly Dictionary<string, int> automationKeys = [];
    private readonly Stack<int> automationGroups = new();
    private AutomationTree automationTree = AutomationTree.Empty;
    private string? hovered;
    private string? pressed;
    private string? focused;
    private string? deferredInputFocus;
    private bool focusVisible;
    private MenuState? menu;
    private RectangleF viewport;
    private RectangleF? clip;
    private RectangleF? occlusion;
    private PointF pointer;
    private bool overlay;
    private bool modal;
    private int focusScopeStart;
    private int focusScopeEnd;

    public ThemePalette Palette { get; private set; } = null!;

    /// <summary>False while another window is active, when the title bar and border are drawn gray as in WPF.</summary>
    public bool IsWindowActive { get; set; } = true;

    public uint TitleBarColor => IsWindowActive ? Palette.TitleBar : Palette.InactiveChrome;

    /// <summary>True while a drawn widget or native text input has the keyboard focus.</summary>
    public bool HasFocusedWidget => focused != null || HasFocusedInput;
    public bool HasFocus(string id) => focused == id;
    public bool HasFocusIn(string idPrefix) => focused?.StartsWith(idPrefix, StringComparison.Ordinal) == true;
    public bool HasMenu => menu != null;

    /// <summary>
    /// True while a menu item chosen with the keyboard, or a combo box's current choice, has the focus in its open menu, as in
    /// WPF, unless a native text input keeps it while its suggestions are browsed.
    /// </summary>
    public bool MenuHasFocus => menu is { Selected: >= 0 } && !HasFocusedInput;

    /// <summary>True while an open context menu, or the focused combo box's open list, moves its highlight with Tab, as in WPF.</summary>
    public bool MenuTakesTab => menu is { IsList: false } || menu is { IsComboBox: true } && menu.Id == focused && !HasFocusedInput;

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
        modal = false;
        focusScopeStart = -1;
        focusScopeEnd = -1;
        clip = null;
        ClearFrame();
        owner.ApplyControlTheme(palette);
        if (menu != null) LayoutMenu(menu);
    }

    public void Overlay() => overlay = true;

    /// <summary>
    /// Starts the widgets that Tab cycles among, which is how a modal pane keeps the focus inside it. A modal dialog drawn
    /// over them has its own tab stops.
    /// </summary>
    public void BeginFocusScope() => focusScopeStart = widgets.Count;

    public void EndFocusScope() => focusScopeEnd = widgets.Count;

    public void ModalOverlay()
    {
        overlay = true;
        modal = true;
        menu = null;
        ClearFrame();
    }

    // Forgets the widgets drawn so far, so only those drawn next can be clicked, focused or read.
    private void ClearFrame()
    {
        widgets.Clear();
        toolTips.Clear();
        automation.Clear();
        automationKeys.Clear();
        automationGroups.Clear();
        inputDropDowns.Clear();
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
            var inner = InputBounds(input.Bounds);
            owner.PlaceInput(input.Handle, inner, ShowsNativeInput(input), input.Enabled);
        }

        Volatile.Write(ref automationTree, new AutomationTree(automation.ToArray()));

        // A deferred focus waits for a frame that lays its input out; a frame does not lay out an input that is under another
        // dialog. A later focus change, e.g. a click, Tab or that dialog's own focus, replaces the deferred one.
        if (deferredInputFocus is { } deferred && deferred != focused) deferredInputFocus = null;

        if (deferredInputFocus is { } pending && Automation.IndexOf(pending) >= 0)
        {
            deferredInputFocus = null;
            FocusNativeInput(pending, inputs[pending]);
        }
    }

    internal float FindYClearOfNativeInputs(RectangleF bounds, float top, float bottom)
    {
        var lowerY = MovePastInputs(below: true);
        if (lowerY + bounds.Height <= bottom) return lowerY;

        var above = MovePastInputs(below: false);
        return above >= top ? above : bounds.Y;

        float MovePastInputs(bool below)
        {
            var candidate = bounds;
            bool moved;
            do
            {
                moved = false;
                foreach (var input in inputs.Values)
                {
                    if (!ShowsNativeInput(input) || !candidate.IntersectsWith(input.Bounds)) continue;
                    var nextY = below ? input.Bounds.Bottom : input.Bounds.Top - candidate.Height;
                    if (below ? nextY <= candidate.Y : nextY >= candidate.Y) continue;

                    candidate.Y = nextY;
                    moved = true;
                }
            } while (moved);

            return candidate.Y;
        }
    }

    private bool ShowsNativeInput(TextInput input) => !input.ShowPlaceholder && CanShowNativeInput(input);

    // Whether the native control may show, as it does once focused even while the placeholder shows.
    private bool CanShowNativeInput(TextInput input) =>
        input is { Visible: true, Enabled: true } && (menu == null || !input.Bounds.IntersectsWith(menu.Bounds));

    /// <summary>
    /// Starts a group, such as a list row or tile, whose elements become its automation children. A key identifies the
    /// group across frames, such as by the stream a row shows; without one, the group is keyed by its id.
    /// </summary>
    public void BeginGroup(
        string id,
        AutomationRole role,
        string name,
        RectangleF bounds,
        AutomationStates states = AutomationStates.None,
        Action? activate = null,
        string? key = null,
        AutomationItem? item = null)
    {
        var group = AddNode(id, role, name, bounds, states, activate: activate, contentKey: key, item: item);
        automationGroups.Push(group < 0 ? HiddenGroup : group);
    }

    /// <summary>
    /// Starts a list of rows or tiles, ended with <see cref="EndList"/>. Unlike other elements, it stays exposed while
    /// partly covered, and so do its uncovered items. The selected key is the key of its selected item, drawn or not.
    /// A content key can distinguish lists that share an automation id.
    /// </summary>
    public void BeginList(
        string id,
        string name,
        RectangleF bounds,
        string? selectedKey,
        Action focus,
        string? contentKey = null)
    {
        var list = AddNode(id, AutomationRole.List, name, bounds, value: selectedKey, activate: focus, contentKey: contentKey, partlyCoverable: true);
        automationGroups.Push(list < 0 ? HiddenGroup : list);
        if (list < 0) return;

        var key = automation[list].Key;
        widgets.Add(new Widget(Id: key, Bounds: RectangleF.Empty, Action: focus, Focusable: true, RadioGroup: null, FocusOnly: true));
    }

    public void EndList()
    {
        var index = automationGroups.Pop();
        if (index == HiddenGroup) return;

        var list = automation[index];
        if (focused != list.Key || MenuHasFocus || list.Value is { } selected && automationKeys.ContainsKey(selected)) return;

        automation[index] = list with { States = AutomationStates.Focused };
        DrawFocus(list.Key, list.Bounds);
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
        float size = FontSize,
        bool systemSymbol = false)
    {
        commands.Add(new DrawCommand
        {
            Kind = DrawKind.Icon,
            Bounds = bounds,
            Color = color,
            Text = text,
            Style = new TextStyle(Size: size, Align: TextAlign.Center, Trim: !systemSymbol, SystemSymbol: systemSymbol),
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
        Action<bool> setExpanded,
        string? status = null,
        uint? statusColor = null)
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

        const float PressedTint = 0.2f;
        var over = IsPointerOver(bounds);
        var ring = over ? Palette.ChoiceAccent : Palette.Text;
        var fill = over && pressed == id ? Theme.Blend(Palette.Panel, Palette.ChoiceAccent, PressedTint) : Palette.Panel;
        commands.Add(new DrawCommand { Kind = DrawKind.Ellipse, Bounds = circle, Color = ring });
        var inner = RectangleF.Inflate(circle, -Stroke, -Stroke);
        commands.Add(new DrawCommand { Kind = DrawKind.Ellipse, Bounds = inner, Color = fill });
        var left = circle.X + ArrowInset;
        var right = circle.Right - ArrowInset;
        var center = circle.Y + Diameter / 2;
        var edge = expanded ? center + ArrowHalfHeight : center - ArrowHalfHeight;
        var tip = expanded ? center - ArrowHalfHeight : center + ArrowHalfHeight;
        var middle = new PointF(circle.X + Diameter / 2, tip);
        Line(new PointF(left, edge), middle, Palette.Text);
        Line(middle, new PointF(right, edge), Palette.Text);
        var labelOffset = Diameter + LabelGap;
        var label = bounds with { X = bounds.X + labelOffset, Width = bounds.Width - labelOffset };
        const float LabelSize = 14;
        Label(text, label, size: LabelSize);
        if (status != null)
        {
            var statusOffset = MeasureWidth(text, LabelSize) + LabelGap;
            var statusBounds = label with { X = label.X + statusOffset, Width = Math.Max(0, label.Width - statusOffset) };
            Label(status, statusBounds, statusColor, size: LabelSize, weight: FontWeight.Bold);
        }

        DrawFocus(id, bounds);
        Add(id, bounds, action: () => setExpanded(!expanded), enabled: true);
        var states = WidgetStates(id, enabled: true) | AutomationStates.Expandable;
        if (expanded) states |= AutomationStates.Expanded;
        AddNode(
            id,
            AutomationRole.Button,
            status == null ? text : $"{text} {status}",
            bounds,
            states,
            activate: () => SetIfAvailable(id, setExpanded, value: true),
            collapse: () => SetIfAvailable(id, setExpanded, value: false));
    }

    /// <summary>
    /// A MahApps button with rounded corners. A <paramref name="chrome"/> button is a window command drawn on the title
    /// bar; a <paramref name="flat"/> button is square. A <paramref name="toggled"/> button switches a setting on (true) and
    /// off (false).
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
        bool focusable = true,
        string? symbol = null,
        bool showToolTipWhenDisabled = false,
        bool? toggled = null)
    {
        const float CornerRadius = 3;
        var radius = flat ? 0 : CornerRadius;
        var over = enabled && IsPointerOver(bounds);
        var isPressed = enabled && pressed == id;
        var background = !enabled ? Palette.ControlDisabled : isPressed ? Palette.Pressed : over ? Palette.Hover : Palette.Control;
        const float AccentHoverOpacity = 0.15f;
        if (enabled && accent) background = over ? Theme.Blend(Palette.Accent, Palette.AccentText, AccentHoverOpacity) : Palette.Accent;

        if (chrome)
        {
            if (over) Fill(bounds, Theme.Blend(TitleBarColor, Palette.TitleText, 0.18f));
            if (enabled && focusable) DrawFocus(id, bounds, TitleBarColor);
        }
        else
        {
            // As in WPF, a pressed or focused button has a border in the text colour.
            var border = !enabled ? Palette.ControlDisabledBorder
                : isPressed || focused == id ? Palette.Text
                : over ? Palette.ControlBorderHover
                : Palette.ControlBorder;

            Fill(bounds, background, radius);
            Border(bounds, border, radius: radius);
        }

        // WPF window commands are drawn at half opacity until the pointer is over them.
        const float CommandTextOpacity = 0.5f;
        var commandText = over ? Palette.TitleText : Theme.Blend(TitleBarColor, Palette.TitleText, CommandTextOpacity);
        var color = enabled
            ? foreground ?? (chrome ? commandText : accent ? Theme.ContrastingText(background) : Palette.Text)
            : chrome ? Palette.MutedText : Palette.ControlDisabledText;

        if (symbol != null)
        {
            const float SymbolSize = 16;
            var padding = (bounds.Height - SymbolSize) / 2;
            var iconBounds = new RectangleF(x: bounds.X + padding, y: bounds.Y + padding, width: SymbolSize, height: SymbolSize);
            var textLeft = iconBounds.Right + padding;
            var label = bounds with { X = textLeft, Width = bounds.Right - textLeft - padding };
            Icon(symbol, iconBounds, color, size: SymbolSize, systemSymbol: true);
            Label(text, label, color, size: size, weight: weight);
        }
        else
        {
            var textBounds = RectangleF.Inflate(bounds, -3, 0);

            if (text == "▾") DropDownArrow(textBounds, color);
            else if (Array.IndexOf(IconTexts, text) >= 0) Icon(text, textBounds, color, size);
            else Label(text, textBounds, color, size: size, weight: weight, alignment: TextAlign.Center);
        }

        Add(id, bounds, action, enabled, focusable);
        if (enabled || showToolTipWhenDisabled) ToolTip(id, bounds, toolTip);
        var states = WidgetStates(id, enabled) | (focusable ? AutomationStates.None : AutomationStates.Unfocusable);
        if (toggled.HasValue) states |= AutomationStates.Checkable;
        if (toggled == true) states |= AutomationStates.Checked;
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
        DrawFocus(id, bounds);
        Add(id, bounds, action, enabled: true);
        var states = WidgetStates(id, enabled: true);
        AddNode(id, AutomationRole.Button, name, bounds, states, activate: () => InvokeWidget(id));
    }

    public void AddAutomationButton(
        string id,
        string name,
        RectangleF bounds,
        Action action,
        bool enabled)
    {
        var states = WidgetStates(id, enabled) | AutomationStates.Unfocusable;
        var index = AddNode(id, AutomationRole.Button, name, bounds, states, activate: () => InvokeWidget(id));

        // The owner handles pointer interaction for this button.
        if (enabled && index >= 0) widgets.Add(new Widget(Id: id, Bounds: RectangleF.Empty, Action: action, Focusable: false, RadioGroup: null));
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
        var fill = Palette.IsDark && value ? Palette.Accent : Palette.Input;
        var border = !enabled ? Palette.ChoiceDisabledBorder
            : !Palette.IsDark && (pressed == id || IsPointerOver(bounds)) ? Palette.ChoiceBorderHover
            : Palette.ChoiceBorder;

        Fill(box, fill);
        Border(box, border);

        if (value)
        {
            const float MarkInset = 3;
            const float MarkThickness = 2;
            var start = new PointF(box.Left + MarkInset, box.Top + BoxSize / 2);
            var corner = new PointF(box.Left + MarkInset * 2, box.Bottom - MarkInset);
            var end = new PointF(box.Right - MarkInset, box.Top + MarkInset);
            var color = enabled ? Theme.ContrastingText(fill) : Palette.IsDark ? Palette.AccentText : Palette.AccentBrush;

            Line(start, corner, color, MarkThickness);
            Line(corner, end, color, MarkThickness);
        }

        var textBounds = bounds with { X = bounds.X + 19, Width = bounds.Width - 19 };
        Label(text, textBounds, enabled ? Palette.Text : Palette.ChoiceDisabledText);
        if (enabled) DrawFocus(id, bounds);
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
        string group,
        bool enabled = true)
    {
        const float Diameter = 16;
        var circle = new RectangleF(
            x: bounds.X + 1,
            y: bounds.Y + (bounds.Height - Diameter) / 2,
            width: Diameter,
            height: Diameter
        );

        var over = IsPointerOver(bounds);
        var border = !enabled ? Palette.ChoiceDisabledBorder
            : over && pressed == id ? Palette.ChoiceAccent
            : over ? Palette.ChoiceBorderHover
            : Palette.ChoiceBorder;
        var outline = new DrawCommand
        {
            Kind = DrawKind.Ellipse,
            Bounds = circle,
            Color = border,
        };

        commands.Add(outline);
        var inner = RectangleF.Inflate(circle, -1, -1);
        commands.Add(new DrawCommand { Kind = DrawKind.Ellipse, Bounds = inner, Color = Palette.Input });

        if (value)
        {
            var dot = RectangleF.Inflate(circle, -4, -4);
            var dotColor = enabled ? Palette.ChoiceAccent : Palette.IsDark ? Palette.Accent : Palette.RowHover;
            commands.Add(new DrawCommand { Kind = DrawKind.Ellipse, Bounds = dot, Color = dotColor });
        }

        var textBounds = bounds with { X = bounds.X + Diameter + 7, Width = bounds.Width - Diameter - 7 };
        Label(text, textBounds, enabled ? Palette.Text : Palette.ChoiceDisabledText);
        if (enabled) DrawFocus(id, bounds);
        Add(id, bounds, action: () => changed(true), enabled, radioGroup: group);
        var states = WidgetStates(id, enabled) | (value ? AutomationStates.Selected : AutomationStates.None);
        AddNode(id, AutomationRole.RadioButton, text, bounds, states, activate: () => InvokeWidget(id));
    }

    /// <summary>
    /// A combo box showing <paramref name="text"/>, usually its choice. <paramref name="name"/> tells UI Automation what the
    /// box chooses; the selected choice is its UI Automation value.
    /// </summary>
    public void Combo(
        string id,
        string name,
        string text,
        IReadOnlyList<string> choices,
        int selectedIndex,
        RectangleF bounds,
        Action<int> changed,
        bool enabled = true,
        bool keepOpen = false)
    {
        if (!enabled) CloseMenu(id);

        // The bound value can change while the list is open, e.g. when a stream coming online resets the platform filter.
        if (menu is { } list && list.Id == id) list.Current = selectedIndex;

        // As in WPF, an open list keeps the pointer-over fill, and a focused box keeps the text box's focus border.
        var open = menu?.Id == id;
        var over = enabled && IsPointerOver(bounds);
        var hasFocus = enabled && focused == id;
        var border = !enabled ? Palette.InputDisabledBorder
            : open ? Palette.ComboOpenBorder
            : hasFocus && !Palette.IsDark ? Palette.InputBorderFocus
            : over ? Palette.ComboHoverBorder
            : hasFocus ? Palette.InputBorderFocus
            : Palette.InputBorder;

        Fill(bounds, !enabled ? Palette.InputDisabled : open || over ? Palette.ComboHover : Palette.Input);
        Border(bounds, border);
        var textBounds = bounds with { X = bounds.X + 6, Width = bounds.Width - 23 };
        var arrowBounds = bounds with { X = bounds.Right - 20, Width = 17 };
        var color = enabled ? Palette.Text : Palette.InputDisabledText;
        Label(text, textBounds, color);
        DropDownArrow(arrowBounds, color);
        if (enabled) DrawFocus(id, bounds);
        var expanded = menu?.Id == id ? AutomationStates.Expanded : AutomationStates.None;
        var value = selectedIndex >= 0 && selectedIndex < choices.Count ? choices[selectedIndex] : text;
        AddNode(
            id,
            AutomationRole.ComboBox,
            name,
            bounds,
            WidgetStates(id, enabled) | expanded,
            value: value,
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
            enabled,
            step: StepChoice
        );

        void StepChoice(int direction)
        {
            var next = Math.Clamp(selectedIndex + direction, 0, choices.Count - 1);
            if (next != selectedIndex) changed(next);
        }
    }

    /// <summary>Shows the text as a tooltip while the pointer rests on the bounds; empty text shows none.</summary>
    public void ToolTip(string id, RectangleF bounds, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsAvailable(bounds)) return;

        var area = clip.HasValue ? RectangleF.Intersect(bounds, clip.Value) : bounds;
        if (area is { Width: > 0, Height: > 0 }) toolTips.Add(new ToolTipRegion(id, area, text));
    }

    /// <summary>The topmost tooltip under the position, restricted to the menu while it is open.</summary>
    public ToolTipRegion? ToolTipAt(PointF position)
    {
        if (menu != null)
        {
            var index = MenuIndexAt(menu, position);
            if (index < 0) return null;

            var choice = menu.Choices[index];
            var text = choice.ToolTip;
            if (choice is { Enabled: false, ShowToolTipWhenDisabled: false } || string.IsNullOrEmpty(text)) return null;

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

    /// <summary>
    /// A text input. <paramref name="name"/> says what it is for. <paramref name="placeholder"/> says what to type and is
    /// shown while the input is empty; UI Automation reads it as help text when there is no tooltip.
    /// </summary>
    public void Input(
        string id,
        string name,
        string placeholder,
        RectangleF bounds,
        Action<string>? changed = null,
        Action? enter = null,
        bool enabled = true,
        bool clear = false,
        string? toolTip = null,
        float trailing = 0,
        Action? lostFocus = null)
    {
        // A trailing area stays inside the box's frame but outside its text: an editable combo box's arrow goes there.
        var textBounds = bounds with { Width = bounds.Width - trailing };
        var input = GetInput(id);
        var showsClear = clear && input.Value.Length != 0;
        input.Changed = changed;
        input.Enter = enter;
        input.LostFocus = lostFocus;
        input.Enabled = enabled;
        const float ClearWidth = 24;
        input.Bounds = showsClear ? textBounds with { Width = textBounds.Width - ClearWidth } : textBounds;
        input.Visible = IsAvailable(bounds) && (!clip.HasValue || clip.Value.Contains(bounds));
        var hasFocus = enabled && Win32.GetFocus() == input.Handle;
        input.ShowPlaceholder = input.Value.Length == 0 && placeholder.Length != 0 && !hasFocus;

        // Focusing a clipped input would show its native control outside the clip.
        Add(id, textBounds, action: () => FocusInput(id, input), enabled: enabled && input.Visible);
        if (enabled) ToolTip(id, bounds, toolTip);
        var helpText = toolTip ?? (placeholder.Length != 0 ? placeholder : null);

        // While the native control is shown, the element is hosted on it: one element with the element's name, id and help
        // text, and the control's Text pattern, focus and bounds. While it is hidden, e.g. when the placeholder is shown, the
        // drawn element stands in for it.
        var inputFocus = hasFocus ? AutomationStates.Focused : AutomationStates.None;
        AddNode(
            id,
            AutomationRole.Edit,
            name,
            textBounds,
            WidgetStates(id, enabled) | inputFocus,
            helpText,
            input.Value,
            activate: () => InvokeWidget(id),
            setText: text => SetValue(id, text),
            window: ShowsNativeInput(input) ? input.Handle : 0);

        Fill(bounds, enabled ? Palette.Input : Palette.InputDisabled);
        var border = !enabled ? Palette.InputDisabledBorder
            : hasFocus ? (focusVisible ? Palette.Text : Palette.InputBorderFocus)
            : IsPointerOver(bounds) ? Palette.InputBorderHover
            : Palette.InputBorder;

        Border(bounds, border);

        if (input.ShowPlaceholder)
        {
            var watermark = textBounds with { X = textBounds.X + InputTextInset, Width = textBounds.Width - InputTextInset * 2 };
            var color = enabled ? Theme.Blend(Palette.Input, Palette.Text, PlaceholderOpacity) : Palette.InputDisabledText;
            Label(placeholder, watermark, color);
        }

        if (showsClear)
        {
            const float ClearIconSize = 10;
            const float ClearStroke = 2;
            var clearBounds = textBounds with { X = textBounds.Right - ClearWidth, Width = ClearWidth };
            Cross(clearBounds, ClearIconSize, ClearStroke, enabled ? Palette.MutedText : Palette.InputDisabledText);
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

            Label(input.Value, valueBounds, enabled ? Palette.Text : Palette.InputDisabledText, trim: false);
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
        focusVisible = false;

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
            else if (DropDownInputAt(position) is { } inputId)
            {
                // As in a WPF editable combo box, a press on a drop-down list's own text input keeps the list open.
                focused = inputId;
                FocusInput(inputId, inputs[inputId]);
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
        // An open menu keeps the arrow keys from a focused radio button, as in WPF.
        if (!HasFocusedInput && focused != null && menu == null &&
            key is (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down) &&
            widgets.Find(widget => widget.Id == focused) is { RadioGroup: { } group } selected)
        {
            var options = widgets.Where(widget => widget.RadioGroup == group).ToArray();
            var index = Array.IndexOf(options, selected);
            var direction = key is VirtualKey.Left or VirtualKey.Up ? -1 : 1;
            var next = options[(index + direction + options.Length) % options.Length];
            focused = next.Id;
            focusVisible = true;
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

        if (key == VirtualKey.Tab && MenuTakesTab) key = shift ? VirtualKey.Up : VirtualKey.Down;

        if (menu is { } active && key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Enter)
        {
            if (key == VirtualKey.Enter)
            {
                Choose(active, active.Selected);
            }
            else if (NextChoice(active, key == VirtualKey.Up ? -1 : 1) is var index and >= 0)
            {
                active.Selected = index;
                var visible = Math.Max(1, active.VisibleRows);
                active.Offset = Math.Clamp(active.Offset, Math.Max(0, index - visible + 1), index);
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
            var stops = TabStops();
            var index = stops.FindIndex(widget => widget.Id == focused);
            var count = stops.Count;
            if (count == 0) return false;

            // With no stop focused, Tab starts at the first stop and Shift+Tab at the last.
            if (index < 0) index = shift ? count - 1 : 0;
            else index = (index + (shift ? count - 1 : 1)) % count;

            // The focus moves on and closes an open list without a choice, as WPF closes a combo box list that loses the focus.
            menu = null;
            var stop = stops[index];
            if (stop.FocusOnly) stop.Action();
            else Focus(stop.Id);

            return true;
        }

        // An editable combo box's text input takes F4 as the combo box does.
        if (key == VirtualKey.F4 && ToggleFocusedComboBox()) return true;

        // Drawn widgets take keys only while the window itself has keyboard focus, not a native control such as a text
        // input or a dialog's message.
        if (Win32.GetFocus() != owner.Handle) return false;

        // A closed combo box steps through its choices with Up and Down, as in Win32.
        if (key is VirtualKey.Up or VirtualKey.Down && FocusedWidget() is { Step: { } step })
        {
            focusVisible = true;
            step(key == VirtualKey.Up ? -1 : 1);
            return true;
        }

        // An open menu keeps Space from the focused widget, and a combo box ignores Space, as in WPF.
        if (key is VirtualKey.Enter or VirtualKey.Space && focused != null && menu == null)
        {
            var widget = widgets.Find(value => value.Id == focused);
            if (widget is { FocusOnly: true } || key == VirtualKey.Space && widget is { Step: not null }) return false;

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

    /// <summary>The row height of an open, overflowing menu with choices at the position, which a touch drag pans as in WPF; null elsewhere.</summary>
    public float? MenuPanStep(PointF position) =>
        menu is { Scrolls: true } scrolling && MenuIndexAt(scrolling, position) >= 0 ? scrolling.RowHeight : null;

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

    /// <summary>
    /// Closes an open menu for a press at the position that the drawn widgets do not take, such as on a native control or
    /// with the middle or a side button, as a press outside a WPF menu does; a press on an open list's own control, such as
    /// its combo box or a drop-down list's text input, keeps it open, as in a WPF combo box. True when a menu closed.
    /// </summary>
    public bool CloseMenuForPress(PointF position)
    {
        if (menu == null || menu.Bounds.Contains(position) || menu.IsList && menu.Anchor.Contains(position)) return false;

        menu = null;
        return true;
    }

    // The id of the open drop-down list's own text input at the position, or null.
    private string? DropDownInputAt(PointF position)
    {
        if (menu is not { IsList: true, IsComboBox: false } list || !list.Anchor.Contains(position)) return null;

        foreach (var (id, input) in inputs)
            if (input is { Enabled: true, Visible: true } && input.Bounds.Contains(position))
                return id;

        return null;
    }

    // Automation may ask to open or close a menu again before the next frame shows the first request, so opening and
    // closing change nothing when already done.
    private void OpenMenu(string id)
    {
        if (menu?.Id != id) InvokeWidget(id);
    }

    public void CloseMenu(string id)
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

    // Chooses an enabled menu item; a menu that does not stay open closes first. Choosing a combo box's current value
    // changes nothing, as in WPF.
    private void Choose(MenuState active, int index)
    {
        if (index < 0 || index >= active.Choices.Count || !active.Choices[index].Enabled) return;

        if (!active.KeepOpen) menu = null;
        active.Selected = index;

        if (active.IsComboBox && index == active.Current) return;

        active.Current = index;
        active.Changed(index);
    }

    // The enabled choice the arrow keys move the highlight to, or -1 for none. A context menu wraps past its ends, as WPF's
    // does, and Up with nothing highlighted starts at its last choice; a list stops at its ends and starts at its first.
    private static int NextChoice(MenuState active, int direction)
    {
        var wraps = !active.IsList;
        var count = active.Choices.Count;
        if (active.Selected < 0 && !wraps) direction = 1;
        var index = active.Selected >= 0 ? active.Selected : direction < 0 ? count : -1;

        for (var i = 0; i < count; i++)
        {
            index += direction;
            if (wraps) index = (index + count) % count;
            else if (index < 0 || index >= count) return -1;

            if (active.Choices[index].Enabled) return index;
        }

        return -1;
    }

    private AutomationStates WidgetStates(string id, bool enabled)
    {
        var states = enabled ? AutomationStates.None : AutomationStates.Disabled;
        return focused == id && !MenuHasFocus ? states | AutomationStates.Focused : states;
    }

    private void DrawFocus(string id, RectangleF bounds, uint? background = null)
    {
        if (!focusVisible || focused != id || !IsWindowActive || Win32.GetFocus() != owner.Handle) return;

        var color = background.HasValue ? Theme.ContrastingText(background.Value) : Palette.Text;
        Border(RectangleF.Inflate(bounds, -1, -1), color);
    }

    // Records an element for UI Automation and returns its index, or -1 when it is covered or scrolled out of view; a
    // partly coverable element is left out only when wholly covered. A content key, when given, replaces the id as the key.
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
        Action? collapse = null,
        string? contentKey = null,
        AutomationItem? item = null,
        bool partlyCoverable = false,
        nint window = 0)
    {
        // A covered group hides its elements too, rather than leaving them without their row or tile.
        var parent = automationGroups.Count == 0 ? -1 : automationGroups.Peek();
        var uncovered = partlyCoverable ? overlay || occlusion?.Contains(bounds) != true : IsAvailable(bounds);
        if (parent == HiddenGroup || !uncovered) return -1;

        var visible = clip.HasValue ? RectangleF.Intersect(bounds, clip.Value) : bounds;
        if (visible.Width <= 0 || visible.Height <= 0) return -1;

        // Elements without an id, such as text, are keyed by their group and role in drawing order, so text that
        // changes, such as a viewer count, keeps its key and runtime id.
        var scope = parent < 0 ? "" : automation[parent].Key;
        var baseKey = contentKey ?? (automationId.Length != 0 ? automationId : $"{scope}/{role}");
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
            Collapse: collapse,
            Item: item,
            Window: window);

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

    /// <summary>
    /// Opens the focused combo box, or closes its list choosing the highlighted item, as F4, Alt+Down and Alt+Up do in WPF.
    /// A focused text input does the same with its drop-down button's list, keeping the focus, as the text box of a WPF
    /// editable combo box does. False when neither has the focus, the button is unavailable or another widget's menu is open.
    /// </summary>
    public bool ToggleFocusedComboBox()
    {
        var focus = Win32.GetFocus();

        foreach (var (id, input) in inputs)
        {
            if (input.Handle != focus) continue;
            if (!inputDropDowns.TryGetValue(id, out var dropDown)) return false;

            var button = widgets.Find(widget => widget.Id == dropDown.Button);
            return ToggleList(dropDown.Menu, button);
        }

        if (focus != owner.Handle || FocusedWidget() is not { Step: not null } comboBox) return false;

        return ToggleList(comboBox.Id, comboBox);

        // Closes the open list and chooses its highlighted item, or opens the list through the widget that shows it. False when
        // another menu is open or the widget is not drawn, e.g. while disabled or covered.
        bool ToggleList(string menuId, Widget? opener)
        {
            // Closing chooses the highlighted item; clearing the menu first also closes a list kept open after a choice.
            if (menu is { } list && list.Id == menuId)
            {
                menu = null;
                Choose(list, list.Selected);
                return true;
            }

            if (menu != null || opener == null) return false;

            opener.Action();
            return true;
        }
    }

    /// <summary>Moves the focus to the widget. The focus is shown only if it was shown before, e.g. after keyboard use.</summary>
    public void MoveFocus(string id)
    {
        var visible = focusVisible;
        Focus(id);
        focusVisible = visible;
    }

    /// <summary>Moves list focus to a new page's list while leaving native keyboard focus on the window.</summary>
    public bool TryTransferListFocus(string sourceKey, string destinationKey, bool allowImplicitFocus)
    {
        if (focused != sourceKey)
        {
            if (!allowImplicitFocus || HasFocusedWidget) return false;

            var tree = Automation;
            var selected = tree.Find(sourceKey)?.Value;
            if (selected == null || tree.Find(selected)?.Item == null) return false;
        }

        focused = destinationKey;
        return true;
    }

    /// <summary>Restores focus after navigation and returns true when no replacement is still needed.</summary>
    public bool TryReplaceMissingFocus(string preferred, string alternate)
    {
        if (focused == null || FocusedWidget() != null) return true;

        var index = automation.FindIndex(node => node.Key == preferred && !node.Has(AutomationStates.Disabled));
        if (index < 0) index = automation.FindIndex(node => node.Key == alternate);
        if (index < 0) return false;

        var node = automation[index];
        var focusIndex = index;
        if (node is { Role: AutomationRole.List, Value: { } selection })
        {
            var selected = automation.FindIndex(index + 1, item => item.Key == selection && item.Parent == index);
            if (selected >= 0) focusIndex = selected;
        }

        if (IsWindowActive) MoveFocus(node.Key);
        else focused = node.Key;

        var target = automation[focusIndex];
        automation[focusIndex] = target with { States = target.States | AutomationStates.Focused };
        return true;
    }

    public void Focus(string id)
    {
        focused = id;
        focusVisible = true;

        if (inputs.TryGetValue(id, out var input)) FocusInput(id, input);
        else Win32.SetFocus(owner.Handle);

        Invalidate?.Invoke();
    }

    /// <summary>
    /// The arrow of an editable combo box. The combo box is the text input with id <paramref name="input"/>; the arrow fills
    /// <paramref name="bounds"/>, the trailing area inside the input's <paramref name="frame"/>. While the pointer is over the
    /// arrow or the list with id <paramref name="menuId"/> is open, the arrow area takes the combo box fill and the frame takes
    /// the combo box border, so the pair looks like a combo box. F4, Alt+Down and Alt+Up in the input open and close that list.
    /// </summary>
    public void DropDownButton(
        string id,
        string input,
        RectangleF bounds,
        RectangleF frame,
        Action action,
        string menuId,
        bool enabled = true,
        string? name = null,
        string? toolTip = null)
    {
        if (!enabled) CloseMenu(menuId);

        inputDropDowns[input] = (id, menuId);
        var open = menu?.Id == menuId;
        var over = enabled && IsPointerOver(bounds);
        var inner = new RectangleF(x: bounds.X, y: bounds.Y + 1, width: bounds.Width - 1, height: bounds.Height - 2);
        Fill(inner, !enabled ? Palette.InputDisabled : open || over ? Palette.ComboHover : Palette.Input);
        if (open || over) Border(frame, open ? Palette.ComboOpenBorder : Palette.ComboHoverBorder);

        DropDownArrow(RectangleF.Inflate(bounds, -3, 0), enabled ? Palette.Text : Palette.InputDisabledText);
        if (enabled) DrawFocus(id, bounds);
        Add(id, bounds, action, enabled);
        ToolTip(id, bounds, toolTip);
        AddNode(
            id,
            AutomationRole.Button,
            name ?? "",
            bounds,
            WidgetStates(id, enabled),
            toolTip,
            activate: () => InvokeWidget(id));
    }

    /// <summary>
    /// Lists choices below the control. The list is as wide as the larger of the control and the widest choice, unless it
    /// scrolls. <paramref name="name"/> names the list for UI Automation. <paramref name="input"/> is the id of the text input
    /// whose suggestions the choices are; that input controls the list while it is open.
    /// </summary>
    public void DropDown(
        string id,
        string name,
        RectangleF control,
        IReadOnlyList<string> choices,
        Action<int> chosen,
        string? input = null) => ShowMenu(id, control, ToMenuChoices(choices), chosen, MenuKind.DropDown, name, input);

    /// <summary>Shows a context menu below the anchor, or above it when there is no room below; a pointer's anchor is a point.</summary>
    public void Popup(
        string id,
        RectangleF anchor,
        IReadOnlyList<MenuChoice> choices,
        Action<int> chosen) =>
        ShowMenu(id, anchor, choices, chosen, MenuKind.ContextMenu);

    private void ShowMenu(
        string id,
        RectangleF anchor,
        IReadOnlyList<MenuChoice> choices,
        Action<int> chosen,
        MenuKind kind,
        string name = "",
        string? input = null)
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
        )
        {
            Name = name,
            Input = input,
        };

        Invalidate?.Invoke();
    }

    // Like a WPF menu, a menu is as wide as its widest choice, and a list is at least as wide as its control.
    private float MenuWidth(MenuKind kind, IReadOnlyList<MenuChoice> choices, float controlWidth)
    {
        var style = StyleOf(kind);
        var widest = 0f;
        foreach (var choice in choices) widest = Math.Max(widest, MeasureWidth(choice.Text, style.TextSize));

        var naturalWidth = style.TextInset + widest + style.EndPadding;
        return kind == MenuKind.ContextMenu ? naturalWidth : Math.Max(controlWidth, naturalWidth);
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
        input.Handle = owner.CreateTextInput(Changed, GotFocus, () => input.LostFocus?.Invoke());

        inputs.Add(id, input);
        return input;

        void Changed()
        {
            input.Value = Win32.GetText(input.Handle);
            input.Changed?.Invoke(input.Value);
            Invalidate?.Invoke();
        }

        // A click on the shown native control focuses it without going through the app, and so can UI Automation's SetFocus.
        // The focused widget therefore follows the native focus; otherwise the widget focused before would still report the focus.
        void GotFocus()
        {
            focused = id;
            Invalidate?.Invoke();
        }
    }

    // SetFocus announces the native control at once, so the input's element must first be hosted on the control, which
    // needs the element in the published tree. If the last frame left the input out, e.g. because it was under a dialog
    // that just closed, the input takes the native focus once a frame lays it out, unless the focus has moved on by then.
    // Until then the window holds the keyboard focus, so a closed dialog's own input cannot keep it.
    private void FocusInput(string id, TextInput input)
    {
        if (Automation.IndexOf(id) >= 0)
        {
            FocusNativeInput(id, input);
        }
        else
        {
            deferredInputFocus = id;
            Win32.SetFocus(owner.Handle);
        }

        Invalidate?.Invoke();
    }

    private void FocusNativeInput(string id, TextInput input)
    {
        var bounds = InputBounds(input.Bounds);
        owner.PlaceInput(input.Handle, bounds, visible: true, input.Enabled);
        HostInputElement(id, input);
        Win32.SetFocus(input.Handle);
    }

    // Until the next frame hosts the input's element on the focused native control, a client would read the control
    // without the element's name.
    private void HostInputElement(string id, TextInput input)
    {
        var tree = Automation;
        var index = tree.IndexOf(id);
        if (index < 0 || tree.Nodes[index].Window != 0 || !CanShowNativeInput(input)) return;

        var nodes = tree.Nodes.ToArray();
        nodes[index] = nodes[index] with { Window = input.Handle };
        Volatile.Write(ref automationTree, new AutomationTree(nodes));
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
        bool focusable = true,
        string? radioGroup = null,
        Action<int>? step = null)
    {
        if (!enabled || !IsAvailable(bounds)) return;

        var hit = clip.HasValue ? RectangleF.Intersect(bounds, clip.Value) : bounds;
        if (hit is { Width: > 0, Height: > 0 }) widgets.Add(new Widget(id, hit, action, focusable, radioGroup, step));
    }

    private Widget? FocusedWidget() => focused == null ? null : widgets.Find(widget => widget.Id == focused);

    // A modal dialog drawn over the scope has its own tab stops.
    private bool HasFocusScope => !modal && focusScopeStart >= 0 && focusScopeEnd >= focusScopeStart;

    private List<Widget> TabStops()
    {
        var candidates = HasFocusScope ? widgets.GetRange(focusScopeStart, focusScopeEnd - focusScopeStart) : widgets;
        return candidates.FindAll(widget => widget.Focusable);
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
        var disabledText = active.IsList ? Palette.MutedText : Palette.ContextMenuDisabledText;
        Fill(active.Bounds, Palette.Background);
        Border(active.Bounds, active.IsList ? Palette.ListBorder : Palette.ContextMenuBorder);

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

        // A combo box's choices are its own children; a popup menu is an element of its own. A drop-down list of a text
        // input's suggestions is a list. Its highlighted choice is selected while the input keeps the focus, as in a XAML
        // AutoSuggestBox, so browsing it with the arrow keys raises selection events.
        var selectedKey = active.Selected >= 0 ? ChoiceKey(active, active.Selected) : null;
        var container = active.IsComboBox ? automation.FindIndex(node => node.Key == active.Id)
            : active.IsList ? AddNode(active.Id, AutomationRole.List, active.Name, active.Bounds, value: selectedKey)
            : AddNode(active.Id, AutomationRole.Menu, active.Name, active.Bounds);

        // As Microsoft's auto-suggest guidance asks, the text input controls its suggestion list, so clients such as Narrator
        // follow the list's events while the input keeps the focus.
        // https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements
        var inputNode = active.Input is { } inputId ? automation.FindIndex(node => node.Key == inputId) : -1;
        if (inputNode >= 0) automation[inputNode] = automation[inputNode] with { Controls = automation[container].Key };

        automationGroups.Push(container);

        for (var i = 0; i < rows; i++)
        {
            var index = active.Offset + i;
            if (index >= active.Choices.Count) break;

            var choice = active.Choices[index];
            var row = MenuRow(active, i);
            var selected = choice.Enabled && index == active.Selected;
            var over = choice.Enabled && row.Contains(pointer);

            if (selected)
            {
                const float MarkerWidth = 3;
                const float MarkerInset = 2;
                Fill(row, active.IsList ? Palette.MenuSelection : Palette.ContextMenuHover);
                Fill(row with { Y = row.Y + MarkerInset, Width = MarkerWidth, Height = row.Height - 2 * MarkerInset }, Palette.MenuAccent);
            }
            else if (over)
            {
                Fill(row, active.IsList ? Palette.Tile : Palette.ContextMenuHover);
            }

            var checkBounds = row with { X = left + 5, Width = 19 };
            var textBounds = row with { X = left + style.TextInset, Width = textWidth };
            var color = choice.Enabled ? Palette.Text : disabledText;

            // A checked item's tick is centred in its own column, so labels line up whether or not an item is ticked,
            // and uses the accent, as WPF menu items do.
            if (choice.Checked) Icon(text: "✓", checkBounds, choice.Enabled ? Palette.MenuAccent : disabledText);

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
        if (active.IsList && index == active.Selected) states |= AutomationStates.Selected;
        if (index == active.Selected && MenuHasFocus) states |= AutomationStates.Focused;
        var role = active.IsList ? AutomationRole.ListItem : AutomationRole.MenuItem;

        AddNode(
            ChoiceKey(active, index),
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

    private static string ChoiceKey(MenuState active, int index) => $"{active.Id}.choice.{index}";

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

    /// <summary>
    /// A widget; one that is not focusable leaves the focus where it was and is no tab stop. A focus-only widget takes focus
    /// without handling clicks or activation keys. A combo box steps to its next (1) or previous (-1) choice without opening.
    /// </summary>
    private sealed record Widget(
        string Id,
        RectangleF Bounds,
        Action Action,
        bool Focusable,
        string? RadioGroup,
        Action<int>? Step = null,
        bool FocusOnly = false);

    internal sealed record ToolTipRegion(string Id, RectangleF Bounds, string Text);

    /// <summary>A menu item: a disabled item is drawn muted and cannot be chosen.</summary>
    internal readonly record struct MenuChoice(
        string Text,
        bool Enabled = true,
        bool Checked = false,
        string? ToolTip = null,
        bool Checkable = false,
        bool ShowToolTipWhenDisabled = false);

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
        public Action? LostFocus;
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

        /// <summary>A combo box's value, kept current while its list is open.</summary>
        public int Current = selected;

        public Action<int> Changed { get; } = changed;

        /// <summary>The name of a menu or drop-down list for UI Automation; a combo box's list is part of the combo box.</summary>
        public string Name { get; init; } = "";

        /// <summary>The id of the text input whose suggestions a drop-down list shows, or null.</summary>
        public string? Input { get; init; }

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
