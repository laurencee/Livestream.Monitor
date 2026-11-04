using System.Diagnostics;
using System.Text.Json;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Windows;
using Livestream.Monitor.Direct2D.Windows.Monitor;

namespace Livestream.Monitor.Direct2D;

internal static class Program
{
    /// <summary>The single-instance name, made per user by appending the user name.</summary>
    internal const string InstanceName = "Livestream Monitor Direct2D";

    // Set once the window exists; a later launch's request before then has no window to show.
    private static MonitorWindow? mainWindow;

    public static string? MonitorName { get; private set; }
    public static int WindowWidth { get; private set; } = 810;
    public static int WindowHeight { get; private set; } = 480;
    public static UiDispatcher Dispatcher { get; private set; } = null!;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            MonitorName = Option(args, "--monitor");
            if (Option(args, "--width") is { } width) WindowWidth = int.Parse(width);
            if (Option(args, "--height") is { } height) WindowHeight = int.Parse(height);

            var exitAfter = Option(args, "--exit-after");
            var exitSeconds = double.TryParse(exitAfter, out var seconds) ? seconds : double.PositiveInfinity;
            var snapshot = Option(args, "--snapshot");
            var dataDirectory = Option(args, "--data-directory");
            if (dataDirectory != null) Directory.SetCurrentDirectory(dataDirectory);

            const uint ApartmentThreaded = 2;
            ComObject.Check(Win32.CoInitializeEx(0, ApartmentThreaded));
            try
            {
                return Run(exitSeconds, snapshot);
            }
            finally
            {
                Win32.CoUninitialize();
            }
        }
        catch (Exception exception)
        {
            File.AppendAllText("direct2d-errors.log", $"{DateTimeOffset.Now:o} {exception}{Environment.NewLine}");

            if (!args.Contains("--quiet"))
                Win32.MessageBox(owner: 0, exception.Message, caption: "Livestream Monitor", Win32.MbIconError);

            return 1;
        }
    }

    private static unsafe int Run(double exitSeconds, string? snapshot)
    {
        Dispatcher = new UiDispatcher();

        using var instance = SingleInstance.Claim(InstanceName, () => Dispatcher.Post(() => mainWindow?.Restore()));
        if (instance == null) return 0;

        ExternalAPIs.HttpClientExtensions.PreconnectProviderApis();

        SynchronizationContext.SetSynchronizationContext(Dispatcher);

        // As in WPF, loading and querying the followed channels starts before the window and its graphics device are created.
        var dialogs = new NativeDialogs(() => mainWindow!);
        var monitorState = new MonitorWindowState(dialogs, showProgress: dialogs.Progress);
        monitorState.BeginInitialQuery();

        using var window = new MonitorWindow(monitorState, dialogs);
        mainWindow = window;
        _ = window.Initialize();

        var clock = Stopwatch.StartNew();
        var savedSnapshot = false;

        while (!window.IsClosed && clock.Elapsed.TotalSeconds < exitSeconds)
        {
            while (Win32.TryRemoveMessage(out var message))
            {
                if (message.Id == Win32.WmQuit) return 0;
                if (DesktopWindow.PreprocessMessage(message)) continue;

                Win32.TranslateMessage(message);
                Win32.DispatchMessage(message);
            }

            Dispatcher.Drain();
            DesktopWindow.ThrowIfFailed();
            DesktopWindow.TickAll();

            // Wait for the first full refresh before taking a snapshot.
            var settled = window.State.StreamsModel.CanRefreshLivestreams || window.State.StreamsModel.Livestreams.Count == 0;
            if (!savedSnapshot && snapshot != null && window.State.StreamsModel.Initialised && !window.State.Busy && settled)
            {
                window.Snapshot(snapshot);
                var state = new
                {
                    monitored = window.State.StreamsModel.Livestreams.Count,
                    live = window.State.StreamsModel.Livestreams.Count(stream => stream.Live),
                    window.State.Status,
                    renderer = "Direct2D/DirectWrite/DXGI/DirectComposition",
                };

                File.WriteAllText(snapshot + ".json", JsonSerializer.Serialize(state));
                savedSnapshot = true;
            }

            var waitMilliseconds = DesktopWindow.GetFrameWaitMilliseconds();
            Win32.MsgWaitForMultipleObjectsEx(
                0,
                null,
                waitMilliseconds,
                Win32.QsAllInput,
                Win32.MwmoInputAvailable);
        }

        SynchronizationContext.SetSynchronizationContext(null);
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
