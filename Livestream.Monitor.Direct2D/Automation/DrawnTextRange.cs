using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Automation;

/// <summary>The degenerate range of a drawn Edit with an empty document.</summary>
[GeneratedComClass]
internal sealed partial class DrawnTextRange(WindowAutomation owner, string key) : ITextRangeProvider
{
    ITextRangeProvider ITextRangeProvider.Clone() => new DrawnTextRange(owner, key);

    bool ITextRangeProvider.Compare(ITextRangeProvider? range) => range is DrawnTextRange other && other.IsFor(owner, key);

    int ITextRangeProvider.CompareEndpoints(TextRangeEndpoint endpoint, ITextRangeProvider? target, TextRangeEndpoint targetEndpoint)
    {
        RequireSameDocument(target);
        return 0;
    }

    void ITextRangeProvider.ExpandToEnclosingUnit(TextUnit unit) { }

    ITextRangeProvider? ITextRangeProvider.FindAttribute(int attribute, Variant value, bool backward) => null;

    ITextRangeProvider? ITextRangeProvider.FindText(string text, bool backward, bool ignoreCase) => null;

    Variant ITextRangeProvider.GetAttributeValue(int attribute)
    {
        Native.ComObject.Check(UiAutomation.UiaGetReservedNotSupportedValue(out var value));
        return Variant.FromUnknown(value);
    }

    nint ITextRangeProvider.GetBoundingRectangles() => UiAutomation.CreateEmptyDoubleArray();

    IRawElementProviderSimple ITextRangeProvider.GetEnclosingElement()
    {
        var node = owner.Tree.Find(key);
        if (node == null || !ElementProvider.Supports(node, UiAutomation.TextPattern))
            throw new COMException("The drawn input no longer provides this range.", UiAutomation.ElementNotAvailable);

        return new ElementProvider(owner, key);
    }

    string ITextRangeProvider.GetText(int maxLength) => "";

    int ITextRangeProvider.Move(TextUnit unit, int count) => 0;

    int ITextRangeProvider.MoveEndpointByUnit(TextRangeEndpoint endpoint, TextUnit unit, int count) => 0;

    void ITextRangeProvider.MoveEndpointByRange(TextRangeEndpoint endpoint, ITextRangeProvider? target, TextRangeEndpoint targetEndpoint) =>
        RequireSameDocument(target);

    void ITextRangeProvider.Select() => throw SelectionUnavailable();

    void ITextRangeProvider.AddToSelection() => throw SelectionUnavailable();

    void ITextRangeProvider.RemoveFromSelection() => throw SelectionUnavailable();

    void ITextRangeProvider.ScrollIntoView(bool alignToTop) { }

    nint ITextRangeProvider.GetChildren() => UiAutomation.CreateUnknownArray([]);

    private bool IsFor(WindowAutomation window, string nodeKey) => owner == window && key == nodeKey;

    private void RequireSameDocument(ITextRangeProvider? target)
    {
        if (target is not DrawnTextRange other || !other.IsFor(owner, key))
            throw new ArgumentException("The range belongs to another document.", nameof(target));
    }

    private static COMException SelectionUnavailable() => new("The drawn input has no text selection.", UiAutomation.InvalidOperation);
}
