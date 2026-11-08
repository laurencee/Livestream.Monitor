using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

// Windows, messaging and input declarations: https://learn.microsoft.com/windows/win32/api/winuser/
internal static unsafe partial class Win32
{
    public const uint WsPopup = 0x80000000;
    public const uint WsChild = 0x40000000;
    public const uint WsVisible = 0x10000000;
    public const uint WsClipChildren = 0x02000000;
    public const uint WsCaption = 0x00C00000;
    public const uint WsSystemMenu = 0x00080000;
    public const uint WsThickFrame = 0x00040000;
    public const uint WsMinimizeBox = 0x00020000;
    public const uint WsTabStop = 0x00010000;
    public const uint WsExTransparent = 0x00000020;
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExNoActivate = 0x08000000;
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;

    public const uint WmDestroy = 0x0002;
    public const uint WmMove = 0x0003;
    public const uint WmSize = 0x0005;
    public const uint WmActivate = 0x0006;
    public const uint WmPaint = 0x000F;
    public const uint WmClose = 0x0010;
    public const uint WmQuit = 0x0012;
    public const uint WmEraseBackground = 0x0014;
    public const uint WmShowWindow = 0x0018;
    public const uint WmSetCursor = 0x0020;
    public const uint WmSetFont = 0x0030;
    public const uint WmGetObject = 0x003D;
    public const uint WmNotify = 0x004E;
    public const uint WmContextMenu = 0x007B;
    public const uint WmNonClientCalculate = 0x0083;
    public const uint WmNonClientHitTest = 0x0084;
    public const uint WmKeyDown = 0x0100;
    public const uint WmCommand = 0x0111;
    public const uint WmSysCommand = 0x0112;
    public const uint WmVerticalScroll = 0x0115;
    public const uint WmControlColorEdit = 0x0133;
    public const uint WmControlColorStatic = 0x0138;
    public const uint WmMouseMove = 0x0200;
    public const uint WmLeftButtonDown = 0x0201;
    public const uint WmLeftButtonUp = 0x0202;
    public const uint WmLeftButtonDoubleClick = 0x0203;
    public const uint WmRightButtonDown = 0x0204;
    public const uint WmMouseWheel = 0x020A;
    public const uint WmCaptureChanged = 0x0215;
    public const uint WmEnterSizeMove = 0x0231;
    public const uint WmExitSizeMove = 0x0232;
    public const uint WmDpiChanged = 0x02E0;
    public const uint WmApp = 0x8000;
    public const uint ScMinimize = 0xF020;

    public const int SwHide = 0;
    public const int SwMinimize = 6;
    public const int SwRestore = 9;
    public const int SwShowNoActivate = 4;
    public const nint HwndNotTopmost = -2;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpFrameChanged = 0x0020;
    public const uint SwpShowWindow = 0x0040;

    // The WM_SHOWWINDOW status sent when an owner window is restored.
    public const nint ShowParentOpening = 3;

    private const uint PmRemove = 0x0001;
    public const uint QsAllInput = 0x04FF;
    public const uint MwmoInputAvailable = 0x0004;
    public const uint MbIconError = 0x10;
    public const uint MonitorDefaultToNearest = 2;
    public const int IdcArrow = 32512;
    public const int IdcWait = 32514;
    public const uint UserDefaultScreenDpi = 96;
    public const int SmCyScreen = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowClass
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged<nint, uint, nuint, nint, nint> WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public uint Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(in WindowClass windowClass);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "CreateWindowExW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint window, out Rect bounds);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint window, ref Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint window, ref Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsChild(nint parent, nint window);

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(Point point);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint window, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessage(
        out Message message,
        nint window,
        uint filterMin,
        uint filterMax,
        uint removeMessage);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in Message message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in Message message);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessage(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [LibraryImport("user32.dll")]
    public static partial uint MsgWaitForMultipleObjectsEx(
        uint count,
        nint* handles,
        uint milliseconds,
        uint wakeMask,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint instance, nint cursorName);

    [LibraryImport("user32.dll")]
    public static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(
        nint deviceContext,
        Rect* clip,
        delegate* unmanaged<nint, nint, Rect*, nint, int> callback,
        nint data);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(
        nint owner,
        string text,
        string caption,
        uint type);

    [LibraryImport("user32.dll")]
    public static partial nint GetFocus();

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnableWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [LibraryImport("user32.dll")]
    public static partial nint SetCapture(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    public static partial nint GetCapture();

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint parameter, out int value, uint update);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int key);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(nint moduleName);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmFlush();

    /// <summary>Removes the next message for any window of the thread; false when the queue is empty.</summary>
    public static bool TryRemoveMessage(out Message message) =>
        PeekMessage(out message, window: 0, filterMin: 0, filterMax: 0, PmRemove);

    public static short LowWord(nint value) => (short)(value & 0xFFFF);

    public static short HighWord(nint value) => (short)((value >> 16) & 0xFFFF);

    /// <summary>True while the key is held down.</summary>
    public static bool IsKeyDown(int key) => GetKeyState(key) < 0;
}

/// <summary>Virtual-key codes: https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes</summary>
internal static class VirtualKey
{
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Delete = 0x2E;
    public const int A = 0x41;
}
