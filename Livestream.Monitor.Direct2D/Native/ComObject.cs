using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

/// <summary>
/// An owned COM interface pointer, released on dispose. Subclasses call only the methods the renderer uses, through
/// the interface's method table; slot numbers are those of Windows.Win32.winmd, the Windows SDK metadata.
/// </summary>
internal unsafe class ComObject(void* pointer) : IDisposable
{
    private const int QueryInterfaceSlot = 0;
    private const int ReleaseSlot = 2;

    public void* Pointer { get; private set; } = pointer;

    /// <summary>The object's method table.</summary>
    protected void** Methods => *(void***)Pointer;

    /// <summary>Another interface of the same object; the caller owns the returned pointer.</summary>
    public void* QueryInterface(in Guid iid)
    {
        void* result;
        fixed (Guid* id = &iid)
            Check(((delegate* unmanaged<void*, Guid*, void**, int>)Methods[QueryInterfaceSlot])(Pointer, id, &result));

        return result;
    }

    public void Dispose()
    {
        if (Pointer is null) return;

        ((delegate* unmanaged<void*, uint>)Methods[ReleaseSlot])(Pointer);
        Pointer = null;
    }

    /// <summary>Throws for a failed HRESULT.</summary>
    public static void Check(int result)
    {
        if (result < 0) throw new COMException($"Native call failed with HRESULT 0x{result:X8}.", result);
    }

    /// <summary>The pointer of an optional object, or null.</summary>
    protected static void* PointerOf(ComObject? value) => value is null ? null : value.Pointer;
}
