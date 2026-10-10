using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.WPF.Model;
using Livestream.Monitor.WPF.ViewModels;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Model;

public class StreamLauncherShould
{
    // Counted from the launch's close callback on a worker thread; xUnit runs each test on its own instance.
    private int closeCallbacks;

    // Output and error lines arrive on separate threads. The app marshals both to the WPF dispatcher, which runs them
    // one at a time; without a dispatcher, Caliburn runs them concurrently and can lose an append.
    public StreamLauncherShould() => PlatformProvider.Current = new SerialUiThreadPlatformProvider();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RespectTheSettingAndKeepReadingAfterWindowHandoff(bool hideOnLoad)
    {
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(new Settings { HideStreamOutputMessageBoxOnLoad = hideOnLoad });
        var source = new RootWindowSource();
        var launcher = new StreamLauncher(handler, Substitute.For<IWindowManager>()) { ProcessWindows = source };
        var output = new RecordingOutput();

        using var gate = new Gate();

        var command = Command($"{gate.WaitCommand} & echo after-handoff");
        IReadOnlyDictionary<string, string> replacements = new Dictionary<string, string>();
        launcher.LaunchApp(output, command, replacements, OnClose);
        await WithTimeout(source.Polled.Task);
        if (hideOnLoad) await WithTimeout(output.Closed.Task);

        Assert.Equal(hideOnLoad ? 1 : 0, output.CloseCount);
        Assert.Equal(0, Volatile.Read(ref closeCallbacks));
        gate.Open();
        await WithTimeout(output.Completed.Task);
        Assert.Contains(Environment.NewLine + "after-handoff", output.MessageText);
        Assert.Equal(1, Volatile.Read(ref closeCallbacks));

        void OnClose() => Interlocked.Increment(ref closeCallbacks);
    }

    [Fact]
    public async Task ReopenOnFailureAfterHiding()
    {
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(new Settings { HideStreamOutputMessageBoxOnLoad = true });
        var windows = Substitute.For<IWindowManager>();
        var shown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        windows.When(x => x.ShowWindowAsync(Arg.Any<object>(), Arg.Any<object>(), Arg.Any<IDictionary<string, object>>()))
            .Do(_ => shown.TrySetResult(true));
        var launcher = new StreamLauncher(handler, windows) { ProcessWindows = new RootWindowSource() };
        var output = new RecordingOutput();

        using var gate = new Gate();
        var command = Command($"{gate.WaitCommand} & echo failed-after-handoff & exit /b 7");
        launcher.LaunchApp(output, command, new Dictionary<string, string>());
        await WithTimeout(output.Closed.Task);
        gate.Open();
        await WithTimeout(shown.Task);

        Assert.Equal(1, output.CloseCount);
        Assert.Contains(Environment.NewLine + "failed-after-handoff", output.MessageText);
        Assert.Contains("code 7", output.MessageText);
    }

    [Fact]
    public async Task ShowStartupFailures()
    {
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(new Settings());
        var windows = Substitute.For<IWindowManager>();
        var shown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        windows.When(x => x.ShowWindowAsync(Arg.Any<object>(), Arg.Any<object>(), Arg.Any<IDictionary<string, object>>()))
            .Do(_ => shown.TrySetResult(true));
        var output = new RecordingOutput();
        var command = Command("");
        command.FilePath = Path.Combine(Environment.SystemDirectory, Guid.NewGuid().ToString("N") + ".exe");
        var launcher = new StreamLauncher(handler, windows);

        launcher.LaunchApp(output, command, new Dictionary<string, string>());
        await WithTimeout(shown.Task);

        Assert.Equal(0, output.CloseCount);
        Assert.Contains("ERROR", output.MessageText);
    }

    [Fact]
    public async Task WaitForCleanExitDespiteReadinessLikeOutputAndStandardError()
    {
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(new Settings { HideStreamOutputMessageBoxOnLoad = true });
        var windows = Substitute.For<IWindowManager>();
        var launcher = new StreamLauncher(handler, windows);
        var output = new RecordingOutput();
        var observed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(output.MessageText) && output.MessageText.Contains("Starting player"))
                observed.TrySetResult(output.CloseCount);
        };

        using var gate = new Gate();
        var command = Command($"echo Starting^ player & echo diagnostic 1>&2 & {gate.WaitCommand} & exit /b 0");
        launcher.LaunchApp(output, command, new Dictionary<string, string>());
        await WithTimeout(observed.Task);
        gate.Open();
        await WithTimeout(output.Closed.Task);

        Assert.Equal(0, await observed.Task);
        Assert.Contains(Environment.NewLine + "diagnostic", output.MessageText);
        await windows.DidNotReceiveWithAnyArgs().ShowWindowAsync(null);
    }

    private static ExecCommand Command(string arguments) => new()
    {
        FilePath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        Args = "/d /c " + arguments,
        CaptureStandardOutput = true,
        CaptureErrorOutput = true,
    };

    private static async Task WithTimeout(Task task)
    {
        const int TimeoutMilliseconds = 5000;
        var completed = await Task.WhenAny(task, Task.Delay(TimeoutMilliseconds));
        Assert.Same(task, completed);
        await task;
    }

    /// <summary>
    /// A named pipe that a command reads to its end, so the process continues only once the test opens the gate. The
    /// cmd type command opens the path twice, so two instances accept it.
    /// </summary>
    private sealed class Gate : IDisposable
    {
        private const int Instances = 2;

        private readonly NamedPipeServerStream[] pipes = new NamedPipeServerStream[Instances];

        public Gate()
        {
            var name = "Livestream.Monitor.Tests." + Guid.NewGuid().ToString("N");
            for (var i = 0; i < Instances; i++)
                pipes[i] = new NamedPipeServerStream(name, PipeDirection.Out, Instances);

            WaitCommand = $@"type \\.\pipe\{name} >nul 2>nul";
        }

        /// <summary>A command that waits until the gate opens.</summary>
        public string WaitCommand { get; }

        public void Open()
        {
            foreach (var pipe in pipes) pipe.Dispose();
        }

        public void Dispose() => Open();
    }

    private sealed class RecordingOutput : MessageBoxViewModel
    {
        private int closeCount;
        public int CloseCount => Volatile.Read(ref closeCount);
        public TaskCompletionSource<bool> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task TryCloseAsync(bool? dialogResult = null)
        {
            Interlocked.Increment(ref closeCount);
            Closed.TrySetResult(true);
            if (MessageText.Contains(Environment.NewLine + "after-handoff")) Completed.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class RootWindowSource : IProcessWindowSource
    {
        private readonly WindowsProcessWindowSource native = new();
        private int rootId;
        public TaskCompletionSource<bool> Polled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ProcessParent> GetProcesses() => [];
        public bool TryGetCreationTime(int processId, out long creationTime)
        {
            rootId = processId;
            return native.TryGetCreationTime(processId, out creationTime);
        }

        public List<int> GetApplicationWindowProcesses()
        {
            Polled.TrySetResult(true);
            return [rootId];
        }
    }

    private sealed class SerialUiThreadPlatformProvider : DefaultPlatformProvider
    {
        private readonly Lock uiThread = new();

        public override void OnUIThread(System.Action action)
        {
            lock (uiThread) action();
        }

        public override void BeginOnUIThread(System.Action action) => OnUIThread(action);
    }
}
