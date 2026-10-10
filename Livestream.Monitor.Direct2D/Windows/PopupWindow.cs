using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Windows;

/// <summary>A click-through popup that never takes activation, shown over its owner, such as a tooltip.</summary>
internal abstract class PopupWindow : CanvasWindow
{
    protected PopupWindow(
        string title,
        int width,
        int height,
        nint owner)
        : base(title: title, width: width, height: height, owner: owner)
    {
        Win32.SetWindowLongPtr(Handle, Win32.GwlStyle, unchecked((nint)Win32.WsPopup));
        var extendedStyle = Win32.WsExToolWindow | Win32.WsExNoActivate | Win32.WsExTransparent;
        Win32.SetWindowLongPtr(Handle, Win32.GwlExStyle, (nint)extendedStyle);
    }

    protected override nint? OnMessage(uint message, nuint wParam, nint lParam)
    {
        const int HitTransparent = -1;
        if (message == Win32.WmNonClientHitTest) return HitTransparent;

        return base.OnMessage(message, wParam, lParam);
    }
}
