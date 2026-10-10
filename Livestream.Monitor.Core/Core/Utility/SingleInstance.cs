using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Livestream.Monitor.Core.Utility;

/// <summary>One running instance of a frontend per user; a later launch asks it to show itself and exits.</summary>
public sealed class SingleInstance : IDisposable
{
    // ASFW_ANY: any process may take the foreground.
    private const int AnyProcess = -1;

    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private readonly RegisteredWaitHandle registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activation, Action activate)
    {
        this.mutex = mutex;
        this.activation = activation;
        registration = ThreadPool.RegisterWaitForSingleObject(
            waitObject: activation,
            callBack: (_, _) => activate(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// Claims the name for this user, or returns null after asking the instance holding it to show itself. The callback
    /// runs on a thread-pool thread each time a later launch asks.
    /// </summary>
    public static SingleInstance Claim(string name, Action activate)
    {
        var instanceName = name + Environment.UserName;

        // Created before the mutex, so a launch racing the first one cannot lose its signal.
        var activation = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Activate");
        var mutex = new Mutex(initiallyOwned: false, instanceName, out var first);
        if (first) return new SingleInstance(mutex, activation, activate);

        // The launch was started by the user, so it may hand the foreground to the running instance, which Windows
        // otherwise stops from activating itself.
        AllowSetForegroundWindow(AnyProcess);
        activation.Set();
        activation.Dispose();
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        registration.Unregister(null);
        activation.Dispose();
        mutex.Dispose();
    }

    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
