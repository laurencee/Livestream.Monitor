using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Livestream.Monitor.Direct2D.Native;

// UI Automation provider interfaces from UIAutomationCore.h, declared in vtable order so source-generated COM can
// expose managed providers without the WPF runtime. Interface-typed results are marshalled by the generator; host
// providers, pattern providers and SAFEARRAYs are returned as raw pointers.
// https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-providerinterfaces

[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    ProviderOptions get_ProviderOptions();

    /// <summary>The provider of the pattern as an owned IUnknown pointer, or zero when it is not supported.</summary>
    nint GetPatternProvider(int patternId);

    Variant GetPropertyValue(int propertyId);

    /// <summary>An owned IRawElementProviderSimple pointer for an element hosted in a window, otherwise zero.</summary>
    nint get_HostRawElementProvider();
}

[GeneratedComInterface]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragment
{
    IRawElementProviderFragment? Navigate(NavigateDirection direction);

    /// <summary>An owned SAFEARRAY of VT_I4, or zero for a fragment root hosted in a window.</summary>
    nint GetRuntimeId();

    UiaRect get_BoundingRectangle();

    /// <summary>An owned SAFEARRAY of embedded fragment roots, or zero for none.</summary>
    nint GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot get_FragmentRoot();
}

[GeneratedComInterface]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawElementProviderFragmentRoot
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    IRawElementProviderFragment? GetFocus();
}

[GeneratedComInterface]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
internal partial interface IInvokeProvider
{
    void Invoke();
}

[GeneratedComInterface]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
internal partial interface IExpandCollapseProvider
{
    void Expand();

    void Collapse();

    ExpandCollapseState get_ExpandCollapseState();
}

[GeneratedComInterface]
[Guid("fb8b03af-3bdf-48d4-bd36-1a65793be168")]
internal partial interface ISelectionProvider
{
    /// <summary>An owned SAFEARRAY of VT_UNKNOWN selected element providers.</summary>
    nint GetSelection();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool get_CanSelectMultiple();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool get_IsSelectionRequired();
}

[GeneratedComInterface]
[Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
internal partial interface ISelectionItemProvider
{
    void Select();

    void AddToSelection();

    void RemoveFromSelection();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool get_IsSelected();

    IRawElementProviderSimple? get_SelectionContainer();
}

[GeneratedComInterface]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
internal partial interface IToggleProvider
{
    void Toggle();

    ToggleState get_ToggleState();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IValueProvider
{
    void SetValue(string value);

    [return: MarshalAs(UnmanagedType.BStr)]
    string get_Value();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool get_IsReadOnly();
}

[Flags]
internal enum ProviderOptions
{
    ServerSideProvider = 0x1,
}

internal enum NavigateDirection
{
    Parent = 0,
    NextSibling = 1,
    PreviousSibling = 2,
    FirstChild = 3,
    LastChild = 4,
}

internal enum ExpandCollapseState
{
    Collapsed = 0,
    Expanded = 1,
    LeafNode = 3,
}

internal enum ToggleState
{
    Off = 0,
    On = 1,
}

/// <summary>
/// The VARIANT layout (24 bytes on 64-bit Windows) holding the integer, boolean or BSTR property values a provider
/// returns; the caller frees a BSTR with VariantClear. Built-in ComVariant marshalling would need runtime marshalling
/// disabled for the whole assembly.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct Variant
{
    private const ushort Int32Type = 3;
    private const ushort StringType = 8;
    private const ushort BooleanType = 11;

    // VARIANT_TRUE: all bits set in the 16-bit boolean.
    private const short VariantTrue = -1;

    private readonly ushort type;
    private readonly ushort reserved1;
    private readonly ushort reserved2;
    private readonly ushort reserved3;
    private readonly nint value;
    private readonly nint record;

    private Variant(ushort type, nint value)
    {
        this.type = type;
        this.value = value;
    }

    public static Variant Empty => default;

    public static Variant FromInt32(int number) => new(Int32Type, number);

    public static Variant FromBoolean(bool flag) => new(BooleanType, flag ? VariantTrue : 0);

    public static Variant FromString(string text) => new(StringType, Marshal.StringToBSTR(text));
}

/// <summary>A UiaRect: screen coordinates in device pixels.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

internal static partial class UiAutomation
{
    // UiaRootObjectId: the WM_GETOBJECT object id with which UI Automation asks a window for its root provider.
    public const int RootObjectId = -25;

    // UiaAppendRuntimeId: the first runtime id element, telling UI Automation to prefix the host window's id.
    public const int AppendRuntimeId = 3;

    public const int ElementNotEnabled = unchecked((int)0x80040200);
    public const int ElementNotAvailable = unchecked((int)0x80040201);
    public const int InvalidOperation = unchecked((int)0x80131509);

    public const int InvokePattern = 10000;
    public const int SelectionPattern = 10001;
    public const int ValuePattern = 10002;
    public const int ExpandCollapsePattern = 10005;
    public const int SelectionItemPattern = 10010;
    public const int TogglePattern = 10015;

    public const int ControlTypeProperty = 30003;
    public const int NameProperty = 30005;
    public const int HasKeyboardFocusProperty = 30008;
    public const int IsKeyboardFocusableProperty = 30009;
    public const int IsEnabledProperty = 30010;
    public const int AutomationIdProperty = 30011;
    public const int HelpTextProperty = 30013;
    public const int FrameworkIdProperty = 30024;

    public const int ButtonControl = 50000;
    public const int CheckBoxControl = 50002;
    public const int ComboBoxControl = 50003;
    public const int EditControl = 50004;
    public const int ListItemControl = 50007;
    public const int MenuControl = 50009;
    public const int MenuItemControl = 50011;
    public const int RadioButtonControl = 50013;
    public const int TextControl = 50020;
    public const int DataItemControl = 50029;

    private const ushort VariantInt32 = 3;
    private const ushort VariantUnknown = 13;

    /// <summary>Answers WM_GETOBJECT with the window's root provider; a zero provider releases the window's.</summary>
    [LibraryImport("UIAutomationCore.dll")]
    public static partial nint UiaReturnRawElementProvider(
        nint window,
        nuint wParam,
        nint lParam,
        nint provider);

    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaHostProviderFromHwnd(nint window, out nint provider);

    /// <summary>A SAFEARRAY of 32-bit integers, owned by the caller.</summary>
    public static unsafe nint CreateInt32Array(ReadOnlySpan<int> values)
    {
        var array = SafeArrayCreateVector(VariantInt32, lowerBound: 0, (uint)values.Length);

        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            ComObject.Check(SafeArrayPutElement(array, i, &value));
        }

        return array;
    }

    /// <summary>A SAFEARRAY of IUnknown pointers, owned by the caller; the array adds its own references.</summary>
    public static unsafe nint CreateUnknownArray(ReadOnlySpan<nint> unknowns)
    {
        var array = SafeArrayCreateVector(VariantUnknown, lowerBound: 0, (uint)unknowns.Length);
        for (var i = 0; i < unknowns.Length; i++) ComObject.Check(SafeArrayPutElement(array, i, (void*)unknowns[i]));

        return array;
    }

    [LibraryImport("oleaut32.dll")]
    private static partial nint SafeArrayCreateVector(ushort variantType, int lowerBound, uint count);

    [LibraryImport("oleaut32.dll")]
    private static unsafe partial int SafeArrayPutElement(nint array, in int index, void* value);
}
