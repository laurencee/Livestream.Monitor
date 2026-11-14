using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

// Touch gesture declarations: https://learn.microsoft.com/windows/win32/wintouch/wm-gesture
internal static unsafe partial class Win32
{
    public const uint WmGesture = 0x0119;
    public const uint WmGestureNotify = 0x011A;

    // GID_PAN, and the GF_BEGIN flag of a gesture's first message.
    public const uint GestureIdPan = 4;
    public const uint GestureFlagBegin = 0x01;

    // GESTURECONFIG pan options: GC_PAN, the single-finger directions, the gutter that locks a pan to its first
    // direction, and inertia once the finger lifts.
    public const uint GestureConfigPan = 0x01;
    public const uint GestureConfigPanSingleFingerVertically = 0x02;
    public const uint GestureConfigPanSingleFingerHorizontally = 0x04;
    public const uint GestureConfigPanWithGutter = 0x08;
    public const uint GestureConfigPanWithInertia = 0x10;

    // POINTS
    [StructLayout(LayoutKind.Sequential)]
    public struct ShortPoint
    {
        public short X;
        public short Y;
    }

    // GESTURENOTIFYSTRUCT
    [StructLayout(LayoutKind.Sequential)]
    public struct GestureNotification
    {
        public uint Size;
        public uint Flags;
        public nint Target;
        public ShortPoint Location;
        public uint InstanceId;
    }

    // GESTUREINFO; its location is in screen coordinates.
    [StructLayout(LayoutKind.Sequential)]
    public struct GestureInfo
    {
        public uint Size;
        public uint Flags;
        public uint Id;
        public nint Target;
        public ShortPoint Location;
        public uint InstanceId;
        public uint SequenceId;
        public ulong Arguments;
        public uint ExtraArgumentsSize;
    }

    // GESTURECONFIG
    [StructLayout(LayoutKind.Sequential)]
    public struct GestureConfig
    {
        public uint Id;
        public uint Want;
        public uint Block;
    }

    // Mouse messages that Windows promotes from touch carry this signature, with its touch bit, in their extra information:
    // https://learn.microsoft.com/windows/win32/tablet/system-events-and-mouse-messages
    private const uint PromotedTouchSignature = 0xFF515780;
    private const uint PromotedTouchMask = 0xFFFFFF80;

    [LibraryImport("user32.dll")]
    private static partial nint GetMessageExtraInfo();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGestureInfo(nint gesture, ref GestureInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseGestureInfoHandle(nint gesture);

    // The BOOL result is not read: should the call fail, the window keeps its previous pan configuration, so the touch may
    // pan where it should arrive as mouse input, or the reverse.
    [LibraryImport("user32.dll")]
    private static partial void SetGestureConfig(
        nint window,
        uint reserved,
        uint count,
        in GestureConfig config,
        uint size);

    /// <summary>True when the mouse message being handled was promoted from touch, rather than sent for a mouse or pen.</summary>
    public static bool IsPromotedFromTouch() => ((uint)GetMessageExtraInfo() & PromotedTouchMask) == PromotedTouchSignature;

    /// <summary>Reads the WM_GESTURENOTIFY structure that the message's lParam points to.</summary>
    public static GestureNotification ReadGestureNotification(nint lParam) => *(GestureNotification*)lParam;

    /// <summary>Reads a WM_GESTURE message's gesture; false when its handle is no longer valid.</summary>
    public static bool TryGetGestureInfo(nint gesture, out GestureInfo info)
    {
        info = new GestureInfo { Size = (uint)sizeof(GestureInfo) };
        return GetGestureInfo(gesture, ref info);
    }

    /// <summary>Sets which pan gestures the window receives, for the touch about to start.</summary>
    public static void SetPanConfig(nint window, uint want, uint block)
    {
        var config = new GestureConfig { Id = GestureIdPan, Want = want, Block = block };
        SetGestureConfig(window, reserved: 0, count: 1, config, (uint)sizeof(GestureConfig));
    }
}
