using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Automation;

/// <summary>
/// Serves a canvas window's drawn widgets to UI Automation as a fragment tree hosted in the window. Providers read the
/// last drawn frame's tree on UI Automation's threads; their actions are posted to the UI thread. After each frame, the UI
/// thread compares the tree with the previous frame's and raises events for what changed.
/// </summary>
/// <remarks>https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-serversideprovider</remarks>
internal sealed class WindowAutomation
{
    // When more parents than this have changed children, they are reported as one change of the window's children.
    private const int MaxSeparateStructureChanges = 3;

    private readonly Func<AutomationTree> treeSource;
    private readonly Func<float> scaleSource;
    private readonly Action invalidate;
    private readonly SynchronizationContext uiContext;
    private readonly ConcurrentDictionary<string, int> runtimeIds = new(StringComparer.Ordinal);
    private readonly List<int> changedParents = [];
    private int lastRuntimeId;
    private AutomationTree raisedTree;
    private string? raisedFocus;
    private volatile bool released;

    /// <summary>Created on the UI thread, whose synchronization context runs the providers' actions.</summary>
    public WindowAutomation(
        nint window,
        Func<AutomationTree> treeSource,
        Func<float> scaleSource,
        Action invalidate)
    {
        Window = window;
        this.treeSource = treeSource;
        this.scaleSource = scaleSource;
        this.invalidate = invalidate;
        uiContext = SynchronizationContext.Current ??
                    throw new InvalidOperationException("UI Automation must be created on the UI thread.");

        raisedTree = treeSource();
        Root = new RootProvider(this);
    }

    public nint Window { get; }
    internal RootProvider Root { get; }
    // A released window's providers find nothing, so clients holding them cannot act on the closed window.
    internal AutomationTree Tree => released ? AutomationTree.Empty : treeSource();

    /// <summary>Answers WM_GETOBJECT for the root object id with the window's root provider.</summary>
    public unsafe nint ReturnRoot(nuint wParam, nint lParam)
    {
        var provider = (nint)ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(Root);
        try
        {
            return UiAutomation.UiaReturnRawElementProvider(Window, wParam, lParam, provider);
        }
        finally
        {
            // UI Automation holds its own reference.
            Marshal.Release(provider);
        }
    }

    /// <summary>Disconnects the window's providers from UI Automation; called as the window is destroyed.</summary>
    public void Release()
    {
        released = true;
        UiAutomation.UiaReturnRawElementProvider(Window, wParam: 0, lParam: 0, provider: 0);
    }

    /// <summary>
    /// Raises UI Automation events for what changed since the previous frame: keyboard focus, toggle, expand and selection
    /// states, menus opening and closing, and changed children. Called on the UI thread after each frame is presented.
    /// </summary>
    public void RaiseChanges()
    {
        var previous = raisedTree;
        var current = Tree;
        if (released || ReferenceEquals(previous, current)) return;

        raisedTree = current;
        if (!UiAutomation.UiaClientsAreListening()) return;

        // A closed menu is no longer drawn, so the window reports that it closed. That comes before the opening of a menu that replaced it.
        var previousNodes = previous.Nodes;
        // ReSharper disable once ForCanBeConvertedToForeach - foreach over the IReadOnlyList allocates an enumerator per frame.
        for (var i = 0; i < previousNodes.Count; i++)
            if (previousNodes[i].Role == AutomationRole.Menu && current.IndexOf(previousNodes[i].Key) < 0)
                RaiseEvent(Root, UiAutomation.MenuClosedEvent);

        changedParents.Clear();
        var nodes = current.Nodes;
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var before = previous.IndexOf(node.Key);

            // A new menu is announced; any other new element is reported by its parent's changed children.
            if (before < 0)
            {
                if (node.Role == AutomationRole.Menu) RaiseEvent(new ElementProvider(this, node.Key), UiAutomation.MenuOpenedEvent);
                continue;
            }

            RaiseStateChanges(previousNodes[before], node);
            if (!HasSameChildren(previous, before, current, i)) changedParents.Add(i);
        }

        var windowChildrenChanged = !HasSameChildren(before: previous, beforeParent: -1, after: current, afterParent: -1);
        RaiseStructureChanges(current, windowChildrenChanged);
        RaiseFocusChange(current);
    }

    /// <summary>A number unique to the key within the window, kept while the window exists.</summary>
    internal int RuntimeId(string key) => runtimeIds.GetOrAdd(key, _ => Interlocked.Increment(ref lastRuntimeId));

    internal ElementProvider? Element(AutomationTree tree, int index) =>
        index < 0 ? null : new ElementProvider(this, tree.Nodes[index].Key);

    /// <summary>
    /// Runs a node's action on the UI thread, where it acts on the window's state at that time; an action that arrives
    /// after the window is destroyed is dropped.
    /// </summary>
    internal void Run(Action action)
    {
        uiContext.Post(RunUnlessReleased, state: null);

        void RunUnlessReleased(object? _)
        {
            if (released) return;

            action();
            invalidate();
        }
    }

    internal UiaRect ToScreen(RectangleF bounds)
    {
        var origin = new Win32.Point();
        Win32.ClientToScreen(Window, ref origin);
        var scale = scaleSource();
        var left = origin.X + bounds.X * scale;
        var top = origin.Y + bounds.Y * scale;
        var width = bounds.Width * scale;
        var height = bounds.Height * scale;
        return new UiaRect { Left = left, Top = top, Width = width, Height = height };
    }

    internal PointF ToClient(double x, double y)
    {
        var point = new Win32.Point { X = (int)x, Y = (int)y };
        Win32.ScreenToClient(Window, ref point);
        var scale = scaleSource();
        return new PointF(point.X / scale, point.Y / scale);
    }

    // Raises the property events of patterns whose state changed, and the selection of a newly selected item.
    private void RaiseStateChanges(AutomationNode before, AutomationNode after)
    {
        const AutomationStates PatternStates = AutomationStates.Checked | AutomationStates.Expanded | AutomationStates.Selected;
        var changed = (before.States ^ after.States) & PatternStates;
        if (changed == AutomationStates.None) return;

        var element = new ElementProvider(this, after.Key);

        if ((changed & AutomationStates.Checked) != 0 && ElementProvider.Supports(after, UiAutomation.TogglePattern))
        {
            var oldValue = Variant.FromInt32((int)ElementProvider.ToggleStateOf(before));
            var newValue = Variant.FromInt32((int)ElementProvider.ToggleStateOf(after));
            RaisePropertyChanged(element, UiAutomation.ToggleStateProperty, oldValue, newValue);
        }

        if ((changed & AutomationStates.Expanded) != 0 && ElementProvider.Supports(after, UiAutomation.ExpandCollapsePattern))
        {
            var oldValue = Variant.FromInt32((int)ElementProvider.ExpandCollapseStateOf(before));
            var newValue = Variant.FromInt32((int)ElementProvider.ExpandCollapseStateOf(after));
            RaisePropertyChanged(element, UiAutomation.ExpandCollapseStateProperty, oldValue, newValue);
        }

        if ((changed & AutomationStates.Selected) == 0 || !ElementProvider.Supports(after, UiAutomation.SelectionItemPattern)) return;

        var selected = after.Has(AutomationStates.Selected);
        var wasSelected = Variant.FromBoolean(!selected);
        var isSelected = Variant.FromBoolean(selected);
        RaisePropertyChanged(element, UiAutomation.IsSelectedProperty, wasSelected, isSelected);
        if (selected) RaiseEvent(element, UiAutomation.ElementSelectedEvent);
    }

    // Reports changed children with few events: an element inside an invalidated one needs none, and many changed parents
    // become one change of the window's children. UI Automation advises bulk events when several children of one element
    // change at once; extending that across parents is this window's choice.
    // https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-structurechangetype
    private void RaiseStructureChanges(AutomationTree tree, bool windowChildrenChanged)
    {
        if (windowChildrenChanged || changedParents.Count > MaxSeparateStructureChanges)
        {
            RaiseChildrenInvalidated(Root);
            return;
        }

        foreach (var index in changedParents)
            if (!HasChangedAncestor(index))
                RaiseChildrenInvalidated(new ElementProvider(this, tree.Nodes[index].Key));

        bool HasChangedAncestor(int node)
        {
            for (var parent = tree.Nodes[node].Parent; parent >= 0; parent = tree.Nodes[parent].Parent)
                if (changedParents.Contains(parent))
                    return true;

            return false;
        }
    }

    // Announces the element with the keyboard focus when it changes or the window regains the focus. A native text input
    // with the focus raises its own events, and an inactive window announces none.
    private void RaiseFocusChange(AutomationTree tree)
    {
        var focus = Win32.GetFocus() == Window ? FocusKey(tree) : null;
        if (focus != null && focus != raisedFocus) RaiseEvent(new ElementProvider(this, focus), UiAutomation.AutomationFocusChangedEvent);

        raisedFocus = focus;
    }

    private static bool HasSameChildren(
        AutomationTree before,
        int beforeParent,
        AutomationTree after,
        int afterParent)
    {
        var beforeChildren = before.ChildrenOf(beforeParent);
        var afterChildren = after.ChildrenOf(afterParent);
        if (beforeChildren.Length != afterChildren.Length) return false;

        for (var i = 0; i < afterChildren.Length; i++)
            if (before.Nodes[beforeChildren[i]].Key != after.Nodes[afterChildren[i]].Key)
                return false;

        return true;
    }

    private static string? FocusKey(AutomationTree tree)
    {
        var index = tree.IndexOfFocus();
        return index < 0 ? null : tree.Nodes[index].Key;
    }

    private static void RaiseEvent(IRawElementProviderSimple provider, int eventId) =>
        Raise(provider, pointer => UiAutomation.UiaRaiseAutomationEvent(pointer, eventId));

    private static void RaisePropertyChanged(
        IRawElementProviderSimple provider,
        int propertyId,
        Variant oldValue,
        Variant newValue) =>
        Raise(provider, pointer => UiAutomation.UiaRaiseAutomationPropertyChangedEvent(pointer, propertyId, oldValue, newValue));

    private static unsafe void RaiseChildrenInvalidated(IRawElementProviderSimple provider) =>
        Raise(
            provider,
            pointer => UiAutomation.UiaRaiseStructureChangedEvent(
                pointer,
                StructureChangeType.ChildrenInvalidated,
                runtimeId: null,
                runtimeIdLength: 0));

    private static unsafe void Raise(IRawElementProviderSimple provider, Func<nint, int> raise)
    {
        var pointer = (nint)ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(provider);
        try
        {
            // A client that misses an event reads the tree again, so a failed raise does not fail the frame.
            _ = raise(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }
}

/// <summary>
/// The window's own element: the window's host provider supplies its name, bounds and other window properties, and
/// this provider adds the drawn elements as its children.
/// </summary>
[GeneratedComClass]
internal sealed partial class RootProvider(WindowAutomation owner) :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IRawElementProviderFragmentRoot
{
    ProviderOptions IRawElementProviderSimple.get_ProviderOptions() => ProviderOptions.ServerSideProvider;

    nint IRawElementProviderSimple.GetPatternProvider(int patternId) => 0;

    Variant IRawElementProviderSimple.GetPropertyValue(int propertyId) => propertyId == UiAutomation.FrameworkIdProperty
        ? Variant.FromString(ElementProvider.FrameworkId)
        : Variant.Empty;

    nint IRawElementProviderSimple.get_HostRawElementProvider()
    {
        Native.ComObject.Check(UiAutomation.UiaHostProviderFromHwnd(owner.Window, out var host));
        return host;
    }

    IRawElementProviderFragment? IRawElementProviderFragment.Navigate(NavigateDirection direction)
    {
        var tree = owner.Tree;
        var children = tree.ChildrenOf(-1);
        if (children.Length == 0) return null;

        return direction switch
        {
            NavigateDirection.FirstChild => owner.Element(tree, children[0]),
            NavigateDirection.LastChild => owner.Element(tree, children[^1]),
            _ => null,
        };
    }

    // The window's host provider supplies the runtime id and bounds.
    nint IRawElementProviderFragment.GetRuntimeId() => 0;

    UiaRect IRawElementProviderFragment.get_BoundingRectangle() => default;

    nint IRawElementProviderFragment.GetEmbeddedFragmentRoots() => 0;

    void IRawElementProviderFragment.SetFocus() { }

    IRawElementProviderFragmentRoot IRawElementProviderFragment.get_FragmentRoot() => this;

    // A null element means the point is on the window itself.
    IRawElementProviderFragment? IRawElementProviderFragmentRoot.ElementProviderFromPoint(double x, double y)
    {
        var tree = owner.Tree;
        var node = tree.FindAt(owner.ToClient(x, y));
        return node == null ? null : owner.Element(tree, tree.IndexOf(node.Key));
    }

    IRawElementProviderFragment? IRawElementProviderFragmentRoot.GetFocus()
    {
        var tree = owner.Tree;
        return owner.Element(tree, tree.IndexOfFocus());
    }
}

/// <summary>
/// A drawn element, found by its key in the latest tree on every call. Calls for an element that is no longer drawn
/// fail with UIA_E_ELEMENTNOTAVAILABLE, as UI Automation expects.
/// </summary>
[GeneratedComClass]
internal sealed partial class ElementProvider(WindowAutomation owner, string key) :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IInvokeProvider,
    IToggleProvider,
    IExpandCollapseProvider,
    IValueProvider,
    ISelectionProvider,
    ISelectionItemProvider
{
    public const string FrameworkId = "Direct2D";

    private AutomationNode Node => owner.Tree.Find(key) ?? throw NotAvailable();

    ProviderOptions IRawElementProviderSimple.get_ProviderOptions() => ProviderOptions.ServerSideProvider;

    nint IRawElementProviderSimple.GetPatternProvider(int patternId)
    {
        if (owner.Tree.Find(key) is not { } node || !Supports(node, patternId)) return 0;

        return patternId switch
        {
            UiAutomation.InvokePattern => Unknown<IInvokeProvider>(this),
            UiAutomation.TogglePattern => Unknown<IToggleProvider>(this),
            UiAutomation.ExpandCollapsePattern => Unknown<IExpandCollapseProvider>(this),
            UiAutomation.ValuePattern => Unknown<IValueProvider>(this),
            UiAutomation.SelectionPattern => Unknown<ISelectionProvider>(this),
            UiAutomation.SelectionItemPattern => Unknown<ISelectionItemProvider>(this),
            _ => 0,
        };
    }

    Variant IRawElementProviderSimple.GetPropertyValue(int propertyId)
    {
        if (owner.Tree.Find(key) is not { } node) return Variant.Empty;

        var enabled = !node.Has(AutomationStates.Disabled);
        return propertyId switch
        {
            UiAutomation.ControlTypeProperty => Variant.FromInt32(ControlType(node.Role)),
            UiAutomation.NameProperty => Variant.FromString(node.Name),
            UiAutomation.AutomationIdProperty when node.AutomationId.Length != 0 =>
                Variant.FromString(node.AutomationId),
            UiAutomation.IsEnabledProperty => Variant.FromBoolean(enabled),
            UiAutomation.HasKeyboardFocusProperty => Variant.FromBoolean(node.Has(AutomationStates.Focused)),
            UiAutomation.IsKeyboardFocusableProperty =>
                Variant.FromBoolean(enabled && node.Activate != null && !node.Has(AutomationStates.Unfocusable)),
            UiAutomation.HelpTextProperty when node.HelpText != null => Variant.FromString(node.HelpText),
            UiAutomation.FrameworkIdProperty => Variant.FromString(FrameworkId),
            _ => Variant.Empty,
        };
    }

    // Drawn elements belong to the window's fragment root, not to a window of their own.
    nint IRawElementProviderSimple.get_HostRawElementProvider() => 0;

    IRawElementProviderFragment? IRawElementProviderFragment.Navigate(NavigateDirection direction)
    {
        var tree = owner.Tree;
        var index = tree.IndexOf(key);
        if (index < 0) return null;

        var parent = tree.Nodes[index].Parent;
        var children = tree.ChildrenOf(index);

        return direction switch
        {
            NavigateDirection.Parent when parent < 0 => owner.Root,
            NavigateDirection.Parent => owner.Element(tree, parent),
            NavigateDirection.NextSibling => owner.Element(tree, tree.SiblingOf(index, step: 1)),
            NavigateDirection.PreviousSibling => owner.Element(tree, tree.SiblingOf(index, step: -1)),
            NavigateDirection.FirstChild when children.Length != 0 => owner.Element(tree, children[0]),
            NavigateDirection.LastChild when children.Length != 0 => owner.Element(tree, children[^1]),
            _ => null,
        };
    }

    // UI Automation prefixes the window's runtime id.
    nint IRawElementProviderFragment.GetRuntimeId() =>
        UiAutomation.CreateInt32Array([UiAutomation.AppendRuntimeId, owner.RuntimeId(key)]);

    UiaRect IRawElementProviderFragment.get_BoundingRectangle() => owner.ToScreen(Node.Bounds);

    nint IRawElementProviderFragment.GetEmbeddedFragmentRoots() => 0;

    // Only text boxes take focus without acting; other widgets would run their action.
    void IRawElementProviderFragment.SetFocus()
    {
        var node = EnabledNode();
        if (node is { Role: AutomationRole.Edit, Activate: { } focus }) owner.Run(focus);
    }

    IRawElementProviderFragmentRoot IRawElementProviderFragment.get_FragmentRoot() => owner.Root;

    void IInvokeProvider.Invoke() => Activate(EnabledNode());

    void IToggleProvider.Toggle() => Activate(EnabledNode());

    ToggleState IToggleProvider.get_ToggleState() => ToggleStateOf(Node);

    void IExpandCollapseProvider.Expand()
    {
        var node = EnabledNode();
        if (!node.Has(AutomationStates.Expanded)) Activate(node);
    }

    void IExpandCollapseProvider.Collapse()
    {
        var node = EnabledNode();
        var collapse = node.Collapse ?? node.Activate;
        if (node.Has(AutomationStates.Expanded) && collapse != null) owner.Run(collapse);
    }

    ExpandCollapseState IExpandCollapseProvider.get_ExpandCollapseState() => ExpandCollapseStateOf(Node);

    void IValueProvider.SetValue(string value)
    {
        var setText = EnabledNode().SetText ??
                      throw new COMException("The value is read-only.", UiAutomation.InvalidOperation);

        owner.Run(() => setText(value));
    }

    string IValueProvider.get_Value() => Node.Value ?? "";

    bool IValueProvider.get_IsReadOnly() => Node.SetText == null;

    unsafe nint ISelectionProvider.GetSelection()
    {
        var tree = owner.Tree;
        var index = tree.IndexOf(key);
        if (index < 0) throw NotAvailable();

        var selected = new List<nint>();

        try
        {
            foreach (var child in tree.ChildrenOf(index))
            {
                if (!tree.Nodes[child].Has(AutomationStates.Selected)) continue;

                var element = (IRawElementProviderSimple)owner.Element(tree, child)!;
                selected.Add((nint)ComInterfaceMarshaller<IRawElementProviderSimple>.ConvertToUnmanaged(element));
            }

            return UiAutomation.CreateUnknownArray(CollectionsMarshal.AsSpan(selected));
        }
        finally
        {
            // The array holds its own references.
            foreach (var element in selected) Marshal.Release(element);
        }
    }

    bool ISelectionProvider.get_CanSelectMultiple() => false;

    bool ISelectionProvider.get_IsSelectionRequired() => true;

    void ISelectionItemProvider.Select() => Activate(EnabledNode());

    void ISelectionItemProvider.AddToSelection()
    {
        if (!Node.Has(AutomationStates.Selected))
            throw new COMException("Only one item can be selected.", UiAutomation.InvalidOperation);
    }

    void ISelectionItemProvider.RemoveFromSelection()
    {
        if (Node.Has(AutomationStates.Selected))
            throw new COMException("A selection is required.", UiAutomation.InvalidOperation);
    }

    bool ISelectionItemProvider.get_IsSelected() => Node.Has(AutomationStates.Selected);

    IRawElementProviderSimple? ISelectionItemProvider.get_SelectionContainer()
    {
        var tree = owner.Tree;
        var index = tree.IndexOf(key);
        if (index < 0) return null;

        var parent = tree.Nodes[index].Parent;
        return parent >= 0 && tree.Nodes[parent].Role == AutomationRole.ComboBox ? owner.Element(tree, parent) : null;
    }

    internal static ToggleState ToggleStateOf(AutomationNode node) =>
        node.Has(AutomationStates.Checked) ? ToggleState.On : ToggleState.Off;

    internal static ExpandCollapseState ExpandCollapseStateOf(AutomationNode node) => node.Has(AutomationStates.Expanded)
        ? ExpandCollapseState.Expanded
        : ExpandCollapseState.Collapsed;

    internal static bool Supports(AutomationNode node, int patternId)
    {
        const AutomationStates OwnPatterns = AutomationStates.Expandable | AutomationStates.Checkable;
        var role = node.Role;

        return patternId switch
        {
            UiAutomation.InvokePattern => role is AutomationRole.Button or AutomationRole.MenuItem &&
                                          node.Activate != null &&
                                          !node.Has(OwnPatterns),
            UiAutomation.TogglePattern => role == AutomationRole.CheckBox || node.Has(AutomationStates.Checkable),
            UiAutomation.ExpandCollapsePattern => role == AutomationRole.ComboBox ||
                                                  node.Has(AutomationStates.Expandable),
            UiAutomation.ValuePattern => role is AutomationRole.Edit or AutomationRole.ComboBox,
            UiAutomation.SelectionPattern => role == AutomationRole.ComboBox,
            UiAutomation.SelectionItemPattern => role == AutomationRole.RadioButton ||
                                                 role == AutomationRole.ListItem &&
                                                 node.Activate != null &&
                                                 !node.Has(AutomationStates.Checkable),
            _ => false,
        };
    }

    private static int ControlType(AutomationRole role) => role switch
    {
        AutomationRole.Text => UiAutomation.TextControl,
        AutomationRole.Button => UiAutomation.ButtonControl,
        AutomationRole.CheckBox => UiAutomation.CheckBoxControl,
        AutomationRole.RadioButton => UiAutomation.RadioButtonControl,
        AutomationRole.ComboBox => UiAutomation.ComboBoxControl,
        AutomationRole.Edit => UiAutomation.EditControl,
        AutomationRole.ListItem => UiAutomation.ListItemControl,
        AutomationRole.DataItem => UiAutomation.DataItemControl,
        AutomationRole.Menu => UiAutomation.MenuControl,
        AutomationRole.MenuItem => UiAutomation.MenuItemControl,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static unsafe nint Unknown<T>(T provider) => (nint)ComInterfaceMarshaller<T>.ConvertToUnmanaged(provider);

    private static COMException NotAvailable() =>
        new("The element is no longer drawn.", UiAutomation.ElementNotAvailable);

    private AutomationNode EnabledNode()
    {
        var node = Node;

        if (node.Has(AutomationStates.Disabled))
            throw new COMException("The element is disabled.", UiAutomation.ElementNotEnabled);

        return node;
    }

    private void Activate(AutomationNode node)
    {
        if (node.Activate is { } activate) owner.Run(activate);
    }
}
