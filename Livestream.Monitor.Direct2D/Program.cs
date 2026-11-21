using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Windows;
using Livestream.Monitor.Direct2D.Windows.Monitor;

namespace Livestream.Monitor.Direct2D;

internal static class Program
{
    /// <summary>The single-instance name of the default profile, made per user by appending the user name.</summary>
    internal const string InstanceName = "Livestream Monitor Direct2D";

    internal const string ErrorLogFile = "errors.log";

    // Appended to the names by which instances find each other, so each data directory has its own running instance.
    private static string profileScope = "";

    // Set once the window exists; a later launch's request before then has no window to show.
    private static MonitorWindow? mainWindow;

    // Where machine-local state is kept: in local application data for the default profile, otherwise with the profile's other data.
    private static string localDataDirectory = "";

    public static string WindowLocationPath => Path.Combine(localDataDirectory, WindowLocationFile.FileName);
    public static string CachePath => Path.Combine(localDataDirectory, CacheHandler.CacheFileName);

    public static string? MonitorName { get; private set; }
    public static int WindowWidth { get; private set; } = 810;
    public static int WindowHeight { get; private set; } = MonitorWindow.DefaultHeight;
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
            if (dataDirectory != null)
            {
                profileScope = GetProfileScope(dataDirectory);
                Directory.SetCurrentDirectory(dataDirectory);
            }

            // A toast activation names the default profile's directory too, so the scope, not the presence of a data directory,
            // identifies the default profile.
            if (profileScope.Length == 0)
            {
                var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                localDataDirectory = Path.Combine(localData, "Livestream.Monitor");
            }

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
            File.AppendAllText(ErrorLogFile, $"{DateTimeOffset.Now:o} {exception}{Environment.NewLine}");

            if (!args.Contains("--quiet") && !args.Contains(StreamNotifications.ActivationSwitch))
                Win32.MessageBox(owner: 0, exception.Message, caption: "Livestream Monitor", Win32.MbIconError);

            return 1;
        }
    }

    private static unsafe int Run(double exitSeconds, string? snapshot, bool toastLaunch)
    {
        Dispatcher = new UiDispatcher();

        (SingleInstance? primary, ToastActivation? pending) = toastLaunch
            ? ReceiveToastActivation()
            : (ClaimInstance().Instance, null);

        using var instance = primary;
        if (instance == null) return 0;

        // Start loading the driver now, so it loads while the window is created and startup is faster. If nothing takes the
        // device, it is left for process exit to release.
        GraphicsDevice.StartHardwareDeviceCreation();

        // Only the launch that holds the instance moves the legacy files. A launch that hands off to a running older version
        // leaves the files to that version.
        if (localDataDirectory.Length > 0)
        {
            LegacyDataFile.Move(WindowLocationFile.FileName, WindowLocationPath);
            LegacyDataFile.Move(CacheHandler.CacheFileName, CachePath);
        }

        using var activationPipe = new ToastActivationPipe(AcceptForwardedActivation, ToastActivationPipe.GetPipeName(profileScope));
        using var notifications = new StreamNotifications(Activate);
        if (!toastLaunch) notifications.RefreshMovedRegistration();

        ExternalAPIs.HttpClientExtensions.PreconnectProviderApis();

        SynchronizationContext.SetSynchronizationContext(Dispatcher);

        // Loading and querying the followed channels starts before the window and its graphics device are created.
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

        static bool Activate(ToastActivation target)
        {
            if (mainWindow is { } window) return window.ActivateNotification(target);

            Dispatcher.Post(() => mainWindow?.ActivateNotification(target));
            return true;
        }
    }

    private static Task<bool> AcceptForwardedActivation(ToastActivation target, CancellationToken cancellation)
    {
        var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.Post(() =>
        {
            if (cancellation.IsCancellationRequested) return;

            accepted.SetResult(mainWindow?.ActivateNotification(target) == true);
        });

        return accepted.Task.WaitAsync(cancellation);
    }

    private static (bool Claimed, SingleInstance? Instance) ClaimInstance(ToastActivation? target = null)
    {
        var name = InstanceName + profileScope;

        // A normal launch must wait for a cold opt-out to save and release its temporary instance.
        using var startup = new Mutex(initiallyOwned: false, name + Environment.UserName + ".Startup");
        try
        {
            startup.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The process that owned startup exited. This caller now owns the mutex.
        }

        try
        {
            var instance = SingleInstance.Claim(name, () => Dispatcher.Post(() => mainWindow?.Restore()), activateExisting: target == null);
            if (instance == null) return (false, null);
            if (target is not { TrayAction: TrayToastAction.DisablePopup }) return (true, instance);

            using (instance)
            {
                var settingsHandler = new SettingsHandler
                {
                    Settings = { DisableMinimizeToTrayNotification = true },
                };

                settingsHandler.SaveSettings();
            }

            return (true, null);
        }
        finally
        {
            startup.ReleaseMutex();
        }
    }

    private static (SingleInstance? Instance, ToastActivation? Target) ReceiveToastActivation()
    {
        (SingleInstance?, ToastActivation?) startup = default;
        using var completion = new ManualResetEvent(false);
        using var activator = StreamNotifications.RegisterActivator(target =>
        {
            try
            {
                if (TryClaim(target)) return 0;

                ToastActivationPipe.Forward(target, ToastActivationPipe.GetPipeName(profileScope));
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
                    if (TryClaim(target)) return 0;

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
        return startup;

        bool TryClaim(ToastActivation target)
        {
            var claim = ClaimInstance(target);
            if (!claim.Claimed) return false;

            startup = (claim.Instance, target);
            return true;
        }
    }

    /// <summary>
    /// The suffix that keeps the instances of one data directory apart from those of another. The default profile, beside
    /// the executable, has none, so it still finds a running instance of an earlier version.
    /// </summary>
    internal static string GetProfileScope(string dataDirectory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        var defaultPath = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (string.Equals(path, defaultPath, StringComparison.OrdinalIgnoreCase)) return "";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));

        // Part of the hash is enough to tell directories apart and keeps the names of the shared objects short.
        const int ScopeBytes = 8;
        return "." + Convert.ToHexString(hash, 0, ScopeBytes);
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
