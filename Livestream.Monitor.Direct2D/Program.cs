using System.Diagnostics;
using System.Runtime.InteropServices;
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

            var toastLaunch = args.Contains(StreamNotifications.ActivationSwitch);
            const uint SingleThreaded = 0;
            ComObject.Check(WindowsToast.RoInitialize(SingleThreaded));
            try
            {
                return Run(exitSeconds, snapshot, toastLaunch);
            }
            finally
            {
                WindowsToast.RoUninitialize();
            }
        }
        catch (Exception exception)
        {
            File.AppendAllText("direct2d-errors.log", $"{DateTimeOffset.Now:o} {exception}{Environment.NewLine}");

            if (!args.Contains("--quiet") && !args.Contains(StreamNotifications.ActivationSwitch))
                Win32.MessageBox(owner: 0, exception.Message, caption: "Livestream Monitor", Win32.MbIconError);

            return 1;
        }
    }

    private static unsafe int Run(double exitSeconds, string? snapshot, bool toastLaunch)
    {
        Dispatcher = new UiDispatcher();

        StreamNotificationTarget? pending = null;
        var primary = ClaimInstance();
        if (primary == null && toastLaunch) (primary, pending) = ForwardToastActivation();

        using var instance = primary;
        if (instance == null) return 0;

        using var activationPipe = new ToastActivationPipe(AcceptForwardedActivation);
        using var notifications = new StreamNotifications(Activate);
        if (!toastLaunch) notifications.RefreshMovedRegistration();

        ExternalAPIs.HttpClientExtensions.PreconnectProviderApis();

        SynchronizationContext.SetSynchronizationContext(Dispatcher);

        // As in WPF, loading and querying the followed channels starts before the window and its graphics device are created.
        var dialogs = new NativeDialogs(() => mainWindow!);
        var monitorState = new MonitorWindowState(dialogs, showProgress: dialogs.Progress, openBrowsingError: dialogs.OpenMessage);
        monitorState.BeginInitialQuery();

        using var window = new MonitorWindow(monitorState, dialogs, notifications);
        mainWindow = window;
        _ = window.Initialize();
        if (pending is { } initialTarget) window.ActivateNotification(initialTarget);

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
            var settled = window.State.FollowedRowsSettled;
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

        static bool Activate(StreamNotificationTarget target)
        {
            if (mainWindow is { } window) return window.ActivateNotification(target);

            Dispatcher.Post(() => mainWindow?.ActivateNotification(target));
            return true;
        }
    }

    private static Task<bool> AcceptForwardedActivation(StreamNotificationTarget target, CancellationToken cancellation)
    {
        var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.Post(() =>
        {
            if (cancellation.IsCancellationRequested) return;

            accepted.SetResult(mainWindow?.ActivateNotification(target) == true);
        });

        return accepted.Task.WaitAsync(cancellation);
    }

    private static SingleInstance? ClaimInstance() =>
        SingleInstance.Claim(InstanceName, () => Dispatcher.Post(() => mainWindow?.Restore()));

    private static (SingleInstance? Instance, StreamNotificationTarget? Target) ForwardToastActivation()
    {
        (SingleInstance?, StreamNotificationTarget?) takeover = default;
        using var completion = new ManualResetEvent(false);
        using var activator = StreamNotifications.RegisterActivator(target =>
        {
            try
            {
                ToastActivationPipe.Forward(target);
                return 0;
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                // The old instance can still hold the claim while its window and pipe are shutting down.
                const int ClaimRetryMilliseconds = 750;
                const int ClaimPollMilliseconds = 50;
                var deadline = Environment.TickCount64 + ClaimRetryMilliseconds;
                do
                {
                    if (ClaimInstance() is { } replacement)
                    {
                        takeover = (replacement, target);
                        return 0;
                    }

                    Thread.Sleep(ClaimPollMilliseconds);
                } while (Environment.TickCount64 < deadline);

                StreamNotifications.LogFailure(exception);
                return Marshal.GetHRForException(exception);
            }
            finally
            {
                // COM dispatches the callback synchronously on this STA while WaitForCallback pumps it.
                // ReSharper disable once AccessToDisposedClosure
                completion.Set();
            }
        });

        ToastActivator.WaitForCallback(completion);
        return takeover;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
