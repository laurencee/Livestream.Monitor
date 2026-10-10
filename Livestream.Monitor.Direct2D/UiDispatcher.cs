using System.Collections.Concurrent;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D;

/// <summary>
/// The UI thread's synchronization context: continuations of awaits started on the UI thread, and work posted from
/// other threads, run on the UI thread between message dispatch and drawing.
/// </summary>
internal sealed class UiDispatcher : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> work = new();
    private readonly uint threadId = Win32.GetCurrentThreadId();

    public override void Post(SendOrPostCallback d, object? state)
    {
        work.Enqueue((d, state));

        // Wakes the message wait so posted work does not wait for the next tick.
        Win32.PostThreadMessage(threadId, Win32.WmApp, wParam: 0, lParam: 0);
    }

    public override SynchronizationContext CreateCopy() => this;

    public void Post(Action action) => Post(static state => ((Action)state!)(), action);

    /// <summary>
    /// Runs the work queued so far; work queued while running waits for the next drain.
    /// </summary>
    public void Drain()
    {
        int count = work.Count;
        for (int i = count; i > 0 && work.TryDequeue(out var item); i--) item.Callback(item.State);
    }
}
