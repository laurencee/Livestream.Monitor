using System.Drawing;
using System.Runtime.InteropServices;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Automation;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows;

/// <summary>
/// A window drawn with Direct2D: custom chrome, drawn widgets and modal dialog panes. Every frame is described as a
/// list of draw commands; a frame identical to the presented one is not drawn or presented again.
/// </summary>
internal abstract class CanvasWindow : DesktopWindow
{
    protected const int TitleHeight = 30;

    // The window border is drawn inside the client area, at the default border thickness.
    protected const float WindowBorderThickness = 1;

    // The caption title's font size, and the caption width beside it: its left inset, and the close button with the
    // space before it.
    protected const float CaptionTitleFontSize = 16;
    protected const float CaptionTitleMargins = 85;

    private const int ResizeBorder = 5;

    // MahApps caption buttons are 46 pixels wide and adjoin each other and the window edge.
    private const int CaptionButtonWidth = 46;
    private const int HitClient = 1;
    private const int HitCaption = 2;
    private const int HitLeft = 10;
    private const int HitRight = 11;
    private const int HitTop = 12;
    private const int HitTopLeft = 13;
    private const int HitTopRight = 14;
    private const int HitBottom = 15;
    private const int HitBottomLeft = 16;
    private const int HitBottomRight = 17;

    // WPF ToolTipService defaults: the hover time before a tooltip opens, and the window after one closes during
    // which the next opens at once.
    private const long ToolTipInitialShowDelay = 1000;
    private const long ToolTipBetweenShowDelay = 100;
    private static readonly ThemePalette DefaultPalette = Theme.Create(new Settings());

    // One device set and text cache serve every canvas window; each window owns only its swap chain. They are
    // created with the first window and released with the last.
    private static GraphicsDevice? sharedGraphics;
    private static Painter? sharedPainter;
    private static int canvasCount;

    private readonly GraphicsDevice graphics;
    private readonly Painter painter;
    private readonly OverlaySurface surface;
    private readonly List<DrawCommand> drawCommands = [];
    private readonly List<DrawCommand> presentedCommands = [];
    private readonly List<DialogPane> dialogs = [];
    private ThemePalette? previousPalette;
    private float presentedScale;
    private bool rendered;
    private bool disposed;
    private bool widgetPressed;
    private bool pressedInDialog;
    private bool pointerInWaitArea;
    private string? focusBeforeDialogs;
    private int wheelDelta;
    private ToolTipWindow? toolTip;
    private string? toolTipOwner;
    private string? toolTipText;
    private Win32.Point toolTipPointer;
    private long toolTipDue;
    private long toolTipClosedAt = long.MinValue / 2;
    private bool toolTipSuppressed;
    private WindowAutomation? automation;

    protected CanvasWindow(
        string title,
        int width,
        int height,
        nint owner = 0)
        : base(title: title, width: width, height: height, owner: owner)
    {
        // Created first: the window's messages reach this class's handlers once the base class has set it up.
        Ui = new SurfaceUi(this, drawCommands)
        {
            Invalidate = Invalidate,
            MeasureWidth = (text, fontSize) => MeasureWidth(text, fontSize),
            MeasureWrapped = MeasureWrapped,
        };

        sharedGraphics ??= new GraphicsDevice();
        sharedPainter ??= new Painter(sharedGraphics);
        canvasCount++;
        graphics = sharedGraphics;
        painter = sharedPainter;
        surface = new OverlaySurface(graphics, Handle);
    }

    protected Func<ThemePalette> ThemeSource { get; init; } = () => DefaultPalette;

    protected override bool IsAnimating => ActiveDialog is { IsProgress: true } or { IsScrolling: true } || Ui.IsMenuScrolling;

    /// <summary>False until the window is activated and while another window is active; the chrome is then drawn gray as the WPF window's is.</summary>
    private bool IsWindowActive { get; set; }

    /// <summary>True to paint a hidden window, for one that shows itself once its first frame is presented.</summary>
    protected virtual bool PaintsWhileHidden => false;

    /// <summary>Called once a frame has been presented, or found already presented.</summary>
    protected virtual void OnPresented() { }

    protected SurfaceUi Ui { get; }
    protected GraphicsDevice Graphics => graphics;
    protected SizeF ClientSize { get; private set; }
    protected PointF Pointer { get; private set; }
    protected bool HasDialog => dialogs.Count != 0;
    protected virtual uint? WindowBorderColor => null;
    protected virtual bool CanResize => true;
    private DialogPane? ActiveDialog => dialogs.Count == 0 ? null : dialogs[^1];

    /// <summary>Requests a frame on the next tick.</summary>
    protected void Invalidate() => NeedsPaint = true;

    internal float MeasureWrapped(string text, float width, float fontSize, FontWeight weight = FontWeight.Normal)
    {
        var style = new TextStyle(fontSize, weight);
        return painter.MeasureWrapped(text, width, style);
    }

    internal float MeasureWidth(string text, float fontSize, FontWeight weight = FontWeight.Normal)
    {
        var style = new TextStyle(fontSize, weight);
        return painter.MeasureWidth(text, style);
    }

    internal DialogPane ShowDialog(
        string title,
        string message,
        DialogKind kind,
        string affirmative,
        string negative = "Cancel")
    {
        // A dialog's native message control shows through drawn content, so only the active dialog shows its message.
        ActiveDialog?.Suspend();

        var dialog = new DialogPane(
            owner: this,
            title: title,
            message: message,
            kind: kind,
            affirmative: affirmative,
            negative: negative);

        // As in WPF, the focus held before the first dialog returns when the last one closes.
        if (dialogs.Count == 0) focusBeforeDialogs = Ui.FocusedId;
        dialogs.Add(dialog);

        // Ends any held press, such as on a scroll bar, whose release the dialog would otherwise receive.
        if (Win32.GetCapture() == Handle) Win32.ReleaseCapture();
        Ui.ModalOverlay();
        Win32.SetFocus(Handle);
        Win32.SetForegroundWindow(Handle);
        NeedsPaint = true;
        return dialog;
    }

    internal void RemoveDialog(DialogPane dialog)
    {
        // A held press on the closing dialog's scroll bar captured the mouse; its release no longer reaches that dialog.
        if (dialog.IsScrolling && Win32.GetCapture() == Handle) Win32.ReleaseCapture();

        dialogs.Remove(dialog);
        ActiveDialog?.Resume();
        Ui.ModalOverlay();

        // With nothing to restore, focus is cleared, so a closed dialog's focused input cannot be restored later.
        if (HasDialog || disposed) Win32.SetFocus(Handle);
        else if (focusBeforeDialogs is { } focus) Ui.Focus(focus);
        else Ui.ClearFocus();

        if (!HasDialog) focusBeforeDialogs = null;
        NeedsPaint = true;
    }

    protected override void Tick()
    {
        if (IsClosed) return;

        try
        {
            TickFrame();
            UpdateToolTip();
        }
        catch (COMException exception) when (GraphicsDevice.IsDeviceLost(exception.HResult))
        {
            // Device removal can surface from any device call, not only from EndDraw and Present.
            surface.AbortDraw();
            RecoverDevice();
        }
    }

    private void TickFrame()
    {
        var visible = IsVisible;
        if (visible) UpdatePointer(ReadPointer());
        BeforeFrame();
        if (Ui.Tick(Environment.TickCount64)) NeedsPaint = true;
        if (ActiveDialog?.Tick(Environment.TickCount64) == true) NeedsPaint = true;
        var palette = ThemeSource();
        if (ActiveDialog?.IsProgress == true || palette != previousPalette || !surface.IsCurrent) NeedsPaint = true;
        if (!visible) surface.ReleaseIfStale();
        if (!NeedsPaint || rendered && !visible && !PaintsWhileHidden) return;

        previousPalette = palette;
        Win32.GetClientRect(Handle, out var bounds);
        ClientSize = new SizeF(bounds.Width / Scale, bounds.Height / Scale);
        var surfaceChanged = surface.Ensure(bounds.Width, bounds.Height);
        drawCommands.Clear();
        var viewport = new RectangleF(PointF.Empty, ClientSize);
        Ui.IsWindowActive = IsWindowActive;
        Ui.ListMaximumHeight = Monitors.PrimaryScreenHeight / 3;
        Ui.Begin(palette, viewport, HasDialog ? viewport : CoveredArea(ClientSize));
        Ui.Fill(viewport, palette.Background);
        Draw(ClientSize);

        if (ActiveDialog is { } dialog)
        {
            Ui.ModalOverlay();
            dialog.Draw(Ui, ClientSize);
        }

        UpdateWaitCursor();
        Ui.End();
        var focusedDialog = ActiveDialog?.Focus(Ui) == true;
        Ui.Border(viewport, WindowBorderColor ?? Ui.TitleBarColor);
        NeedsPaint = focusedDialog;

        if (surfaceChanged || surface.PendingSnapshotPath != null || !IsPresented())
        {
            painter.BeginFrame(Environment.TickCount64);
            painter.Render(surface.Begin(), drawCommands, Scale);
            surface.EndAndPresent();
            rendered = true;
            presentedScale = Scale;
            presentedCommands.Clear();
            presentedCommands.AddRange(drawCommands);
        }

        UpdatePlacedControls();
        OnPresented();
    }

    public void Snapshot(string path)
    {
        surface.PendingSnapshotPath = path;
        NeedsPaint = true;
        Tick();
    }

    public override void Close()
    {
        // The owned popup is destroyed with this window.
        toolTip?.Dispose();
        toolTip = null;
        base.Close();
    }

    public override void Dispose()
    {
        if (disposed) return;

        disposed = true;
        foreach (var dialog in dialogs.ToArray()) dialog.Dispose();
        surface.Dispose();
        base.Dispose();
        if (--canvasCount != 0) return;

        sharedPainter!.Dispose();
        sharedGraphics!.Dispose();
        sharedPainter = null;
        sharedGraphics = null;
    }

    protected abstract void Draw(SizeF size);
    protected virtual void BeforeFrame() { }
    protected virtual RectangleF? CoveredArea(SizeF size) => null;
    protected virtual void OnPointerDown(PointF point) { }
    protected virtual void OnPointerUp(PointF point) { }
    protected virtual void OnPointerMove(PointF point) { }
    protected virtual void OnDoubleClick(PointF point) { }
    protected virtual void OnScroll(int steps) { }
    protected virtual bool OnKey(int key) => false;
    protected virtual bool OnDialogKey(int key) => false;
    protected virtual void RequestClose() => Close();
    protected virtual void Minimize() => Hide();
    protected virtual bool IsCaption(PointF point) => point.Y < TitleHeight;

    protected void DrawCaption(string title, float width, bool minimize = false)
    {
        const float TitleLeft = 8;
        Ui.Fill(new RectangleF(x: 0, y: 0, width: width, height: TitleHeight), Ui.TitleBarColor);
        var titleBounds = new RectangleF(x: TitleLeft, y: 0, width: width - CaptionTitleMargins, height: TitleHeight);
        Ui.Text(title, titleBounds, Ui.Palette.TitleText, size: CaptionTitleFontSize);
        var closeLeft = width - CaptionButtonWidth;
        var close = new RectangleF(x: closeLeft, y: 0, width: CaptionButtonWidth, height: TitleHeight);
        Ui.CaptionButton(id: "chrome.close", bounds: close, action: RequestClose, close: true);

        if (minimize)
        {
            var minimizeLeft = close.Left - CaptionButtonWidth;
            var bounds = new RectangleF(x: minimizeLeft, y: 0, width: CaptionButtonWidth, height: TitleHeight);
            Ui.CaptionButton(id: "chrome.minimize", bounds: bounds, action: Minimize, close: false);
        }
    }

    protected override bool PreprocessKey(int key)
    {
        DismissToolTip();
        var shift = Win32.IsKeyDown(VirtualKey.Shift);

        if (ActiveDialog is { } dialog)
        {
            var handled = Ui.Key(key, shift) || dialog.Key(key);
            if (handled) NeedsPaint = true;
            return handled;
        }

        if (Ui.Key(key, shift) || OnDialogKey(key) || !Ui.HasFocusedInput && OnKey(key))
        {
            NeedsPaint = true;
            return true;
        }

        return false;
    }

    protected override nint? OnMessage(uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            // Owned windows are destroyed with their owner, without their own Close.
            case Win32.WmDestroy:
                automation?.Release();
                break;

            // The system menu, Alt+F4 and the taskbar close as the caption's close button does.
            case Win32.WmClose:
                RequestClose();
                return 0;
            case Win32.WmNonClientHitTest:
                return HitTest(lParam);
            case Win32.WmActivate:
                IsWindowActive = Win32.LowWord((nint)wParam) != 0;
                NeedsPaint = true;
                break;
            case Win32.WmCaptureChanged:
                Ui.EndMenuScroll();
                ActiveDialog?.MouseUp();
                break;
            case Win32.WmMouseMove:
                UpdatePointer(ToLogical(lParam));
                return 0;
            case Win32.WmLeftButtonDown:
                DismissToolTip();
                Pointer = ToLogical(lParam);
                pressedInDialog = HasDialog;
                widgetPressed = Ui.MouseDown(Pointer);

                // A menu scroll bar press follows the pointer beyond the window, as a drag of the window's own bars does.
                if (Ui.IsMenuScrolling) Win32.SetCapture(Handle);
                if (!widgetPressed && ActiveDialog?.MouseDown(Pointer) == true) Win32.SetCapture(Handle);
                if (!widgetPressed && !HasDialog) OnPointerDown(Pointer);
                NeedsPaint = true;
                return 0;
            case Win32.WmLeftButtonUp:
                Pointer = ToLogical(lParam);

                if (ActiveDialog is { IsScrolling: true } scrolling)
                {
                    scrolling.MouseUp();
                    Win32.ReleaseCapture();
                }

                if (!Ui.MouseUp(Pointer) && !widgetPressed && !HasDialog && !pressedInDialog) OnPointerUp(Pointer);
                if (widgetPressed && Win32.GetCapture() == Handle) Win32.ReleaseCapture();
                widgetPressed = false;
                NeedsPaint = true;
                return 0;

            // A quick second press on an open menu or a dialog's scroll bar, such as on its arrow, is a press like the first.
            // Elsewhere it is dropped while a dialog is open or after a first press in one, and so is its release, so a double
            // click that closes a dialog can neither answer the next dialog nor act on the window behind it.
            case Win32.WmLeftButtonDoubleClick when Ui.HasMenu || ActiveDialog?.IsOnScrollBar(ToLogical(lParam)) == true:
                goto case Win32.WmLeftButtonDown;
            case Win32.WmLeftButtonDoubleClick:
                Pointer = ToLogical(lParam);
                if (!HasDialog && !pressedInDialog && !Ui.ContainsHit(Pointer)) OnDoubleClick(Pointer);
                return 0;
            case Win32.WmMouseWheel:
                DismissToolTip();
                Wheel(Win32.HighWord((nint)wParam));
                return 0;

            // As in WPF, whose progress dialog sets the wait cursor, the wait cursor shows anywhere on a progress dialog's
            // panel, including over its native message control, which lets this window set the cursor first.
            case Win32.WmSetCursor when Win32.LowWord(lParam) == HitClient && IsWaitCursorOver((nint)wParam):
                Win32.SetCursor(Win32.LoadCursor(0, Win32.IdcWait));
                return 1;
            case Win32.WmGetObject when (int)lParam == UiAutomation.RootObjectId:
                automation ??= new WindowAutomation(
                    window: Handle,
                    treeSource: () => Ui.Automation,
                    scaleSource: () => Scale,
                    invalidate: Invalidate);

                return automation.ReturnRoot(wParam, lParam);
        }

        return base.OnMessage(message, wParam, lParam);
    }

    // Every window's surface is rebuilt on the new device when it next ticks.
    private void RecoverDevice()
    {
        presentedCommands.Clear();
        NeedsPaint = true;
        try
        {
            graphics.Recreate();
        }
        catch (COMException exception) when (GraphicsDevice.IsDeviceLost(exception.HResult))
        {
            // The adapter may still be resetting; the next frame fails on the old device and retries.
        }
    }

    private bool IsPresented()
    {
        if (!rendered || !presentedScale.Equals(Scale) || presentedCommands.Count != drawCommands.Count) return false;

        for (var i = 0; i < drawCommands.Count; i++)
            if (!drawCommands[i].Matches(presentedCommands[i]))
                return false;

        return true;
    }

    /// <summary>
    /// Opens the tooltip of the region under the pointer once the pointer has stayed on it for the WPF delay; it
    /// stays open until the pointer leaves that region, a button is pressed or a key is typed.
    /// </summary>
    private void UpdateToolTip()
    {
        var now = Environment.TickCount64;
        var region = IsVisible && !HasDialog ? Ui.ToolTipAt(Pointer) : null;

        if (region?.Id != toolTipOwner)
        {
            if (toolTipText != null) CloseToolTip(now);
            toolTipOwner = region?.Id;
            toolTipSuppressed = false;
            var reopening = now - toolTipClosedAt <= ToolTipBetweenShowDelay;
            toolTipDue = reopening ? now : now + ToolTipInitialShowDelay;
        }

        if (region is not { } active || toolTipSuppressed || now < toolTipDue || active.Text == toolTipText) return;

        // An open tooltip whose text changes, such as a refreshed stream title, stays where it opened.
        if (toolTipText == null) Win32.GetCursorPos(out toolTipPointer);
        toolTip ??= new ToolTipWindow(Handle, ThemeSource);
        toolTip.Display(active.Text, toolTipPointer);
        toolTipText = active.Text;
    }

    private void DismissToolTip()
    {
        if (toolTipText != null) CloseToolTip(Environment.TickCount64);
        toolTipSuppressed = true;
    }

    private void CloseToolTip(long now)
    {
        toolTip?.Hide();
        toolTipText = null;
        toolTipClosedAt = now;
    }

    private bool IsOwnWindow(nint window) => window == Handle || Win32.IsChild(Handle, window);

    private bool IsWaitCursorOver(nint window) =>
        ActiveDialog is { IsProgress: true } dialog && IsOwnWindow(window) && dialog.PanelBounds.Contains(ReadPointer());

    // Windows sets the cursor again only when the pointer moves or the window under it changes, and a dialog drawn on this
    // window does neither; so when a progress dialog opens or closes under a still pointer, the window under it is asked
    // to set its cursor as a move would.
    private void UpdateWaitCursor()
    {
        if (!pointerInWaitArea && ActiveDialog is not { IsProgress: true }) return;

        Win32.GetCursorPos(out var cursor);
        var window = Win32.WindowFromPoint(cursor);
        var wait = IsWaitCursorOver(window);
        if (wait == pointerInWaitArea) return;

        pointerInWaitArea = wait;

        if (!IsOwnWindow(window)) return;

        const int MouseMoveShift = 16;
        var hitTest = HitClient | (nint)Win32.WmMouseMove << MouseMoveShift;
        Win32.SendMessage(window, Win32.WmSetCursor, wParam: (nuint)window, lParam: hitTest);
    }

    private PointF ReadPointer()
    {
        Win32.GetCursorPos(out var cursor);
        var pointerWindow = Win32.WindowFromPoint(cursor);

        // While this window captures the mouse, such as during a scroll bar drag, the pointer is followed beyond it.
        if (Win32.GetCapture() != Handle && pointerWindow != Handle && !Win32.IsChild(Handle, pointerWindow))
            return new PointF(-1, -1);

        Win32.ScreenToClient(Handle, ref cursor);
        return new PointF(cursor.X / Scale, cursor.Y / Scale);
    }

    private void UpdatePointer(PointF point)
    {
        if (Pointer == point) return;

        Pointer = point;
        if (Ui.MouseMove(point)) NeedsPaint = true;
        if (ActiveDialog?.MouseMove(point) == true) NeedsPaint = true;
        if (!HasDialog) OnPointerMove(point);
    }

    private PointF ToLogical(nint lParam) => new(Win32.LowWord(lParam) / Scale, Win32.HighWord(lParam) / Scale);

    private nint HitTest(nint lParam)
    {
        if (HasDialog) return HitClient;

        var position = new Win32.Point { X = Win32.LowWord(lParam), Y = Win32.HighWord(lParam) };
        Win32.ScreenToClient(Handle, ref position);
        var logical = new PointF(position.X / Scale, position.Y / Scale);
        var caption = IsCaption(logical) && !Ui.ContainsHit(logical) ? HitCaption : HitClient;
        if (!CanResize) return caption;

        var left = logical.X < ResizeBorder;
        var right = logical.X >= ClientSize.Width - ResizeBorder;
        var top = logical.Y < ResizeBorder;
        var bottom = logical.Y >= ClientSize.Height - ResizeBorder;
        if (top && left) return HitTopLeft;
        if (top && right) return HitTopRight;
        if (bottom && left) return HitBottomLeft;
        if (bottom && right) return HitBottomRight;
        if (left) return HitLeft;
        if (right) return HitRight;
        if (top) return HitTop;
        if (bottom) return HitBottom;

        return caption;
    }

    // Precision touchpads report fractions of a notch; scroll once the accumulated delta reaches whole notches.
    private void Wheel(int delta)
    {
        const int NotchDelta = 120;
        wheelDelta += delta;
        var steps = wheelDelta / NotchDelta;
        if (steps == 0) return;

        wheelDelta -= steps * NotchDelta;

        if (ActiveDialog is { } active) active.Scroll(steps);
        else if (!Ui.ScrollMenu(steps)) OnScroll(steps);

        NeedsPaint = true;
    }
}
