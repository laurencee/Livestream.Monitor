using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>A frontend's main window, driven through UI Automation as a user drives it.</summary>
internal sealed class FrontendWindow : IDisposable
{
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

    private static readonly Condition Items = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));

    private static readonly Condition Rows =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem);

    // WPF names its dialog element after the title; Direct2D draws the title as text.
    private static readonly Condition AuthorizationPrompt =
        new PropertyCondition(AutomationElement.NameProperty, "Twitch Authorization");

    // An action a repeat would undo, such as a toggle or the next page, waits long enough for a slow frame to show its
    // outcome; one that checks its outcome before acting again is repeated sooner.
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RepeatSafeRetryInterval = TimeSpan.FromMilliseconds(250);

    // Only its registration matters; see Root.
    private static readonly StructureChangedEventHandler IgnoreStructureChange = (_, _) => { };

    private readonly UiApplication application;

    private AutomationElement listenedWindow;

    private FrontendWindow(UiFrontend ids, UiApplication application)
    {
        Ids = ids;
        this.application = application;
    }

    public UiFrontend Ids { get; }

    public static FrontendWindow Launch(FrontendKind kind, UiTestProfile profile)
    {
        var ids = UiFrontend.Of(kind);
        var arguments = ids.Arguments(profile);
        var application = UiApplication.Launch(ids.Executable, ids.InstanceName, profile, arguments);
        return new FrontendWindow(ids, application);
    }

    public T WaitFor<T>(
        Func<T> probe,
        TimeSpan timeout,
        string description,
        Func<string> lastSeen = null) where T : class
    {
        string error = null;
        return UiApplication.WaitFor(TryProbe, timeout, description, Seen);

        // An element can vanish, or a WPF automation peer fail, while a page changes; the next poll reads the new page.
        T TryProbe()
        {
            try
            {
                return probe();
            }
            catch (Exception exception) when (exception is ElementNotAvailableException or COMException)
            {
                error = exception.Message;
                return null;
            }
        }

        string Seen() => string.Join(" ", new[] { lastSeen?.Invoke(), error }.Where(part => part != null));
    }

    /// <summary>
    /// Acts until the outcome is seen. An action taken while a dialog, flyout or page is still appearing can be lost,
    /// so it is repeated, at most once per retry interval, until the outcome shows.
    /// </summary>
    public void RetryUntil(Action act, Func<bool> done, string description) =>
        RetryUntil(RetryInterval, act, done, description);

    /// <summary>Invokes the control while it is shown and enabled until the outcome is seen.</summary>
    public void InvokeUntil(string automationId, Func<bool> done, string description) =>
        RetryUntil(() => InvokeIfEnabled(automationId), done, description);

    /// <summary>
    /// Invokes the control, closing any message in its way, until it raises the Twitch authorization prompt, then
    /// declines the prompt.
    /// </summary>
    public void DeclineAuthorizationAfter(string automationId)
    {
        RetryUntil(CloseMessageOrInvoke, PromptShown, $"The Twitch authorization prompt after '{automationId}'");

        // Declining again does nothing once the prompt has closed.
        var declined = "The declined authorization prompt closed";
        RetryUntil(RepeatSafeRetryInterval, Decline, PromptClosed, declined);

        // Only a message's OK is pressed; the prompt's own affirmative button would start authorizing.
        void CloseMessageOrInvoke()
        {
            if (Find(Ids.AcceptMessage) is { Current.Name: "OK" } ok) Invoke(ok);
            else if (Find(automationId) is { Current.IsEnabled: true } control) Invoke(control);
        }

        void Decline() => InvokeIfEnabled(Ids.DeclinePrompt);

        bool PromptShown() => Find(AuthorizationPrompt) != null;

        bool PromptClosed() => !PromptShown();
    }

    public bool ShowsText(string text) => Find(TextNamed(text)) != null;

    private void WaitUntil(Func<bool> condition, string description, Func<string> lastSeen = null)
    {
        WaitFor(Met, StepTimeout, description, lastSeen);

        FrontendWindow Met() => condition() ? this : null;
    }

    /// <summary>Waits for an enabled element with the automation id.</summary>
    public AutomationElement Element(string automationId, TimeSpan timeout) =>
        Element(ById(automationId), timeout, automationId);

    public AutomationElement Element(string automationId) => Element(automationId, StepTimeout);

    public AutomationElement Element(Condition condition, TimeSpan timeout, string description)
    {
        return WaitFor(FindEnabled, timeout, $"Enabled '{description}'");

        AutomationElement FindEnabled() => Find(condition) is { Current.IsEnabled: true } found ? found : null;
    }

    public void WaitUntilDisabled(string automationId)
    {
        var condition = ById(automationId);
        WaitUntil(() => Find(condition) is { Current.IsEnabled: false }, $"Disabled '{automationId}'");
    }

    public void SetText(string automationId, string text) =>
        Pattern<ValuePattern>(Element(automationId), ValuePattern.Pattern).SetValue(text);

    /// <summary>Waits until the probe accepts the followed streams' names, in listed order.</summary>
    public void WaitForRows(Func<string[], bool> probe, string description, TimeSpan timeout)
    {
        string[] last = null;
        WaitFor(ReadRows, timeout, description, Seen);

        string[] ReadRows()
        {
            var rows = Root()?.FindAll(TreeScope.Descendants, Rows).Cast<AutomationElement>();
            last = rows?.Select(DisplayedText).ToArray();
            return last != null && probe(last) ? last : null;
        }

        string Seen() => last == null ? "no list" : string.Join(", ", last);
    }

    public void WaitForRows(Func<string[], bool> probe, string description) =>
        WaitForRows(probe, description, StepTimeout);

    /// <summary>
    /// Opens a list, by expanding it or by invoking the button that shows it, and returns the list or menu items within
    /// the container once each shows text. A list that closes before then, such as under a message, is opened again.
    /// </summary>
    public AutomationElement[] OpenItems(string openerId, string containerId, string description)
    {
        Element(openerId);
        AutomationElement[] items = [];
        var seen = "no items";
        RetryUntil(RepeatSafeRetryInterval, Open, Shown, description, Seen);
        return items;

        string Seen() => seen;

        // A button that shows a list is invoked only while the list is absent, since invoking it again rebuilds the
        // list.
        void Open()
        {
            if (Find(openerId) is not { } opener) return;

            if (opener.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern))
            {
                var expandable = (ExpandCollapsePattern)pattern;
                if (expandable.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) expandable.Expand();
            }
            else if (Find(containerId) == null)
            {
                Invoke(opener);
            }
        }

        bool Shown()
        {
            var container = Find(containerId);
            items = container?.FindAll(TreeScope.Descendants, Items).Cast<AutomationElement>().ToArray() ?? [];
            seen = string.Join(", ", items.Select(item => $"'{DisplayedText(item)}'"));
            return items.Length != 0 && items.All(item => !string.IsNullOrWhiteSpace(DisplayedText(item)));
        }
    }

    /// <summary>Chooses a list or menu item, closing a drop-down that stays open after a selection.</summary>
    public static void Choose(AutomationElement item, AutomationElement container)
    {
        if (!item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectable))
        {
            Invoke(item);
            return;
        }

        ((SelectionItemPattern)selectable).Select();

        if (container.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandable) &&
            ((ExpandCollapsePattern)expandable).Current.ExpandCollapseState == ExpandCollapseState.Expanded)
        {
            ((ExpandCollapsePattern)expandable).Collapse();
        }
    }

    public static void Invoke(AutomationElement element) =>
        Pattern<InvokePattern>(element, InvokePattern.Pattern).Invoke();

    public static T Pattern<T>(AutomationElement element, AutomationPattern pattern) where T : BasePattern =>
        (T)element.GetCurrentPattern(pattern);

    /// <summary>
    /// The first text an element shows, or its name when it shows none. A WPF item bound to an object is named after
    /// the object's type, not what it displays.
    /// </summary>
    public static string DisplayedText(AutomationElement element) =>
        Texts(element).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? element.Current.Name;

    /// <summary>The names of the text elements within the element.</summary>
    public static string[] Texts(AutomationElement element)
    {
        var text = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
        var parts = element.FindAll(TreeScope.Descendants, text).Cast<AutomationElement>();
        return parts.Select(part => part.Current.Name).ToArray();
    }

    public void Dispose()
    {
        // Removed before the application is closed; it may already have exited, leaving nothing to remove. The
        // application is closed even if removal fails, so a hung frontend does not block later UI tests.
        try
        {
            if (listenedWindow != null) Automation.RemoveStructureChangedEventHandler(listenedWindow, IgnoreStructureChange);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or COMException)
        {
        }
        finally
        {
            application.Dispose();
        }
    }

    /// <summary>The element with the automation id, or null when there is none, without waiting.</summary>
    public AutomationElement Find(string automationId) => Find(ById(automationId));

    private static PropertyCondition ById(string automationId) =>
        new(AutomationElement.AutomationIdProperty, automationId);

    private AutomationElement Find(Condition condition) => Root()?.FindFirst(TreeScope.Descendants, condition);

    private void RetryUntil(
        TimeSpan retryInterval,
        Action act,
        Func<bool> done,
        string description,
        Func<string> lastSeen = null)
    {
        var lastAct = DateTime.MinValue;
        WaitUntil(ActUnlessDone, description, lastSeen);

        bool ActUnlessDone()
        {
            if (done()) return true;
            if (DateTime.UtcNow - lastAct < retryInterval) return false;

            act();
            lastAct = DateTime.UtcNow;
            return false;
        }
    }

    private void InvokeIfEnabled(string automationId)
    {
        if (Find(automationId) is { Current.IsEnabled: true } control) Invoke(control);
    }

    private static AndCondition TextNamed(string text) => new(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
        new PropertyCondition(AutomationElement.NameProperty, text));

    private AutomationElement Root()
    {
        var window = application.MainWindow;
        if (window == IntPtr.Zero) return null;

        var root = AutomationElement.FromHandle(window);

        // WPF refreshes the cached children of reused item peers, such as a list row shown again after filtering, only
        // while a UI Automation client listens for structure or property changes, as assistive technology does.
        // Without a listener such a row exposes the detached text of its previous cells.
        if (listenedWindow == null)
        {
            Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, IgnoreStructureChange);
            listenedWindow = root;
        }

        return root;
    }
}
