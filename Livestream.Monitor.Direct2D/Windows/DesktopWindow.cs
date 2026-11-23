using System.ComponentModel;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows;

/// <summary>
/// A borderless top-level window and its native child controls. All windows live on the UI thread; the message loop
/// ticks them through <see cref="TickAll"/>.
/// </summary>
/// <remarks> You're probably interested in <see cref="Livestream.Monitor.Direct2D.Windows.Monitor.MonitorWindow"/> </remarks>
internal abstract class DesktopWindow : IDisposable
{
    private const string ClassName = "LivestreamMonitorDirect2D";
    private const uint DoubleClicks = 0x0008;
    private const float ControlFontSize = 12;
    private static readonly Dictionary<nint, DesktopWindow> WindowsByHandle = [];
    private static DesktopWindow[] tickWindows = [];
    private static bool tickWindowListNeedsRebuild;
    private static ExceptionDispatchInfo? failure;
    private static bool registered;

    // Each child control's logical bounds as last placed, with the logical size of its font. The bounds are kept unrounded
    // so a change smaller than a logical pixel still reaches the control's device size.
    private readonly List<(nint Handle, RectangleF Bounds, float FontSize)> controls = [];
    private readonly Dictionary<int, Action<int>> commands = [];

    // Child control fonts by logical size, created at the window's current DPI.
    private readonly Dictionary<float, nint> fonts = [];

    // Read-only texts are drawn on the window background rather than as input boxes.
    private readonly HashSet<nint> readOnlyTexts = [];

    // Edit controls whose inner margins are removed; setting a font brings the margins back.
    private readonly HashSet<nint> marginlessControls = [];

    // Child control changes can reach the screen before the frame they're drawn for.
    // Defer showing new controls and destroying retired controls until after the frame.
    private readonly HashSet<nint> controlsToShow = [];
    private readonly HashSet<nint> controlsToHide = [];
    private readonly HashSet<nint> controlsToDestroy = [];
    private int nextControlId = 100;
    private nint inputBrush;
    private nint backgroundBrush;
    private ThemePalette? controlPalette;
    private bool disposed;
    private bool hideRequested = true;
    private bool paintingDuringSizeMove;

    // Set once this constructor has set the window up. Until then its messages reach only the base handler, since a
    // derived class's constructor has not run.
    private readonly bool created;

    protected DesktopWindow(
        string title,
        int width,
        int height,
        nint owner = 0)
    {
        Register();
        var work = Monitors.FindWorkArea(Program.MonitorName) ?? Monitors.GetWorkArea(new Win32.Point());
        const int WindowInset = 120;

        // Leave the composed frame intact behind moving native controls so a delayed repaint cannot expose the desktop.
        var style = Win32.WsPopup | Win32.WsThickFrame | Win32.WsSystemMenu | Win32.WsMinimizeBox;
        Handle = Win32.CreateWindowEx(
            extendedStyle: 0,
            className: ClassName,
            windowName: title,
            style: style,
            x: work.Left + WindowInset,
            y: work.Top + WindowInset,
            width: width,
            height: height,
            parent: owner,
            menu: 0,
            instance: Win32.GetModuleHandle(0),
            parameter: 0);

        if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        WindowsByHandle.Add(Handle, this);
        tickWindowListNeedsRebuild = true;
        Scale = Win32.GetDpiForWindow(Handle) / (float)Win32.UserDefaultScreenDpi;
        var flags = Win32.SwpNoMove | Win32.SwpNoZOrder | Win32.SwpNoActivate | Win32.SwpFrameChanged;
        Win32.SetWindowPos(
            Handle,
            insertAfter: 0,
            x: 0,
            y: 0,
            (int)(width * Scale),
            (int)(height * Scale),
            flags);

        created = true;
    }

    public nint Handle { get; }
    public bool IsClosed { get; private set; }
    protected float Scale { get; private set; }
    protected bool NeedsPaint { get; set; } = true;
    protected bool IsInSizeMove { get; private set; }
    protected virtual bool IsAnimating => false;
    protected bool IsVisible => Win32.IsWindowVisible(Handle) && !Win32.IsIconic(Handle);

    public static void ThrowIfFailed() => failure?.Throw();

    public static void TickAll()
    {
        if (tickWindowListNeedsRebuild)
        {
            tickWindows = WindowsByHandle.Values.ToArray();
            tickWindowListNeedsRebuild = false;
        }

        foreach (var window in tickWindows)
            if (!window.IsClosed)
                window.Tick();

        // Pace the whole presented batch once, rather than waiting separately for each window. A failed flush only
        // skips that wait.
        if (HasVisibleAnimation()) Win32.DwmFlush();
    }

    public static uint GetFrameWaitMilliseconds()
    {
        // Animation pacing already happened at DwmFlush; this timeout only polls an idle event loop.
        const uint IdleWaitMilliseconds = 100;
        return HasVisibleAnimation() ? 0 : IdleWaitMilliseconds;
    }

    /// <summary>
    /// Offers a key press to the window owning its target, and forwards wheel input aimed at a read-only text to that
    /// text's window; returns true when the message was handled. A native control's system key is offered to its window
    /// without being handled.
    /// </summary>
    public static bool PreprocessMessage(in Win32.Message message)
    {
        if (message.Id == Win32.WmMouseWheel) return ForwardTextWheel(message);

        if (message.Id == Win32.WmSysKeyDown)
        {
            OfferControlSystemKey(message);
            return false;
        }

        if (message.Id != Win32.WmKeyDown) return false;

        var target = message.Window;

        foreach (var window in WindowsByHandle.Values)
        {
            var ownsTarget = window.Handle == target || window.controls.Exists(control => control.Handle == target);
            if (ownsTarget) return window.PreprocessKey((int)message.WParam);
        }

        return false;
    }

    // A system key pressed in a native control, e.g. Alt+Down in a text input, does not reach the window that owns the
    // control: that window gets only its own system keys through its window procedure. The control still receives the key,
    // so releasing Alt afterwards does not enter the system menu.
    private static void OfferControlSystemKey(in Win32.Message message)
    {
        var target = message.Window;

        foreach (var window in WindowsByHandle.Values)
        {
            if (!window.controls.Exists(control => control.Handle == target)) continue;

            window.OnControlSystemKey((int)message.WParam, message.LParam);
            return;
        }
    }

    // A read-only text has no scrollbar and ignores the wheel, so its window receives the wheel and scrolls it instead.
    private static bool ForwardTextWheel(in Win32.Message message)
    {
        foreach (var window in WindowsByHandle.Values)
        {
            if (!window.readOnlyTexts.Contains(message.Window)) continue;

            Win32.SendMessage(window.Handle, Win32.WmMouseWheel, message.WParam, message.LParam);
            return true;
        }

        return false;
    }

    protected virtual void Tick() { }

    protected void Show()
    {
        hideRequested = false;
        var flags = Win32.SwpShowWindow | Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoZOrder | Win32.SwpNoActivate;
        Win32.SetWindowPos(Handle, insertAfter: 0, x: 0, y: 0, width: 0, height: 0, flags);
    }

    public virtual void Hide()
    {
        hideRequested = true;
        Win32.ShowWindow(Handle, Win32.SwHide);
    }

    public virtual void Close()
    {
        if (IsClosed) return;

        IsClosed = true;
        Win32.DestroyWindow(Handle);
        WindowsByHandle.Remove(Handle);
        tickWindowListNeedsRebuild = true;
        OnClosed();
    }

    public virtual void Dispose()
    {
        if (disposed) return;

        disposed = true;
        Close();
        foreach (var font in fonts.Values) Win32.DeleteObject(font);
        if (inputBrush != 0) Win32.DeleteObject(inputBrush);
        if (backgroundBrush != 0) Win32.DeleteObject(backgroundBrush);
    }

    internal void ApplyControlTheme(ThemePalette palette)
    {
        if (controlPalette == palette) return;

        controlPalette = palette;
        var previousInput = inputBrush;
        var previousBackground = backgroundBrush;
        inputBrush = Win32.CreateSolidBrush(Win32.ColorRef(palette.Input));
        backgroundBrush = Win32.CreateSolidBrush(Win32.ColorRef(palette.Background));
        foreach (var (control, _, _) in controls) Win32.InvalidateRect(control, 0, true);
        if (previousInput != 0) Win32.DeleteObject(previousInput);
        if (previousBackground != 0) Win32.DeleteObject(previousBackground);

        NeedsPaint = true;
    }

    /// <summary>A single-line native edit control, positioned later through <see cref="PlaceInput"/>.</summary>
    internal nint CreateTextInput(Action changed, Action gotFocus, Action lostFocus)
    {
        var input = Control(
            "EDIT",
            "",
            Rectangle.Empty,
            Win32.WsTabStop | Win32.EsAutoHorizontalScroll,
            code =>
            {
                if (code == Win32.EnChange) changed();
                if (code == Win32.EnSetFocus) gotFocus();
                if (code == Win32.EnKillFocus) lostFocus();
            }
        );

        // Without inner margins, typed text starts exactly where the drawn placeholder does.
        RemoveMargins(input);
        return input;
    }

    /// <summary>
    /// A multiline read-only edit control on the window background, whose text can be selected and copied. It is positioned
    /// later through <see cref="PlaceInput"/> and removed through <see cref="DestroyControl"/>.
    /// </summary>
    internal nint CreateReadOnlyText(string text, float fontSize)
    {
        var style = Win32.EsMultiLine | Win32.EsReadOnly | Win32.EsAutoVerticalScroll;

        // Edit controls break lines only at CR LF.
        var shown = text.ReplaceLineEndings("\r\n");
        var control = Control(className: "EDIT", text: shown, bounds: Rectangle.Empty, style: style, fontSize: fontSize);

        // Without inner margins, the text wraps at the bounds it is placed in.
        RemoveMargins(control);
        readOnlyTexts.Add(control);
        return control;
    }

    /// <summary>Hides the control before submitting the next frame, unless it is placed visible again first.</summary>
    internal void HideControl(nint control)
    {
        controlsToShow.Remove(control);
        controlsToHide.Add(control);
    }

    /// <summary>Destroys the control once the next frame is on screen.</summary>
    internal void DestroyControl(nint control)
    {
        // Stop keyboard input while the control waits to be destroyed.
        if (Win32.GetFocus() == control) Win32.SetFocus(Handle);

        controlsToShow.Remove(control);
        controlsToHide.Remove(control);
        controlsToDestroy.Add(control);
    }

    private void RemoveMargins(nint control)
    {
        Win32.SendMessage(control, Win32.EmSetMargins, Win32.EcLeftMargin | Win32.EcRightMargin, lParam: 0);
        marginlessControls.Add(control);
    }

    internal void PlaceInput(
        nint input,
        RectangleF bounds,
        bool visible,
        bool enabled)
    {
        var index = controls.FindIndex(entry => entry.Handle == input);

        if (index >= 0 && controls[index].Bounds != bounds)
        {
            controls[index] = controls[index] with { Bounds = bounds };
            var pixels = ControlPixels(bounds);
            Win32.SetWindowPos(
                window: input,
                insertAfter: 0,
                x: pixels.X,
                y: pixels.Y,
                width: pixels.Width,
                height: pixels.Height,
                flags: Win32.SwpNoZOrder | Win32.SwpNoActivate);
        }

        Win32.EnableWindow(input, enabled);
        controlsToHide.Remove(input);

        // Wait for the new frame before showing a control over it.
        // Hiding is safe now because the previous frame has the input text underneath.
        var shown = (Win32.GetWindowLongPtr(input, Win32.GwlStyle) & Win32.WsVisible) != 0;
        if (visible && !shown) controlsToShow.Add(input);
        if (visible) return;

        controlsToShow.Remove(input);
        if (shown) Win32.ShowWindow(input, Win32.SwHide);
    }

    /// <summary>Hides retired native text before submitting the replacement frame.</summary>
    protected void HideRetiredControls()
    {
        foreach (var control in controlsToHide) Win32.ShowWindow(control, Win32.SwHide);
        foreach (var control in controlsToDestroy) Win32.ShowWindow(control, Win32.SwHide);
        controlsToHide.Clear();
    }

    /// <summary>Waits for the frame, then shows newly placed controls and destroys retired controls.</summary>
    internal void UpdatePlacedControls()
    {
        if (controlsToShow.Count == 0 && controlsToDestroy.Count == 0) return;

        // A hidden window's controls cannot paint, so showing and destroying controls waits until the window is shown and its
        // next frame is drawn.
        if (!IsVisible) return;

        // Still update the controls if DwmFlush fails.
        Win32.DwmFlush();

        // A newly shown layered control appears black until its first paint reaches the screen. So it is shown fully
        // transparent, painted straight away, and made opaque only once the next composition has its painted content.
        if (controlsToShow.Count != 0)
        {
            foreach (var control in controlsToShow)
            {
                if (IsLayered(control)) SetControlAlpha(control, alpha: 0);
                Win32.ShowWindow(control, Win32.SwShowNoActivate);
            }

            // Paint child controls too, such as the Rich Edit inside the output's text host.
            const uint UpdateNow = 0x0100;
            const uint AllChildren = 0x0080;
            foreach (var control in controlsToShow) Win32.RedrawWindow(control, updateRect: 0, updateRegion: 0, flags: UpdateNow | AllChildren);

            Win32.DwmFlush();

            foreach (var control in controlsToShow)
                if (IsLayered(control))
                    SetControlAlpha(control, alpha: byte.MaxValue);
        }

        foreach (var control in controlsToDestroy)
        {
            controls.RemoveAll(entry => entry.Handle == control);
            readOnlyTexts.Remove(control);
            marginlessControls.Remove(control);
            Win32.DestroyWindow(control);
        }

        controlsToShow.Clear();
        controlsToDestroy.Clear();
    }

    private static bool IsLayered(nint control) => (Win32.GetWindowLongPtr(control, Win32.GwlExStyle) & Win32.WsExLayered) != 0;

    private static void SetControlAlpha(nint control, byte alpha)
    {
        if (!Win32.SetLayeredWindowAttributes(window: control, colorKey: 0, alpha: alpha, flags: Win32.LwaAlpha))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>Bounds used by <see cref="PlaceInput"/>, rounded to device pixels and returned in logical units.</summary>
    internal RectangleF ControlArea(RectangleF bounds)
    {
        var pixels = ControlPixels(bounds);
        return new RectangleF(x: pixels.X / Scale, y: pixels.Y / Scale, width: pixels.Width / Scale, height: pixels.Height / Scale);
    }

    private Rectangle ControlPixels(RectangleF bounds) => new(
        x: (int)(bounds.X * Scale),
        y: (int)(bounds.Y * Scale),
        width: Math.Max(1, (int)(bounds.Width * Scale)),
        height: Math.Max(1, (int)(bounds.Height * Scale)));

    protected void CenterOnOwner(nint owner)
    {
        if (!Win32.GetWindowRect(owner, out var parent) || !Win32.GetWindowRect(Handle, out var window)) return;

        var workArea = Monitors.GetWindowWorkArea(owner);
        if (Win32.IsIconic(owner)) parent = workArea;

        var x = parent.Left + (parent.Width - window.Width) / 2;
        var y = parent.Top + (parent.Height - window.Height) / 2;
        var position = Monitors.ClampToWorkArea(x, y, window.Width, window.Height, workArea);
        var flags = Win32.SwpNoSize | Win32.SwpNoZOrder | Win32.SwpNoActivate;
        Win32.SetWindowPos(Handle, insertAfter: 0, position.X, position.Y, width: 0, height: 0, flags);
    }

    protected void SetTopmost()
    {
        var flags = Win32.SwpNoSize | Win32.SwpNoMove | Win32.SwpNoActivate;
        nint topmost = -1;
        Win32.SetWindowPos(Handle, topmost, x: 0, y: 0, width: 0, height: 0, flags);
    }

    protected nint Control(
        string className,
        string text,
        Rectangle bounds,
        uint style,
        Action<int>? command = null,
        float fontSize = ControlFontSize,
        nint parent = 0)
    {
        // Rich Edit keeps its original wrap width if created hidden, so a control nested in a host, such as the output's Rich
        // Edit, is created visible while the window is hidden; its hidden host keeps it off the screen. Other controls are
        // created hidden and shown once the frame they belong to is on screen.
        var layered = parent == 0;
        var visibility = IsVisible || layered ? 0 : Win32.WsVisible;

        var id = nextControlId++;
        var control = Win32.CreateWindowEx(
            extendedStyle: layered ? Win32.WsExLayered : 0,
            className: className,
            windowName: text,
            style: Win32.WsChild | visibility | style,
            x: (int)(bounds.X * Scale),
            y: (int)(bounds.Y * Scale),
            width: (int)(bounds.Width * Scale),
            height: (int)(bounds.Height * Scale),
            parent: layered ? Handle : parent,
            menu: id,
            instance: Win32.GetModuleHandle(0),
            parameter: 0);

        if (control == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        // Give native controls their own surface above the composed frame without clipping a hole in it.
        if (layered && !Win32.SetLayeredWindowAttributes(window: control, colorKey: 0, alpha: byte.MaxValue, flags: Win32.LwaAlpha))
        {
            var error = Marshal.GetLastWin32Error();
            Win32.DestroyWindow(control);
            throw new Win32Exception(error);
        }

        controls.Add((control, bounds, fontSize));
        if (command != null) commands.Add(id, command);

        Win32.SendMessage(control, Win32.WmSetFont, wParam: (nuint)GetFont(fontSize), lParam: 1);
        return control;
    }

    /// <summary>Width of the widest line in the child controls' font, in logical units, as those controls lay it out.</summary>
    protected unsafe float MeasureControlText(string text)
    {
        var context = Win32.GetDC(Handle);
        var previous = Win32.SelectObject(context, GetFont(ControlFontSize));
        var widest = 0;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            fixed (char* characters = line)
            {
                Win32.GetTextExtentPoint32(context, characters, line.Length, out var extent);
                widest = Math.Max(widest, extent.X);
            }
        }

        Win32.SelectObject(context, previous);
        Win32.ReleaseDC(Handle, context);
        return widest / Scale;
    }

    /// <summary>Height of a line in a control font, in logical units: the font's full height, as edit and Rich Edit controls space lines.</summary>
    internal unsafe float MeasureControlLineHeight(float fontSize = ControlFontSize)
    {
        var context = Win32.GetDC(Handle);
        var previous = Win32.SelectObject(context, GetFont(fontSize));
        var character = 'X';
        Win32.GetTextExtentPoint32(
            context,
            &character,
            1,
            out var extent);

        Win32.SelectObject(context, previous);
        Win32.ReleaseDC(Handle, context);
        return extent.Y / Scale;
    }

    /// <summary>Text baseline offset in logical units for a single-line control. GDI rounds the ascent to device pixels, so it can differ from Direct2D.</summary>
    internal float MeasureControlAscent()
    {
        var context = Win32.GetDC(Handle);
        var previous = Win32.SelectObject(context, GetFont(ControlFontSize));

        Win32.GetTextMetrics(context, out var metrics);

        Win32.SelectObject(context, previous);
        Win32.ReleaseDC(Handle, context);

        return metrics.Ascent / Scale;
    }

    protected virtual void OnClosed() { }

    protected virtual bool PreprocessKey(int key) => false;

    /// <summary>A WM_SYSKEYDOWN at one of the window's native controls, with its lParam.</summary>
    protected virtual void OnControlSystemKey(int key, nint lParam) { }

    protected virtual nint? OnMessage(uint message, nuint wParam, nint lParam) => HandleMessage(message, wParam, lParam);

    private nint? HandleMessage(uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case Win32.WmNonClientCalculate:
                // The client area covers the whole window; subclasses draw their own chrome.
                return 0;
            case Win32.WmControlColorEdit or Win32.WmControlColorStatic when controlPalette is { } palette:
                var onBackground = readOnlyTexts.Contains(lParam);
                Win32.SetTextColor((nint)wParam, Win32.ColorRef(palette.Text));
                Win32.SetBkColor((nint)wParam, Win32.ColorRef(onBackground ? palette.Background : palette.Input));
                return onBackground ? backgroundBrush : inputBrush;
            case Win32.WmClose:
                Close();
                return 0;

            // Windows hides an owner's visible popups when it is minimized and shows them again when it is restored,
            // including those the app hid in between, such as a preview the pointer has left.
            case Win32.WmShowWindow when wParam != 0 && lParam == Win32.ShowParentOpening && hideRequested:
                return 0;
            case Win32.WmEnterSizeMove:
                IsInSizeMove = true;
                return 0;
            case Win32.WmExitSizeMove:
                IsInSizeMove = false;
                return 0;
            case Win32.WmPaint:
                NeedsPaint = true;
                if (!IsInSizeMove || paintingDuringSizeMove || !IsVisible) return null;

                // Win32's move/size loop suspends TickAll. Validate the update region and draw here until it returns.
                var result = Win32.DefWindowProc(Handle, message, wParam, lParam);
                paintingDuringSizeMove = true;
                try
                {
                    Tick();
                }
                finally
                {
                    paintingDuringSizeMove = false;
                }

                return result;
            case Win32.WmEraseBackground:
                // The Direct2D frame paints the background.
                return 1;
            case Win32.WmCommand:
                if (commands.TryGetValue(Win32.LowWord((nint)wParam), out var command))
                    command(Win32.HighWord((nint)wParam));

                NeedsPaint = true;
                return 0;
            case Win32.WmSize:
                NeedsPaint = true;
                if (IsInSizeMove) Win32.InvalidateRect(Handle, rect: 0, erase: false);
                return 0;
            case Win32.WmDpiChanged:
                Scale = Win32.LowWord((nint)wParam) / (float)Win32.UserDefaultScreenDpi;
                UpdateFonts();
                var rect = Marshal.PtrToStructure<Win32.Rect>(lParam);
                Win32.SetWindowPos(
                    Handle,
                    0,
                    rect.Left,
                    rect.Top,
                    rect.Width,
                    rect.Height,
                    Win32.SwpNoZOrder
                );

                foreach (var (control, bounds, _) in controls)
                {
                    Win32.SetWindowPos(
                        window: control,
                        insertAfter: 0,
                        x: (int)(bounds.X * Scale),
                        y: (int)(bounds.Y * Scale),
                        width: (int)(bounds.Width * Scale),
                        height: (int)(bounds.Height * Scale),
                        flags: Win32.SwpNoZOrder | Win32.SwpNoActivate);
                }

                NeedsPaint = true;
                return 0;
            default:
                return null;
        }
    }

    private static bool HasVisibleAnimation()
    {
        foreach (var window in tickWindows)
            if (window is { IsClosed: false, IsAnimating: true, IsVisible: true })
                return true;

        return false;
    }

    // Recreates the fonts at the window's current DPI.
    private void UpdateFonts()
    {
        var previous = fonts.Values.ToArray();
        fonts.Clear();

        foreach (var (control, _, fontSize) in controls)
        {
            Win32.SendMessage(control, Win32.WmSetFont, wParam: (nuint)GetFont(fontSize), lParam: 1);
            if (marginlessControls.Contains(control)) RemoveMargins(control);
        }

        foreach (var font in previous) Win32.DeleteObject(font);
    }

    private nint GetFont(float size)
    {
        if (fonts.TryGetValue(size, out var existing)) return existing;

        const int NormalWeight = 400;
        const uint DefaultCharacterSet = 1;
        const uint ClearTypeQuality = 5;
        var font = Win32.CreateFont(
            height: -(int)(size * Scale),
            width: 0,
            escapement: 0,
            orientation: 0,
            weight: NormalWeight,
            italic: 0,
            underline: 0,
            strikeOut: 0,
            characterSet: DefaultCharacterSet,
            outputPrecision: 0,
            clipPrecision: 0,
            quality: ClearTypeQuality,
            pitchAndFamily: 0,
            face: "Segoe UI");

        fonts.Add(size, font);
        return font;
    }

    private static unsafe void Register()
    {
        if (registered) return;

        fixed (char* className = ClassName)
        {
            var windowClass = new Win32.WindowClass
            {
                Size = (uint)sizeof(Win32.WindowClass),
                Style = DoubleClicks,
                WindowProcedure = &WindowProcedure,
                Instance = Win32.GetModuleHandle(0),
                Cursor = Win32.LoadCursor(0, Win32.IdcArrow),
                ClassName = className,
            };

            if (Win32.RegisterClassEx(windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        registered = true;
    }

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam)
    {
        try
        {
            if (WindowsByHandle.TryGetValue(window, out var target))
            {
                var result = target.created
                    ? target.OnMessage(message, wParam, lParam)
                    : target.HandleMessage(message, wParam, lParam);

                if (result.HasValue) return result.Value;
            }

            return Win32.DefWindowProc(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            // Exceptions cannot unwind through the native window procedure; rethrow in the managed message loop.
            failure = ExceptionDispatchInfo.Capture(exception);
            return 0;
        }
    }
}
