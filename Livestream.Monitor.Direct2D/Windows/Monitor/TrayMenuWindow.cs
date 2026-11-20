using System.Drawing;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

/// <summary>Hosts the shared context menu while the monitor window may be hidden or minimized.</summary>
internal sealed class TrayMenuWindow : CanvasWindow
{
    private static readonly SurfaceUi.MenuChoice[] Choices = [new("Show"), new("Exit")];
    private readonly Action show;
    private readonly Action exit;
    private bool showPending;

    public TrayMenuWindow(Action show, Action exit, Func<ThemePalette> theme)
        : base(title: "Livestream Monitor tray menu", width: 1, height: 1)
    {
        this.show = show;
        this.exit = exit;
        ThemeSource = theme;
        Win32.SetWindowLongPtr(Handle, Win32.GwlStyle, unchecked((nint)Win32.WsPopup));
        Win32.SetWindowLongPtr(Handle, Win32.GwlExStyle, (nint)Win32.WsExToolWindow);
    }

    protected override bool CanResize => false;
    protected override bool HasWindowBorder => false;
    protected override bool PaintsWhileHidden => showPending;
    protected override bool IsCaption(PointF point) => false;

    public void Display(Win32.Point cursorPosition)
    {
        if (IsVisible) Hide();

        // Move the hidden window onto the cursor's monitor before measuring at its DPI.
        var workArea = Monitors.GetWorkArea(cursorPosition);
        var monitorX = workArea.Left + workArea.Width / 2;
        var monitorY = workArea.Top + workArea.Height / 2;
        const uint MoveFlags = Win32.SwpNoZOrder | Win32.SwpNoActivate;
        Win32.SetWindowPos(Handle, insertAfter: 0, x: monitorX, y: monitorY, width: 1, height: 1, flags: MoveFlags);

        var size = Ui.Popup(id: "tray", anchor: null, choices: Choices, chosen: Choose);
        var width = Math.Min(workArea.Width, (int)MathF.Ceiling(size.Width * Scale));
        var height = Math.Min(workArea.Height, (int)MathF.Ceiling(size.Height * Scale));
        var preferredY = cursorPosition.Y + height <= workArea.Bottom ? cursorPosition.Y : cursorPosition.Y - height;
        var (x, y) = Monitors.ClampToWorkArea(cursorPosition.X, preferredY, width, height, workArea);
        const uint PositionFlags = Win32.SwpNoActivate | Win32.SwpFrameChanged;
        nint topmost = -1;
        Win32.SetWindowPos(Handle, topmost, x, y, width, height, PositionFlags);

        showPending = true;
        Tick();
    }

    public override void Hide()
    {
        showPending = false;
        Ui.CloseMenu();
        if (Win32.GetCapture() == Handle) Win32.ReleaseCapture();

        base.Hide();
    }

    protected override void Draw(SizeF size) { }
    protected override void RequestClose() => Hide();

    protected override void OnPresented()
    {
        if (!showPending) return;

        showPending = false;
        Show();
        if (Win32.SetForegroundWindow(Handle)) Win32.SetCapture(Handle);
        else Hide();
    }

    protected override void BeforeFrame()
    {
        if (IsVisible && !Ui.HasMenu) Hide();
    }

    protected override nint? OnMessage(uint message, nuint wParam, nint lParam)
    {
        // A press outside the popup can transfer either mouse capture or activation to another window.
        if (Ui.HasMenu && (message is Win32.WmCaptureChanged or Win32.WmRightButtonDown ||
            message == Win32.WmActivate && Win32.LowWord((nint)wParam) == 0)) Hide();

        return base.OnMessage(message, wParam, lParam);
    }

    private void Choose(int index)
    {
        Hide();
        if (index == 0) show();
        else exit();
    }
}
