using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Livestream.Monitor.Core.Utility;

internal sealed class WindowsProcessWindowSource : IProcessWindowSource
{
    private const uint SnapshotProcesses = 0x00000002;
    private const uint QueryLimitedInformation = 0x00001000;
    private const uint WindowOwner = 4;
    private const int ExtendedWindowStyle = -20;
    private const int ToolWindowStyle = 0x00000080;
    private const int MaxPathCharacters = 260;
    private const int WindowClassCharacters = 256;

    public List<ProcessParent> GetProcesses()
    {
        var processes = new List<ProcessParent>();
        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot.IsInvalid) return processes;

        var entry = new NativeProcessEntry { Size = (uint)Marshal.SizeOf<NativeProcessEntry>() };
        if (!Process32FirstW(snapshot, ref entry)) return processes;

        do
        {
            processes.Add(new ProcessParent((int)entry.ProcessId, (int)entry.ParentProcessId));
        } while (Process32NextW(snapshot, ref entry));

        return processes;
    }

    public bool TryGetCreationTime(int processId, out long creationTime)
    {
        creationTime = 0;
        using var process = OpenProcess(QueryLimitedInformation, false, processId);
        if (process.IsInvalid) return false;

        return GetProcessTimes(process, out creationTime, out var exitTime, out _, out _) && exitTime == 0;
    }

    public List<int> GetApplicationWindowProcesses()
    {
        var processes = new List<int>();
        var className = new StringBuilder(WindowClassCharacters);
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (!IsWindowVisible(window) || GetWindow(window, WindowOwner) != IntPtr.Zero) return true;

            var isToolWindow = (GetWindowLongW(window, ExtendedWindowStyle) & ToolWindowStyle) != 0;
            if (isToolWindow) return true;
            if (GetClassNameW(window, className, className.Capacity) == 0) return true;
            if (!IsApplicationWindow(
                    visible: true,
                    owned: false,
                    toolWindow: false,
                    className: className.ToString()))
            {
                return true;
            }

            processes.Add((int)processId);
            return true;
        }, IntPtr.Zero);
        return processes;
    }

    internal static bool IsApplicationWindow(
        bool visible,
        bool owned,
        bool toolWindow,
        string className)
    {
        // ConsoleWindowClass is the Windows console host class, not a particular executable.
        // https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/obtain-console-window-handle
        return visible && !owned && !toolWindow && className != "ConsoleWindowClass";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPathCharacters)]
        public string Executable;
    }

    // ReSharper disable once ClassNeverInstantiated.Local - Constructed by the P/Invoke marshaler.
    private sealed class SnapshotHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern SnapshotHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(SnapshotHandle snapshot, ref NativeProcessEntry entry);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(SnapshotHandle snapshot, ref NativeProcessEntry entry);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        int processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out long creationTime,
        out long exitTime,
        out long kernelTime,
        out long userTime);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetWindowLongW(IntPtr window, int index);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, StringBuilder className, int maximumCount);
}
