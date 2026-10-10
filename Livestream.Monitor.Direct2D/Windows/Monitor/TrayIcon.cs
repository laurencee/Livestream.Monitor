using System.Runtime.InteropServices;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

/// <summary>The notification-area icon with its Show/Exit menu and balloon notifications.</summary>
internal sealed class TrayIcon : IDisposable
{
    public const uint CallbackMessage = Win32.WmApp + 1;

    /// <summary>Broadcast when Explorer (re)creates the taskbar, which drops existing notification icons.</summary>
    public static readonly uint TaskbarCreatedMessage = Win32.RegisterWindowMessage("TaskbarCreated");

    private const uint Add = 0;
    private const uint Modify = 1;
    private const uint Delete = 2;
    private const uint MessageFlag = 1;
    private const uint IconFlag = 2;
    private const uint TipFlag = 4;
    private const uint InfoFlag = 0x10;
    private const uint StandardFlags = MessageFlag | IconFlag | TipFlag;
    private const nuint ShowCommand = 1;
    private const nuint ExitCommand = 2;
    private readonly Action show;
    private readonly Action exit;
    private NotifyIconData data;
    private Action? notificationClicked;

    public TrayIcon(nint window, Action show, Action exit)
    {
        this.show = show;
        this.exit = exit;
        data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = window,
            Id = 1,
            Flags = StandardFlags,
            Callback = CallbackMessage,
            Icon = LoadApplicationIcon(),
            Tip = "Livestream Monitor",
            Info = "",
            InfoTitle = "",
        };

        ShellNotifyIcon(Add, ref data);
    }

    /// <summary>Adds the icon again after Explorer restarted.</summary>
    public void Recreate()
    {
        data.Flags = StandardFlags;
        ShellNotifyIcon(Add, ref data);
    }

    private static nint LoadApplicationIcon()
    {
        ExtractIconEx(file: Environment.ProcessPath!, index: 0, out var large, out var small, count: 1);
        var icon = small != 0 ? small : large;
        if (large != 0 && large != icon) DestroyIcon(large);
        if (icon == 0) throw new InvalidOperationException("The application icon could not be loaded.");

        return icon;
    }

    public void Notify(string? title, string? message, Action? clicked = null)
    {
        title ??= "";
        message ??= "";
        const int TitleLimit = 63;
        const int MessageLimit = 255;
        data.Flags = InfoFlag;
        data.InfoTitle = title.Length > TitleLimit ? title[..TitleLimit] : title;
        data.Info = message.Length > MessageLimit ? message[..MessageLimit] : message;
        data.InfoFlags = 1;
        notificationClicked = clicked;
        ShellNotifyIcon(Modify, ref data);
    }

    public void OnMessage(nint message)
    {
        const uint RightButtonUp = 0x0205;
        const uint BalloonClicked = 0x0405;
        if ((uint)message == BalloonClicked) notificationClicked?.Invoke();
        if ((uint)message == Win32.WmLeftButtonDoubleClick) show();
        if ((uint)message != RightButtonUp) return;

        var menu = Win32.CreatePopupMenu();
        try
        {
            Win32.AppendMenu(menu, flags: 0, ShowCommand, title: "Show");
            Win32.AppendMenu(menu, flags: 0, ExitCommand, title: "Exit");
            Win32.GetCursorPos(out var point);
            Win32.SetForegroundWindow(data.Window);
            var command = (nuint)Win32.TrackPopupMenu(
                menu,
                Win32.TpmReturnCommand,
                point.X,
                point.Y,
                reserved: 0,
                data.Window,
                rect: 0);

            if (command == ShowCommand) show();
            if (command == ExitCommand) exit();
        }
        finally
        {
            Win32.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        ShellNotifyIcon(Delete, ref data);
        if (data.Icon != 0) DestroyIcon(data.Icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint action, ref NotifyIconData data);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string file,
        int index,
        out nint large,
        out nint small,
        uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
