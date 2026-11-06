using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Livestream.Monitor.Direct2D.Native;

/// <summary>Exposes the Windows notification activation callback as a local COM class.</summary>
internal sealed unsafe partial class ToastActivator : IDisposable
{
    private readonly uint cookie;

    public ToastActivator(Guid clsid, Func<string?, string?, int> activate)
    {
        var factory = new ClassFactory(new Callback(activate));
        var pointer = ComInterfaceMarshaller<IClassFactory>.ConvertToUnmanaged(factory);
        try
        {
            const uint LocalServer = 4;
            const uint MultipleUse = 1;
            ComObject.Check(CoRegisterClassObject(clsid, (nint)pointer, LocalServer, MultipleUse, out cookie));
        }
        finally
        {
            ComInterfaceMarshaller<IClassFactory>.Free(pointer);
        }
    }

    public void Dispose() => _ = CoRevokeClassObject(cookie);

    // A COM-launched secondary instance must dispatch the callback before handing off and exiting.
    public static void WaitForCallback(WaitHandle completion)
    {
        const uint DispatchCalls = 8;
        const uint TimeoutMilliseconds = 10000;
        const int CallPending = unchecked((int)0x80010115);
        var handle = completion.SafeWaitHandle.DangerousGetHandle();
        var result = CoWaitForMultipleHandles(DispatchCalls, TimeoutMilliseconds, count: 1, in handle, index: out _);
        GC.KeepAlive(completion);
        if (result != CallPending) ComObject.Check(result);
    }

    [GeneratedComInterface, Guid("00000001-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IClassFactory
    {
        [PreserveSig] int CreateInstance(nint outer, in Guid iid, out nint instance);
        [PreserveSig] int LockServer(int shouldLock);
    }

    // notificationactivationcallback.h in the Windows SDK.
    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("53e31837-6600-4a81-9395-75cffe746f94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface INotificationActivationCallback
    {
        [PreserveSig]
        int Activate(
            string? appId,
            string? arguments,
            nint input,
            uint inputCount);
    }

    [GeneratedComClass]
    private sealed partial class ClassFactory(Callback callback) : IClassFactory
    {
        public int CreateInstance(nint outer, in Guid iid, out nint instance)
        {
            instance = 0;
            const int ClassNoAggregation = unchecked((int)0x80040110);
            if (outer != 0) return ClassNoAggregation;

            var pointer = ComInterfaceMarshaller<INotificationActivationCallback>.ConvertToUnmanaged(callback);
            try
            {
                return Marshal.QueryInterface((nint)pointer, iid, out instance);
            }
            finally
            {
                ComInterfaceMarshaller<INotificationActivationCallback>.Free(pointer);
            }
        }

        public int LockServer(int shouldLock) => 0;
    }

    [GeneratedComClass]
    private sealed partial class Callback(Func<string?, string?, int> activate) : INotificationActivationCallback
    {
        public int Activate(
            string? appId,
            string? arguments,
            nint input,
            uint inputCount) => activate(appId, arguments);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoRegisterClassObject(
        in Guid clsid,
        nint factory,
        uint context,
        uint flags,
        out uint cookie);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoRevokeClassObject(uint cookie);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoWaitForMultipleHandles(
        uint flags,
        uint timeout,
        uint count,
        in nint handles,
        out uint index);
}
