using System;
using System.Linq;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using Xunit;
using static Interop.UIAutomationClient.UIA_ControlTypeIds;
using static Interop.UIAutomationClient.UIA_PatternIds;
using static Interop.UIAutomationClient.UIA_PropertyIds;
using Win32 = Livestream.Monitor.Direct2D.Native.Win32;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>A frontend's main window, driven through UI Automation as a user drives it.</summary>
internal sealed class FrontendWindow : IDisposable
{
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

    private const TreeScope Descendants = TreeScope.TreeScope_Descendants;

    // One client for all windows, created once: journeys that run alongside each other fail when each creates its own at
    // the same moment.
    private static readonly IUIAutomation Automation = new CUIAutomation8();
    private static readonly IUIAutomationCondition ItemCondition =
        Automation.CreateOrCondition(ByType(UIA_ListItemControlTypeId), ByType(UIA_MenuItemControlTypeId));
    private static readonly IUIAutomationCondition RowCondition = ByType(UIA_DataItemControlTypeId);
    private static readonly IUIAutomationCondition AuthorizationPrompt =
        Automation.CreatePropertyCondition(UIA_NamePropertyId, "Twitch Authorization");

    // An action a repeat would undo, such as a toggle or the next page, waits long enough for a slow frame to show its
    // outcome; one that checks its outcome before acting again is repeated sooner.
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RepeatSafeRetryInterval = TimeSpan.FromMilliseconds(250);

    private readonly UiApplication application;

    private FrontendWindow(UiTestProfile profile) => application = UiApplication.Launch(profile);

    public static FrontendWindow Launch(UiTestProfile profile) => new(profile);

    public T WaitFor<T>(
        Func<T> probe,
        TimeSpan timeout,
        string description,
        Func<string> lastSeen = null) where T : class
    {
        string error = null;
        return UiApplication.WaitFor(TryProbe, timeout, description, Seen);

        // An element can vanish while a page changes; the next poll reads the new page.
        T TryProbe()
        {
            try
            {
                return probe();
            }
            catch (COMException exception)
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
    private void RetryUntil(Action act, Func<bool> done, string description) =>
        RetryUntil(RetryInterval, act, done, description);

    /// <summary>Invokes the control while it is shown and enabled until the outcome is seen.</summary>
    public void InvokeUntil(string automationId, Func<bool> done, string description) =>
        RetryUntil(() => InvokeIfEnabled(automationId), done, description);

    /// <summary>
    /// Invokes the control, closing any message in its way, until it raises the Twitch authorization prompt, then
    /// declines the prompt.
    /// </summary>
    public void DeclineAuthorizationAfter(string automationId, bool checkMessageRendering = false)
    {
        RetryUntil(CloseMessageOrInvoke, PromptShown, $"The Twitch authorization prompt after '{automationId}'");
        if (checkMessageRendering) WaitForPaintedDialogMessage();

        // Declining again does nothing once the prompt has closed.
        var declined = "The declined authorization prompt closed";
        RetryUntil(RepeatSafeRetryInterval, Decline, PromptClosed, declined);

        // Only a message's OK is pressed; the prompt's own affirmative button would start authorizing.
        void CloseMessageOrInvoke()
        {
            if (Find("dialog.accept") is { CurrentName: "OK" } ok) Invoke(ok);
            else if (Find(automationId) is { CurrentIsEnabled: not 0 } control) Invoke(control);
        }

        void Decline() => InvokeIfEnabled("dialog.cancel");

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
    public IUIAutomationElement Element(string automationId, TimeSpan timeout)
    {
        var condition = ById(automationId);
        return WaitFor(FindEnabled, timeout, $"Enabled '{automationId}'");

        IUIAutomationElement FindEnabled() => Find(condition) is { CurrentIsEnabled: not 0 } found ? found : null;
    }

    public IUIAutomationElement Element(string automationId) => Element(automationId, StepTimeout);

    public void WaitUntilDisabled(string automationId)
    {
        var condition = ById(automationId);
        WaitUntil(() => Find(condition) is { CurrentIsEnabled: 0 }, $"Disabled '{automationId}'");
    }

    public void SetText(string automationId, string text) =>
        Value(Element(automationId)).SetValue(text);

    /// <summary>Waits until the probe accepts the followed streams' names, in listed order.</summary>
    public void WaitForRows(Func<string[], bool> probe, string description, TimeSpan timeout)
    {
        string[] last = null;
        WaitFor(ReadRows, timeout, description, Seen);

        string[] ReadRows()
        {
            var rows = Root()?.FindAll(Descendants, RowCondition);
            last = rows == null ? null : Elements(rows).Select(DisplayedText).ToArray();
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
    public IUIAutomationElement[] OpenItems(
        string openerId,
        string containerId,
        string description,
        Func<string[], bool> ready = null)
    {
        Element(openerId);
        IUIAutomationElement[] items = [];
        var seen = "no items";
        RetryUntil(RepeatSafeRetryInterval, Open, Shown, description, Seen);
        return items;

        string Seen() => seen;

        // A button that shows a list is invoked only while the list is absent, since invoking it again rebuilds the
        // list.
        void Open()
        {
            if (Find(openerId) is not { } opener) return;

            if (opener.GetCurrentPattern(UIA_ExpandCollapsePatternId) is IUIAutomationExpandCollapsePattern expandable)
            {
                if (expandable.CurrentExpandCollapseState == ExpandCollapseState.ExpandCollapseState_Collapsed) expandable.Expand();
            }
            else if (Find(containerId) == null)
            {
                Invoke(opener);
            }
        }

        bool Shown()
        {
            var container = Find(containerId);
            items = container == null ? [] : Elements(container.FindAll(Descendants, ItemCondition));
            var labels = items.Select(DisplayedText).ToArray();
            seen = string.Join(", ", labels.Select(label => $"'{label}'"));
            return labels.Length != 0 && labels.All(label => !string.IsNullOrWhiteSpace(label)) && (ready?.Invoke(labels) ?? true);
        }
    }

    /// <summary>Chooses a list or menu item, closing a drop-down that stays open after a selection.</summary>
    public static void Choose(IUIAutomationElement item, IUIAutomationElement container)
    {
        if (item.GetCurrentPattern(UIA_SelectionItemPatternId) is not IUIAutomationSelectionItemPattern selectable)
        {
            Invoke(item);
            return;
        }

        selectable.Select();

        if (container.GetCurrentPattern(UIA_ExpandCollapsePatternId) is IUIAutomationExpandCollapsePattern
            { CurrentExpandCollapseState: ExpandCollapseState.ExpandCollapseState_Expanded } expandable)
        {
            expandable.Collapse();
        }
    }

    public static void Invoke(IUIAutomationElement element) =>
        ((IUIAutomationInvokePattern)element.GetCurrentPattern(UIA_InvokePatternId)).Invoke();

    public static IUIAutomationValuePattern Value(IUIAutomationElement element) =>
        (IUIAutomationValuePattern)element.GetCurrentPattern(UIA_ValuePatternId);

    public static IUIAutomationSelectionPattern Selection(IUIAutomationElement element) =>
        (IUIAutomationSelectionPattern)element.GetCurrentPattern(UIA_SelectionPatternId);

    public static IUIAutomationSelectionItemPattern SelectionItem(IUIAutomationElement element) =>
        (IUIAutomationSelectionItemPattern)element.GetCurrentPattern(UIA_SelectionItemPatternId);

    public static IUIAutomationTogglePattern Toggle(IUIAutomationElement element) =>
        (IUIAutomationTogglePattern)element.GetCurrentPattern(UIA_TogglePatternId);

    public static IUIAutomationExpandCollapsePattern ExpandCollapse(IUIAutomationElement element) =>
        (IUIAutomationExpandCollapsePattern)element.GetCurrentPattern(UIA_ExpandCollapsePatternId);

    public static IUIAutomationElement[] Elements(IUIAutomationElementArray array)
    {
        var elements = new IUIAutomationElement[array.Length];
        for (var i = 0; i < elements.Length; i++) elements[i] = array.GetElement(i);

        return elements;
    }

    public string DisplayedText(IUIAutomationElement element) =>
        Texts(element).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? element.CurrentName;

    public string[] Texts(IUIAutomationElement element) =>
        Elements(element.FindAll(Descendants, ByType(UIA_TextControlTypeId))).Select(part => part.CurrentName).ToArray();

    public void Dispose() => application.Dispose();

    /// <summary>The element with the automation id, or null when there is none, without waiting.</summary>
    public IUIAutomationElement Find(string automationId) => Find(ById(automationId));

    private static IUIAutomationCondition ById(string automationId) => Automation.CreatePropertyCondition(UIA_AutomationIdPropertyId, automationId);

    private static IUIAutomationCondition ByType(int type) => Automation.CreatePropertyCondition(UIA_ControlTypePropertyId, type);

    private IUIAutomationElement Find(IUIAutomationCondition condition) => Root()?.FindFirst(Descendants, condition);

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

    private void WaitForPaintedDialogMessage()
    {
        // Another journey's window opens at the same place, so this one stays above it while its pixels are read.
        const int TopmostWindow = -1;
        var topmostFlags = Win32.SwpNoSize | Win32.SwpNoMove | Win32.SwpNoActivate;
        var madeTopmost = Win32.SetWindowPos(
            window: application.MainWindow,
            insertAfter: TopmostWindow,
            x: 0,
            y: 0,
            width: 0,
            height: 0,
            flags: topmostFlags);

        Assert.True(madeTopmost);

        var lastSeen = "";
        WaitUntil(HasVisibleText, "The dialog message has visible text on screen", () => lastSeen);

        bool HasVisibleText()
        {
            var handle = application.MainWindow;
            if (handle == 0) return false;

            var edit = ScreenTextProbe.FindVisibleReadOnlyEdit(handle, "Edit");
            return ScreenTextProbe.HasPaintedText(handle, edit, out lastSeen);
        }
    }

    private void InvokeIfEnabled(string automationId)
    {
        if (Find(automationId) is { CurrentIsEnabled: not 0 } control) Invoke(control);
    }

    private static IUIAutomationCondition TextNamed(string text) =>
        Automation.CreateAndCondition(ByType(UIA_TextControlTypeId), Automation.CreatePropertyCondition(UIA_NamePropertyId, text));

    private IUIAutomationElement Root()
    {
        var window = application.MainWindow;
        return window == IntPtr.Zero ? null : Automation.ElementFromHandle(window);
    }
}
