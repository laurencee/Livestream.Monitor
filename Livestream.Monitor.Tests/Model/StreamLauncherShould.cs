using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model;
using Livestream.Monitor.ViewModels;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Model;

public class StreamLauncherShould
{
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
        var callbackCount = 0;

        var command = Command("ping -n 2 127.0.0.1 >nul & echo after-handoff");
        IReadOnlyDictionary<string, string> replacements = new Dictionary<string, string>();
        launcher.LaunchApp(output, command, replacements, OnClose);
        await WithTimeout(source.Polled.Task);
        if (hideOnLoad) await WithTimeout(output.Closed.Task);

        Assert.Equal(hideOnLoad ? 1 : 0, output.CloseCount);
        Assert.Equal(0, Volatile.Read(ref callbackCount));
        await WithTimeout(output.Completed.Task);
        Assert.Contains(Environment.NewLine + "after-handoff", output.MessageText);
        Assert.Equal(1, Volatile.Read(ref callbackCount));

        // ReSharper disable once AccessToModifiedClosure // Interlocked/Volatile synchronize this counter.
        void OnClose() => Interlocked.Increment(ref callbackCount);
    }

    [Fact]
    public async Task ReopenOnFailureAfterHiding()
    {
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(new Settings { HideStreamOutputMessageBoxOnLoad = true });
        var windows = Substitute.For<IWindowManager>();
        var shown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        windows.When(x => x.ShowWindow(Arg.Any<object>(), Arg.Any<object>(), Arg.Any<IDictionary<string, object>>()))
            .Do(_ => shown.TrySetResult(true));
        var launcher = new StreamLauncher(handler, windows) { ProcessWindows = new RootWindowSource() };
        var output = new RecordingOutput();

        launcher.LaunchApp(output, Command("ping -n 2 127.0.0.1 >nul & echo failed-after-handoff & exit /b 7"),
            new Dictionary<string, string>());
        await WithTimeout(output.Closed.Task);
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
        windows.When(x => x.ShowWindow(Arg.Any<object>(), Arg.Any<object>(), Arg.Any<IDictionary<string, object>>()))
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

        var command = Command("echo Starting^ player & echo diagnostic 1>&2 & ping -n 2 127.0.0.1 >nul & exit /b 0");
        launcher.LaunchApp(output, command, new Dictionary<string, string>());
        await WithTimeout(observed.Task);
        await WithTimeout(output.Closed.Task);

        Assert.Equal(0, await observed.Task);
        Assert.Contains(Environment.NewLine + "diagnostic", output.MessageText);
        windows.DidNotReceiveWithAnyArgs().ShowWindow(null);
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
        const int timeoutMilliseconds = 5000;
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMilliseconds));
        Assert.Same(task, completed);
        await task;
    }

    private sealed class RecordingOutput : MessageBoxViewModel
    {
        // ReSharper disable once InconsistentNaming // Project convention: private fields have no underscore prefix.
        private int closeCount;
        public int CloseCount => Volatile.Read(ref closeCount);
        public TaskCompletionSource<bool> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void TryClose(bool? dialogResult = null)
        {
            Interlocked.Increment(ref closeCount);
            Closed.TrySetResult(true);
            if (MessageText.Contains(Environment.NewLine + "after-handoff")) Completed.TrySetResult(true);
        }
    }

    private sealed class RootWindowSource : IProcessWindowSource
    {
        // ReSharper disable once InconsistentNaming // Project convention: private fields have no underscore prefix.
        private readonly WindowsProcessWindowSource native = new();
        // ReSharper disable once InconsistentNaming // Project convention: private fields have no underscore prefix.
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
}
