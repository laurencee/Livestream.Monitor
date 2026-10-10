using System;
using System.Threading;
using System.Threading.Tasks;

namespace Livestream.Monitor.Tests.TestFramework;

/// <summary>Runs a test on a single-threaded apartment thread, which WPF collection views and dispatcher timers need.</summary>
internal static class StaThread
{
    public static Task Run(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                test().GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
