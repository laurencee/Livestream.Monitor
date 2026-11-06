using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

/// <summary>Windows Runtime toast and Shell shortcut bindings from the Windows SDK headers.</summary>
internal static unsafe class WindowsToast
{
    private const int FirstInspectableMethod = 6;
    private static readonly Guid DocumentId = new("f7f3a506-1e87-42d6-bcfb-b8c809fa5494");
    private static readonly Guid DocumentIoId = new("6cd0e74e-ee65-4489-9ebf-ca43e87ba637");
    private static readonly Guid ToastFactoryId = new("04124b20-82c6-4229-b109-fd9ed4662b53");
    private static readonly Guid ToastManagerId = new("50ac103f-d235-4598-bbef-98fe4d1a3ad4");
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-c000-000000000046");
    private static readonly Guid ShellLinkId = new("000214f9-0000-0000-c000-000000000046");
    private static readonly Guid PropertyStoreId = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    private static readonly Guid PersistFileId = new("0000010b-0000-0000-c000-000000000046");
    private static readonly Guid AppUserModelFormat = new("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3");

    public static void SetProcessIdentity(string appId) => ComObject.Check(SetCurrentProcessExplicitAppUserModelID(appId));

    public static void Show(string appId, string xml)
    {
        using var documentClass = new HString("Windows.Data.Xml.Dom.XmlDocument");
        ComObject.Check(RoActivateInstance(documentClass.Handle, out var instance));

        using var inspectable = new ComObject(instance);
        using var document = new ComObject(inspectable.QueryInterface(DocumentId));
        using var documentIo = new ComObject(inspectable.QueryInterface(DocumentIoId));
        using var content = new HString(xml);
        var load = (delegate* unmanaged<void*, nint, int>)Method(documentIo, FirstInspectableMethod);
        ComObject.Check(load(documentIo.Pointer, content.Handle));

        using var factory = GetFactory("Windows.UI.Notifications.ToastNotification", ToastFactoryId);
        var create = (delegate* unmanaged<void*, void*, void**, int>)Method(factory, FirstInspectableMethod);
        void* toastPointer;
        ComObject.Check(create(factory.Pointer, document.Pointer, &toastPointer));

        using var toast = new ComObject(toastPointer);
        using var manager = GetFactory("Windows.UI.Notifications.ToastNotificationManager", ToastManagerId);
        using var id = new HString(appId);
        const int CreateNotifierWithId = FirstInspectableMethod + 1;
        var createNotifier = (delegate* unmanaged<void*, nint, void**, int>)Method(manager, CreateNotifierWithId);
        void* notifierPointer;
        ComObject.Check(createNotifier(manager.Pointer, id.Handle, &notifierPointer));

        using var notifier = new ComObject(notifierPointer);
        const int GetSetting = FirstInspectableMethod + 2;
        var getSetting = (delegate* unmanaged<void*, int*, int>)Method(notifier, GetSetting);
        int setting;
        ComObject.Check(getSetting(notifier.Pointer, &setting));
        const int Enabled = 0;
        if (setting != Enabled) return;

        var show = (delegate* unmanaged<void*, void*, int>)Method(notifier, FirstInspectableMethod);
        ComObject.Check(show(notifier.Pointer, toast.Pointer));
    }

    // https://learn.microsoft.com/windows/desktop/win32_tile_badge_notif/respond-to-toast-activations
    public static void SaveShortcut(
        string path,
        string executable,
        string appId,
        Guid activator)
    {
        const uint InProcessServer = 1;
        ComObject.Check(CoCreateInstance(ShellLinkClass, outer: 0, InProcessServer, ShellLinkId, out var pointer));

        using var link = new ComObject(pointer);
        using var properties = new ComObject(link.QueryInterface(PropertyStoreId));
        using var file = new ComObject(link.QueryInterface(PersistFileId));
        const int SetPath = 20;
        var setPath = (delegate* unmanaged<void*, char*, int>)Method(link, SetPath);
        fixed (char* value = executable) ComObject.Check(setPath(link.Pointer, value));

        const uint AppIdProperty = 5;
        const uint ActivatorProperty = 26;
        const ushort VtLpwstr = 31;
        const ushort VtClsid = 72;
        fixed (char* value = appId) SetProperty(properties, AppIdProperty, VtLpwstr, value);
        SetProperty(properties, ActivatorProperty, VtClsid, &activator);
        const int Commit = 7;
        var commit = (delegate* unmanaged<void*, int>)Method(properties, Commit);
        ComObject.Check(commit(properties.Pointer));

        const int Save = 6;
        var save = (delegate* unmanaged<void*, char*, int, int>)Method(file, Save);
        fixed (char* value = path)
        {
            ComObject.Check(save(file.Pointer, value, 1));

            const uint ItemUpdated = 0x2000;
            const uint UnicodePath = 5;
            SHChangeNotify(ItemUpdated, UnicodePath, value, second: null);
        }
    }

    private static void SetProperty(
        ComObject properties,
        uint propertyId,
        ushort type,
        void* value)
    {
        var key = new PropertyKey { Format = AppUserModelFormat, Id = propertyId };
        var variant = new PropVariant { Type = type, Value = value };
        const int SetValue = 6;
        var set = (delegate* unmanaged<void*, PropertyKey*, PropVariant*, int>)Method(properties, SetValue);
        ComObject.Check(set(properties.Pointer, &key, &variant));
    }

    private static ComObject GetFactory(string className, in Guid id)
    {
        using var name = new HString(className);
        ComObject.Check(RoGetActivationFactory(name.Handle, id, out var pointer));
        return new ComObject(pointer);
    }

    private static void* Method(ComObject instance, int slot) => (*(void***)instance.Pointer)[slot];

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public void* Value;
    }

    private readonly struct HString : IDisposable
    {
        public HString(string value) => ComObject.Check(WindowsCreateString(value, (uint)value.Length, out Handle));

        public readonly nint Handle;

        public void Dispose() => _ = WindowsDeleteString(Handle);
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern int RoInitialize(uint mode);

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern void RoUninitialize();

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoActivateInstance(nint className, out void* instance);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint className, in Guid id, out void* factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string value, uint length, out nint handle);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint handle);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        in Guid clsid,
        nint outer,
        uint context,
        in Guid iid,
        out void* instance);

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        void* first,
        void* second);
}
