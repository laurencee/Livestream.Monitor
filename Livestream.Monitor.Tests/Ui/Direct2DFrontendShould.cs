using System;
using System.Linq;
using Interop.UIAutomationClient;
using Xunit;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>
/// The end-to-end user journey through the main window, driven through UI Automation. It runs as most users do, with a
/// Twitch token and followed streams, and covers in one launch what updates most often break: loading saved data, list
/// bindings and drawing, enabled states, provider queries, paging and data shared between pages.
/// </summary>
[Trait("Category", "UI")]
[Collection(UiApplicationCollection.Name)]
public class Direct2DFrontendShould
{
    [Fact]
    public void ManageFollowedStreamsAndBrowseTopStreamsAndVods()
    {
        using var profile = UiTestProfile.Create();
        using var window = FrontendWindow.Launch(profile);
        RunMainJourney(window);
    }

    /// <summary>The main journey through a launched frontend.</summary>
    private static void RunMainJourney(FrontendWindow window)
    {
        var followed = UiTestProfile.FollowedChannels;
        var ascending = followed.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();

        const string SavedStreams = "The saved followed streams";
        window.WaitForRows(names => SameNames(names, followed), SavedStreams, FrontendWindow.StartupTimeout);

        // Filtering narrows the list, and clearing the filter restores it.
        window.SetText("filter", followed[0]);
        window.WaitForRows(names => names.SequenceEqual([followed[0]]), $"Only {followed[0]}");
        window.SetText("filter", "");
        window.WaitForRows(names => SameNames(names, followed), "Every followed stream after clearing the filter");

        // The first sort by name is ascending and the next descending.
        var nameHeader = window.Element("column.1");
        FrontendWindow.Invoke(nameHeader);
        window.WaitForRows(names => names.SequenceEqual(ascending), "Streams sorted by ascending name");
        FrontendWindow.Invoke(nameHeader);
        var descending = followed.OrderDescending(StringComparer.CurrentCultureIgnoreCase).ToArray();
        window.WaitForRows(names => names.SequenceEqual(descending), "Streams sorted by descending name");

        // A channel can be added only once a name is entered.
        window.WaitUntilDisabled("add");
        window.SetText("channel", "a channel");
        window.Element("add");
        window.SetText("channel", "");
        window.WaitUntilDisabled("add");

        // Top Streams offers the providers with one selected, and its next page shows other streams.
        window.InvokeUntil("top", () => window.Find("page.next") != null, "The Top Streams page");
        var firstPage = window.DisplayedText(WaitForStreamTile(window, previous: null));
        var providers = window.Element("provider");
        var choices = window.OpenItems("provider", "provider", "Providers with text in the drop-down");
        var selection = FrontendWindow.Selection(providers);
        var shown = choices.Select(window.DisplayedText).ToArray();
        Assert.Equal(["Kick", "Twitch"], shown.Order());
        var seen = "";
        var oneSelected = "One of the shown providers selected";
        window.WaitFor(ShownSelection, FrontendWindow.StepTimeout, oneSelected, Seen);

        // Closed without choosing, which would query the provider again.
        FrontendWindow.ExpandCollapse(providers).Collapse();
        window.InvokeUntil("page.next", () => window.ShowsText("Page 2"), "Page 2 of the top streams");
        WaitForStreamTile(window, firstPage);

        // The VOD viewer offers the followed channels and lists a chosen channel's VODs.
        window.InvokeUntil("vod", () => window.Find("vod.channels") != null, "The VOD viewer");

        window.Element("vod.type");
        var vodProviders = window.OpenItems("provider", "provider", "VOD providers");
        Assert.Equal(["Twitch", "YouTube"], vodProviders.Select(window.DisplayedText).Order());
        var youtube = Assert.Single(vodProviders, item => window.DisplayedText(item) == "YouTube");
        FrontendWindow.Choose(youtube, window.Element("provider"));
        window.WaitUntilDisabled("vod.type");

        vodProviders = window.OpenItems("provider", "provider", "VOD providers");
        var twitch = Assert.Single(vodProviders, item => window.DisplayedText(item) == "Twitch");
        FrontendWindow.Choose(twitch, window.Element("provider"));
        var vodTypes = window.OpenItems("vod.type", "vod.type", "Twitch VOD types");
        Assert.Equal(["archive", "highlight", "upload"], vodTypes.Select(window.DisplayedText));
        FrontendWindow.ExpandCollapse(window.Element("vod.type")).Collapse();

        var knownChannels = "The followed channels in the VOD viewer";
        var known = window.OpenItems("vod.channels", "vod.known", knownChannels, ready: KnownChannelsShown);
        FrontendWindow.Choose(known[0], window.Element("vod.known"));
        var vod = window.Element("tile.0", FrontendWindow.QueryTimeout);
        Assert.False(string.IsNullOrWhiteSpace(window.DisplayedText(vod)), "The first VOD shows no text.");
        VerifyVodProviderFallback(window);

        bool KnownChannelsShown(string[] labels) => labels.Order(StringComparer.CurrentCultureIgnoreCase).SequenceEqual(ascending);

        string Seen() => seen;

        IUIAutomationElement ShownSelection()
        {
            var items = FrontendWindow.Elements(selection.GetCurrentSelection());
            var item = items is [var only] ? only : null;
            seen = $"selected [{string.Join(", ", items.Select(window.DisplayedText))}]";
            return item != null && shown.Contains(window.DisplayedText(item)) ? item : null;
        }
    }

    private static void VerifyVodProviderFallback(FrontendWindow window)
    {
        window.InvokeUntil("back", () => window.Find("channel") != null, "The followed channels page");
        var providers = window.OpenItems("provider", "provider", "Followed channel providers");
        Assert.Equal(["Kick", "Twitch", "YouTube"], providers.Select(window.DisplayedText).Order());
        ChooseProvider(providers, "Kick");

        window.InvokeUntil("vod", () => window.Find("vod.channels") != null, "The VOD viewer");
        AssertSelectedProvider("Twitch");

        window.InvokeUntil("back", () => window.Find("channel") != null, "The followed channels page");
        AssertSelectedProvider("Kick");

        window.InvokeUntil("vod", () => window.Find("vod.channels") != null, "The VOD viewer");
        var vodProviders = window.OpenItems("provider", "provider", "VOD providers");
        ChooseProvider(vodProviders, "YouTube");
        AssertSelectedProvider("YouTube");

        void ChooseProvider(IUIAutomationElement[] choices, string name)
        {
            var choice = choices.Single(item => window.DisplayedText(item) == name);
            FrontendWindow.Choose(choice, window.Element("provider"));
        }

        void AssertSelectedProvider(string name)
        {
            var choices = window.OpenItems("provider", "provider", "Providers with a selection");
            var selected = Assert.Single(choices, item =>
                FrontendWindow.SelectionItem(item).CurrentIsSelected != 0);
            Assert.Equal(name, window.DisplayedText(selected));
            FrontendWindow.ExpandCollapse(window.Element("provider")).Collapse();
        }
    }

    // The first top stream tile showing a viewer count, once it shows a stream other than the previous one.
    private static IUIAutomationElement WaitForStreamTile(FrontendWindow window, string previous)
    {
        return window.WaitFor(FindTile, FrontendWindow.QueryTimeout, "A top stream tile with a viewer count");

        IUIAutomationElement FindTile()
        {
            if (window.Find("tile.0") is not { } tile) return null;

            var texts = window.Texts(tile);
            var counted = texts.Any(text => text.StartsWith("Viewers:", StringComparison.Ordinal));
            return counted && window.DisplayedText(tile) != previous ? tile : null;
        }
    }

    private static bool SameNames(string[] names, string[] expected) =>
        names.Length == expected.Length && !expected.Except(names).Any();
}
