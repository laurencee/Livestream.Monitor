using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Livestream.Monitor.Core.Utility;

internal interface IProcessWindowSource
{
    List<ProcessParent> GetProcesses();
    bool TryGetCreationTime(int processId, out long creationTime);
    List<int> GetApplicationWindowProcesses();
}

internal readonly struct ProcessParent(int processId, int parentId)
{
    public int ProcessId { get; } = processId;
    public int ParentId { get; } = parentId;
}

internal sealed class ProcessWindowObserver(IProcessWindowSource source, int rootId, long rootCreationTime)
{
    private const int PollDelayMilliseconds = 50;
    private const int AncestryRetryMilliseconds = 1000;
    private readonly Dictionary<int, long> tracked = new() { [rootId] = rootCreationTime };
    private readonly Dictionary<int, long> candidates = [];
    private readonly Dictionary<int, (long CreationTime, long NextCheck)> deferred = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();

    public async Task<bool> WaitForWindow(CancellationToken cancellationToken, Func<bool> shouldStop)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !shouldStop())
            {
                if (HasWindow()) return !cancellationToken.IsCancellationRequested && !shouldStop();

                await Task.Delay(PollDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return false;
    }

    public bool HasWindow()
    {
        if (!source.TryGetCreationTime(rootId, out var rootTime) || rootTime != rootCreationTime) return false;

        // Toolhelp snapshots are expensive. Only discover ancestry when a new process has a usable window.
        candidates.Clear();
        foreach (var processId in source.GetApplicationWindowProcesses())
        {
            if (!source.TryGetCreationTime(processId, out var time) || time < rootCreationTime) continue;
            if (deferred.TryGetValue(processId, out var entry) && entry.CreationTime == time &&
                clock.ElapsedMilliseconds < entry.NextCheck)
            {
                continue;
            }

            candidates[processId] = time;
        }

        if (candidates.Count == 0) return false;
        if (candidates.TryGetValue(rootId, out var windowRootTime) && windowRootTime == rootCreationTime)
        {
            return source.TryGetCreationTime(rootId, out rootTime) && rootTime == rootCreationTime;
        }

        foreach (var processId in tracked.Keys.ToArray())
        {
            if (!source.TryGetCreationTime(processId, out var time) || time != tracked[processId])
                tracked.Remove(processId);
        }

        if (!tracked.ContainsKey(rootId)) return false;

        var processes = source.GetProcesses();
        bool added;
        do
        {
            added = false;
            foreach (var process in processes)
            {
                if (tracked.ContainsKey(process.ProcessId)) continue;
                if (!tracked.TryGetValue(process.ParentId, out var parentTime)) continue;
                if (!source.TryGetCreationTime(process.ProcessId, out var childTime)) continue;
                if (childTime < parentTime) continue;

                // Recheck the parent: its PID may have been reused since the snapshot was captured.
                if (!source.TryGetCreationTime(process.ParentId, out var currentParentTime) ||
                    currentParentTime != parentTime)
                {
                    continue;
                }

                tracked.Add(process.ProcessId, childTime);
                added = true;
            }
        } while (added);

        foreach (var candidate in candidates)
        {
            if (tracked.TryGetValue(candidate.Key, out var expectedTime) && expectedTime == candidate.Value)
            {
                // Window enumeration can race both process exit and PID reuse.
                if (source.TryGetCreationTime(candidate.Key, out var windowTime) && windowTime == expectedTime &&
                    source.TryGetCreationTime(rootId, out rootTime) && rootTime == rootCreationTime)
                {
                    return true;
                }
            }
            else
            {
                // Proven unrelated identities never need retrying; unknown ancestry is checked at most once per second.
                var nextCheck = HasUnrelatedAncestor(candidate.Key, candidate.Value, processes)
                    ? long.MaxValue
                    : clock.ElapsedMilliseconds + AncestryRetryMilliseconds;
                deferred[candidate.Key] = (candidate.Value, nextCheck);
            }
        }

        return false;
    }

    private bool HasUnrelatedAncestor(int processId, long creationTime, List<ProcessParent> processes)
    {
        var visited = new HashSet<int>();
        while (visited.Add(processId))
        {
            var found = false;
            foreach (var process in processes)
            {
                if (process.ProcessId != processId) continue;
                if (process.ParentId == 0) return true;
                if (process.ParentId == rootId) return false;
                if (!source.TryGetCreationTime(process.ParentId, out var parentTime)) return false;
                if (parentTime > creationTime) return false;
                if (parentTime < rootCreationTime) return true;

                processId = process.ParentId;
                creationTime = parentTime;
                found = true;
                break;
            }

            if (!found) return false;
        }

        return false;
    }
}
