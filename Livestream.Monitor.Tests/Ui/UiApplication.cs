using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>
/// A Release build of a frontend started on a <see cref="UiTestProfile"/>, and stopped on dispose. It needs an
/// interactive desktop; its window opens on a secondary display when there is one, away from the user's work, but may
/// still take focus while it runs.
/// </summary>
internal sealed class UiApplication : IDisposable
{
    // Written by the test project's build, which builds the frontends in Release for these tests.
    private const string AppListFileName = "ui-apps.txt";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(5);

    private readonly Process process;

    private UiApplication(Process process) => this.process = process;

    /// <summary>The main window, or zero until it is shown; fails the test once the application has exited.</summary>
    public IntPtr MainWindow
    {
        get
        {
            process.Refresh();
            if (process.HasExited) Assert.Fail($"The application exited with code {process.ExitCode}.");

            return process.MainWindowHandle;
        }
    }

    public static UiApplication Launch(
        string executable,
        string singleInstanceName,
        UiTestProfile profile,
        params string[] arguments)
    {
        // A second instance only activates the first and exits, so the test would observe the user's window.
        if (Mutex.TryOpenExisting(singleInstanceName + Environment.UserName, out var running))
        {
            running.Dispose();
            Assert.Fail($"Close the running {Path.GetFileNameWithoutExtension(executable)} before running UI tests.");
        }

        var start = new ProcessStartInfo(FindExecutable(executable))
        {
            WorkingDirectory = profile.DataDirectory,
            UseShellExecute = false,
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        if (Screen.AllScreens.FirstOrDefault(screen => !screen.Primary) is { } secondary)
        {
            start.ArgumentList.Add("--monitor");
            start.ArgumentList.Add(secondary.DeviceName);
        }

        return new UiApplication(Process.Start(start));

        static string FindExecutable(string executable)
        {
            var listed = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, AppListFileName));
            var path = listed.FirstOrDefault(line => IsExecutable(line, executable));
            var missing = $"{executable} is not in {AppListFileName}; build the tests.";
            return path ?? throw new InvalidOperationException(missing);
        }

        static bool IsExecutable(string path, string executable) =>
            Path.GetFileName(path).Equals(executable, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Polls the probe until it returns a value, failing with the description, and what was last seen when given,
    /// after the timeout.
    /// </summary>
    public static T WaitFor<T>(
        Func<T> probe,
        TimeSpan timeout,
        string description,
        Func<string> lastSeen = null) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;

        do
        {
            if (probe() is { } value) return value;

            Thread.Sleep(PollInterval);
        } while (DateTime.UtcNow < deadline);

        var seen = lastSeen?.Invoke() is { Length: > 0 } last ? $" Last seen: {last}" : "";
        Assert.Fail($"{description} did not appear within {timeout.TotalSeconds} seconds.{seen}");
        return null;
    }

    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.Kill();
            process.WaitForExit(ExitTimeout);
        }

        process.Dispose();
    }
}
