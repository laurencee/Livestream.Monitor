using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

// Child controls, GDI, menus, clipboard and COM declarations.
internal static unsafe partial class Win32
{
    public const uint EsMultiLine = 0x0004;
    public const uint EsAutoVerticalScroll = 0x0040;
    public const uint EsAutoHorizontalScroll = 0x0080;
    public const uint EsReadOnly = 0x0800;
    public const uint EmSetSelection = 0x00B1;
    public const uint EmSetMargins = 0x00D3;
    public const nuint EcLeftMargin = 0x0001;
    public const nuint EcRightMargin = 0x0002;
    public const int EnChange = 0x0300;
    public const int SbVertical = 1;
    public const uint TpmReturnCommand = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    public struct NotificationHeader
    {
        public nint Window;
        public nuint Id;
        public uint Code;
    }

    // RichEdit.h uses four-byte packing for MSGFILTER, including on x64.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct MessageFilter
    {
        public NotificationHeader Header;
        public uint MessageId;
        public nuint WParam;
        public nint LParam;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CharacterFormat
    {
        private const int FontFamilyLength = 32;
        public uint Size;
        public uint Mask;
        public uint Effects;
        public int Height;
        public int Offset;
        public uint TextColor;
        public byte CharacterSet;
        public byte PitchAndFamily;
        public fixed char FontFamily[FontFamilyLength];
    }

    // TEXTMETRICW; only its leading height fields are read.
    [StructLayout(LayoutKind.Sequential)]
    public struct TextMetrics
    {
        private const int RemainingBytes = 48;
        public int Height;
        public int Ascent;
        public int Descent;
        private fixed byte remaining[RemainingBytes];
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint window, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowText(nint window, string text);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static partial int GetWindowText(nint window, char* text, int count);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(nint window);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint context, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetBkColor(nint context, uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint characterSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string face);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint value);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint window, nint context);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint context, nint value);

    // The extent is a SIZE, laid out as Point's two ints (cx, cy).
    [LibraryImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextExtentPoint32(
        nint context,
        char* text,
        int length,
        out Point size);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextMetricsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextMetrics(nint context, out TextMetrics metrics);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenu(
        nint menu,
        uint flags,
        nuint id,
        string title);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenu(
        nint menu,
        uint flags,
        int x,
        int y,
        int reserved,
        nint owner,
        nint rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    public static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalFree(nint memory);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint mode);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    public static string GetText(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length == 0) return "";

        // The count includes the terminating null that GetWindowText writes.
        const int StackLimit = 256;
        var buffer = length < StackLimit ? stackalloc char[length + 1] : new char[length + 1];
        fixed (char* text = buffer)
        {
            var copied = GetWindowText(window, text, buffer.Length);
            return new string(text, 0, copied);
        }
    }

    /// <summary>Converts an ARGB colour to a GDI COLORREF (0x00BBGGRR).</summary>
    public static uint ColorRef(uint argb) => ((argb & 0xFF) << 16) | (argb & 0xFF00) | ((argb >> 16) & 0xFF);
}
