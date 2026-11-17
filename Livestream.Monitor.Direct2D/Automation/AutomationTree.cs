using System.Drawing;
using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Automation;

/// <summary>The UI Automation control types drawn widgets and content map to.</summary>
internal enum AutomationRole
{
    Text,
    Button,
    CheckBox,
    RadioButton,
    ComboBox,
    Edit,
    ListItem,
    List,
    DataItem,
    Menu,
    MenuItem,
}

[Flags]
internal enum AutomationStates
{
    None = 0,
    Disabled = 1,
    Checked = 2,
    Expanded = 4,
    Selected = 8,
    Focused = 16,

    /// <summary>
    /// An element that switches on and off, such as a tickable menu item, a toggle button or a followable tile; its
    /// <see cref="Checked"/> state is then exposed as a toggle.
    /// </summary>
    Checkable = 32,

    /// <summary>A button that opens and closes content, such as a settings section.</summary>
    Expandable = 64,

    /// <summary>An element that acts when invoked but cannot take the keyboard focus, such as a caption button.</summary>
    Unfocusable = 128,
}

/// <summary>
/// One element of a window's automation tree as last drawn, in logical client coordinates. Its actions run on the UI
/// thread and act on whatever is drawn with the node's id at that time, which may be newer content with the same id,
/// such as the next dialog's buttons.
/// </summary>
/// <param name="Key">
/// Unique within the window; it forms the runtime id. List rows and tiles are keyed by the item they show; menu items are
/// keyed by position, so a menu item's key can name another item in the next menu.
/// </param>
/// <param name="Parent">The parent node's index, or -1 for a top-level element.</param>
/// <param name="Value">A text box's text or a combo box's choice; for a list, the key of its selected item, drawn or not.</param>
/// <param name="Activate">Clicks, toggles, selects or opens the element, or null when it has no action.</param>
/// <param name="Collapse">Closes an expanded element, or null when activating it again closes it.</param>
/// <param name="Item">A list row's or tile's place in its list, status and actions, or null for other elements.</param>
internal sealed record AutomationNode(
    string Key,
    string AutomationId,
    AutomationRole Role,
    string Name,
    RectangleF Bounds,
    int Parent,
    AutomationStates States,
    string? HelpText = null,
    string? Value = null,
    Action? Activate = null,
    Action<string>? SetText = null,
    Action? Collapse = null,
    AutomationItem? Item = null)
{
    public bool Has(AutomationStates state) => (States & state) != 0;
}

/// <summary>What UI Automation asks of a list row or tile.</summary>
internal enum ItemRequest
{
    Select,
    Deselect,
    ScrollIntoView,

    /// <summary>Selects the item, brings it into view and gives its list the keyboard focus.</summary>
    Focus,
}

/// <summary>A list row's or tile's place in its list, its status and the requests UI Automation makes of it.</summary>
/// <param name="Position">The 1-based position in the whole list, including items not drawn.</param>
/// <param name="Count">The number of items in the whole list.</param>
/// <param name="Status">What UI Automation reads after the item's name, such as a stream's live state.</param>
/// <param name="Act">Runs on the UI thread and finds the item again, since a refresh may have moved it.</param>
internal sealed record AutomationItem(
    int Position,
    int Count,
    string Status,
    Action<ItemRequest> Act);

/// <summary>
/// An immutable snapshot of a window's automation tree. UI Automation reads it from its own threads while the UI
/// thread publishes the next one.
/// </summary>
internal sealed class AutomationTree
{
    public static readonly AutomationTree Empty = new([]);
    private readonly AutomationNode[] nodes;
    private readonly Dictionary<string, int> indices = [];
    private readonly List<int>?[] children;
    private readonly List<int> topLevel = [];

    public AutomationTree(AutomationNode[] nodes)
    {
        this.nodes = nodes;
        children = new List<int>[nodes.Length];

        for (var i = 0; i < nodes.Length; i++)
        {
            indices.TryAdd(nodes[i].Key, i);
            var parent = nodes[i].Parent;

            if (parent < 0) topLevel.Add(i);
            else (children[parent] ??= []).Add(i);
        }
    }

    public IReadOnlyList<AutomationNode> Nodes => nodes;

    public AutomationNode? Find(string key) => indices.TryGetValue(key, out var index) ? nodes[index] : null;

    /// <summary>The indices of the children of the node at the index, or of the top-level nodes for -1.</summary>
    public ReadOnlySpan<int> ChildrenOf(int parent) =>
        CollectionsMarshal.AsSpan(parent < 0 ? topLevel : children[parent]);

    public int IndexOf(string key) => indices.GetValueOrDefault(key, -1);

    /// <summary>The index of the node with keyboard focus, or -1 when none has it.</summary>
    public int IndexOfFocus() => Array.FindIndex(nodes, node => node.Has(AutomationStates.Focused));

    /// <summary>The index of the next (step 1) or previous (step -1) sibling, or -1 when there is none.</summary>
    public int SiblingOf(int index, int step)
    {
        var siblings = ChildrenOf(nodes[index].Parent);

        for (var i = 0; i < siblings.Length; i++)
        {
            if (siblings[i] != index) continue;

            var sibling = i + step;
            return sibling >= 0 && sibling < siblings.Length ? siblings[sibling] : -1;
        }

        return -1;
    }

    /// <summary>The innermost, topmost node containing the point, or null when none does.</summary>
    public AutomationNode? FindAt(PointF point)
    {
        // Later nodes are drawn above earlier ones, and children follow their parent.
        for (var i = nodes.Length - 1; i >= 0; i--)
            if (nodes[i].Bounds.Contains(point))
                return nodes[i];

        return null;
    }
}
