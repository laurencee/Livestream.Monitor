using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Livestream.Monitor.Core.Utility;
using Xunit;

namespace Livestream.Monitor.Tests;

public class ProcessWindowObserverShould
{
    private const int RootId = 100;
    private const int ChildId = 200;
    private const int GrandchildId = 300;
    private const int UnrelatedId = 400;
    private const long RootTime = 1000;
    private const long ChildTime = 2000;
    private const long GrandchildTime = 3000;

    [Fact]
    public void FollowGrandchildrenRegardlessOfSnapshotOrder()
    {
        var source = new FakeSource { WindowProcessId = GrandchildId };
        source.Processes.Add(new ProcessParent(GrandchildId, ChildId));
        source.Processes.Add(new ProcessParent(ChildId, RootId));
        source.Times.Add(ChildId, ChildTime);
        source.Times.Add(GrandchildId, GrandchildTime);
        var observer = new ProcessWindowObserver(source, RootId, RootTime);

        Assert.True(observer.HasWindow());
    }

    [Fact]
    public void IgnoreAnInaccessibleParent()
    {
        var source = new FakeSource { WindowProcessId = GrandchildId };
        source.Processes.Add(new ProcessParent(ChildId, RootId));
        source.Processes.Add(new ProcessParent(GrandchildId, ChildId));
        source.Times.Add(GrandchildId, GrandchildTime);
        var observer = new ProcessWindowObserver(source, RootId, RootTime);

        Assert.False(observer.HasWindow());
    }

    [Fact]
    public void RejectRootPidReuse()
    {
        var source = new FakeSource
        {
            WindowProcessId = RootId,
            Times = { [RootId] = ChildTime },
        };
        var observer = new ProcessWindowObserver(source, RootId, RootTime);

        Assert.False(observer.HasWindow());
    }

    [Fact]
    public void RejectDescendantPidReuse()
    {
        var source = new FakeSource
        {
            WindowProcessId = UnrelatedId,
            Times = { [UnrelatedId] = GrandchildTime },
        };
        source.Processes.Add(new ProcessParent(ChildId, RootId));
        source.Times.Add(ChildId, ChildTime);
        var observer = new ProcessWindowObserver(source, RootId, RootTime);
        Assert.False(observer.HasWindow());
        Assert.Equal(1, source.SnapshotCount);

        source.Times[ChildId] = GrandchildTime;
        source.Processes.Clear();
        source.Processes.Add(new ProcessParent(ChildId, UnrelatedId));
        source.WindowProcessId = ChildId;
        Assert.False(observer.HasWindow());
    }

    [Fact]
    public void RecheckIdentityAfterWindowEnumeration()
    {
        var source = new FakeSource { WindowProcessId = RootId };
        source.AfterFind = () => source.Times.Remove(RootId);
        var observer = new ProcessWindowObserver(source, RootId, RootTime);

        Assert.False(observer.HasWindow());
    }

    [Fact]
    public async Task ObserveDelayedWindows()
    {
        var source = new FakeSource();
        source.AfterFind = () => source.WindowProcessId = RootId;
        var observer = new ProcessWindowObserver(source, RootId, RootTime);
        using var cancellation = new CancellationTokenSource(System.TimeSpan.FromSeconds(5));

        Assert.True(await observer.WaitForWindow(cancellation.Token, () => false));
        Assert.Equal(2, source.FindCount);
    }

    [Theory]
    [InlineData("cancelled before polling", 0)]
    [InlineData("cancelled during a poll", 1)]
    [InlineData("application shutting down", 1)]
    public async Task StopObservingWhenCancelledOrShuttingDown(string reason, int expectedPolls)
    {
        using var cancellation = new CancellationTokenSource();
        var source = new FakeSource();
        if (reason == "cancelled before polling") await cancellation.CancelAsync();

        if (reason == "cancelled during a poll")
        {
            source.WindowProcessId = RootId;
            source.AfterFind = cancellation.Cancel;
        }

        var shuttingDown = reason == "application shutting down";
        var observer = new ProcessWindowObserver(source, RootId, RootTime);

        Assert.False(await observer.WaitForWindow(cancellation.Token, () => shuttingDown && source.FindCount > 0));
        Assert.Equal(expectedPolls, source.FindCount);
    }

    [Fact]
    public void AvoidSnapshotsUntilAPossiblePlayerWindowAppears()
    {
        var source = new FakeSource();
        var observer = new ProcessWindowObserver(source, RootId, RootTime);
        Assert.False(observer.HasWindow());
        Assert.Equal(0, source.SnapshotCount);

        source.Processes.Add(new ProcessParent(ChildId, RootId));
        source.Times[ChildId] = RootTime - 1;
        source.WindowProcessId = ChildId;
        Assert.False(observer.HasWindow());
        Assert.Equal(0, source.SnapshotCount);

        source.WindowProcessId = RootId;
        Assert.True(observer.HasWindow());
        Assert.Equal(0, source.SnapshotCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AvoidRepeatedSnapshotsForUnrelatedWindowsWithoutDelayingNewOnes(bool unrelatedParentKnown)
    {
        var source = new FakeSource
        {
            WindowProcessId = UnrelatedId,
            Times = { [UnrelatedId] = GrandchildTime },
        };
        if (unrelatedParentKnown) source.Processes.Add(new ProcessParent(UnrelatedId, 0));
        var observer = new ProcessWindowObserver(source, RootId, RootTime);
        Assert.False(observer.HasWindow());
        Assert.False(observer.HasWindow());
        Assert.Equal(1, source.SnapshotCount);

        source.WindowProcessId = ChildId;
        source.Times[ChildId] = ChildTime;
        source.Processes.Add(new ProcessParent(ChildId, RootId));
        Assert.True(observer.HasWindow());
        Assert.Equal(2, source.SnapshotCount);
    }

    [Theory]
    [InlineData(true, false, false, "PlayerWindow", true)]
    [InlineData(false, false, false, "PlayerWindow", false)]
    [InlineData(true, true, false, "PlayerWindow", false)]
    [InlineData(true, false, true, "PlayerWindow", false)]
    [InlineData(true, false, false, "ConsoleWindowClass", false)]
    public void OnlyAcceptVisibleUnownedApplicationWindows(
        bool visible,
        bool owned,
        bool toolWindow,
        string className,
        bool expected)
    {
        var actual = WindowsProcessWindowSource.IsApplicationWindow(visible, owned, toolWindow, className);
        Assert.Equal(expected, actual);
    }

    private sealed class FakeSource : IProcessWindowSource
    {
        public List<ProcessParent> Processes { get; } = [];
        public Dictionary<int, long> Times { get; } = new() { [RootId] = RootTime };
        public int WindowProcessId { get; set; }
        public int FindCount { get; private set; }
        public int SnapshotCount { get; private set; }
        public System.Action AfterFind { get; set; }

        public List<ProcessParent> GetProcesses()
        {
            SnapshotCount++;
            return Processes;
        }
        public bool TryGetCreationTime(int processId, out long creationTime) =>
            Times.TryGetValue(processId, out creationTime);

        public List<int> GetApplicationWindowProcesses()
        {
            FindCount++;
            var result = WindowProcessId == 0 ? new List<int>() : new List<int> { WindowProcessId };
            AfterFind?.Invoke();
            return result;
        }
    }
}
