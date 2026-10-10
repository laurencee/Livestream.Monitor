using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Automation;
using Xunit;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>
/// End-to-end user journeys through each frontend, driven through UI Automation. The main journey runs as most users
/// do, with a Twitch token and followed streams, and covers in one launch what updates most often break: loading
/// saved data, list bindings and drawing, enabled states, provider queries, paging, data shared between pages and
/// saving settings. A short journey without a token checks that Twitch asks for authorization where it needs it.
/// </summary>
[Trait("Category", "UI")]
[Collection(UiApplicationCollection.Name)]
public class FrontendShould
{
    [Theory]
    [InlineData(FrontendKind.Wpf)]
    [InlineData(FrontendKind.Direct2D)]
    public void ManageFollowedStreamsBrowseTopStreamsAndVodsAndSaveSettings(FrontendKind kind)
    {
        using var profile = UiTestProfile.Create();
        using var window = FrontendWindow.Launch(kind, profile);
        RunMainJourney(window, profile);
    }

    [Theory]
    [InlineData(FrontendKind.Wpf)]
    [InlineData(FrontendKind.Direct2D)]
    public void AskForTwitchAuthorizationToAddAStreamOrBrowseTopStreamsWithoutAToken(FrontendKind kind)
    {
        using var profile = UiTestProfile.CreateWithoutToken();
        using var window = FrontendWindow.Launch(kind, profile);
        var ids = window.Ids;

        window.Element(ids.ChannelToAdd, FrontendWindow.StartupTimeout);
        window.SetText(ids.ChannelToAdd, UiTestProfile.FollowedChannels[0]);
        window.DeclineAuthorizationAfter(ids.AddChannel);
        window.DeclineAuthorizationAfter(ids.TopStreams);
    }

    /// <summary>The main journey through a launched frontend.</summary>
    private static void RunMainJourney(FrontendWindow window, UiTestProfile profile)
    {
        var ids = window.Ids;
        var followed = UiTestProfile.FollowedChannels;
        var ascending = followed.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();

        var saved = "The saved followed streams";
        window.WaitForRows(names => SameNames(names, followed), saved, FrontendWindow.StartupTimeout);

        // Filtering narrows the list, and clearing the filter restores it.
        window.SetText(ids.NameFilter, followed[0]);
        window.WaitForRows(names => names.SequenceEqual([followed[0]]), $"Only {followed[0]}");
        window.SetText(ids.NameFilter, "");
        window.WaitForRows(names => SameNames(names, followed), "Every followed stream after clearing the filter");

        // The first sort by name is ascending and the next descending.
        var nameHeader = window.Element(ids.NameHeader, FrontendWindow.StepTimeout, "The name column header");
        FrontendWindow.Invoke(nameHeader);
        window.WaitForRows(names => names.SequenceEqual(ascending), "Streams sorted by ascending name");
        FrontendWindow.Invoke(nameHeader);
        var descending = followed.OrderDescending(StringComparer.CurrentCultureIgnoreCase).ToArray();
        window.WaitForRows(names => names.SequenceEqual(descending), "Streams sorted by descending name");

        // A channel can be added only once a name is entered.
        window.WaitUntilDisabled(ids.AddChannel);
        window.SetText(ids.ChannelToAdd, "a channel");
        window.Element(ids.AddChannel);
        window.SetText(ids.ChannelToAdd, "");
        window.WaitUntilDisabled(ids.AddChannel);

        // Top Streams offers the providers with one selected, and its next page shows other streams.
        window.InvokeUntil(ids.TopStreams, () => window.Find(ids.NextPage) != null, "The Top Streams page");
        var firstPage = FrontendWindow.DisplayedText(WaitForStreamTile(window, previous: null));
        var providers = window.Element(ids.Providers);
        var choices = window.OpenItems(ids.Providers, ids.Providers, "Providers with text in the drop-down");
        var selection = FrontendWindow.Pattern<SelectionPattern>(providers, SelectionPattern.Pattern).Current;
        var shown = choices.Select(FrontendWindow.DisplayedText).ToArray();
        var seen = "";
        var oneSelected = "One of the shown providers selected";
        window.WaitFor(ShownSelection, FrontendWindow.StepTimeout, oneSelected, Seen);

        // Closed without choosing, which would query the provider again.
        FrontendWindow.Pattern<ExpandCollapsePattern>(providers, ExpandCollapsePattern.Pattern).Collapse();
        window.InvokeUntil(ids.NextPage, () => window.ShowsText("Page 2"), "Page 2 of the top streams");
        WaitForStreamTile(window, firstPage);

        // The VOD viewer offers the followed channels and lists a chosen channel's VODs.
        window.InvokeUntil(ids.VodViewer, () => window.Find(ids.KnownChannelsOpener) != null, "The VOD viewer");
        var knownChannels = "The followed channels in the VOD viewer";
        var known = window.OpenItems(ids.KnownChannelsOpener, ids.KnownChannels, knownChannels);
        var knownNames = known.Select(FrontendWindow.DisplayedText).Order(StringComparer.CurrentCultureIgnoreCase);
        Assert.Equal(ascending, knownNames);
        FrontendWindow.Choose(known[0], window.Element(ids.KnownChannels));
        var vod = window.Element(ids.VodTile, FrontendWindow.QueryTimeout);
        Assert.False(string.IsNullOrWhiteSpace(FrontendWindow.DisplayedText(vod)), "The first VOD shows no text.");

        // Settings show the saved values and save changes to the settings file.
        window.InvokeUntil(ids.OpenSettings, () => window.Find(ids.CheckForNewVersions) != null, "The settings");
        var versionCheck = window.Element(ids.CheckForNewVersions);
        var toggle = FrontendWindow.Pattern<TogglePattern>(versionCheck, TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, toggle.Current.ToggleState);
        window.RetryUntil(toggle.Toggle, Ticked, "The version check ticked");
        window.InvokeUntil(ids.SaveSettings, Saved, "The version check saved");

        bool Ticked() => toggle.Current.ToggleState == ToggleState.On;
        bool Saved() => ReadFlag(profile, "CheckForNewVersions") == true;

        string Seen() => seen;

        // A WPF item shows its text only once realised, and is named after its bound type until then.
        AutomationElement ShownSelection()
        {
            var items = selection.GetSelection();
            var item = items is [var only] ? only : null;
            seen = $"selected [{string.Join(", ", items.Select(FrontendWindow.DisplayedText))}]";
            return item != null && shown.Contains(FrontendWindow.DisplayedText(item)) ? item : null;
        }
    }

    // The first top stream tile showing a viewer count, once it shows a stream other than the previous one.
    private static AutomationElement WaitForStreamTile(FrontendWindow window, string previous)
    {
        return window.WaitFor(FindTile, FrontendWindow.QueryTimeout, "A top stream tile with a viewer count");

        AutomationElement FindTile()
        {
            if (window.Find(window.Ids.TopStreamTile) is not { } tile) return null;

            var texts = FrontendWindow.Texts(tile);
            var counted = texts.Any(text => text.StartsWith("Viewers:", StringComparison.Ordinal));
            return counted && FrontendWindow.DisplayedText(tile) != previous ? tile : null;
        }
    }

    private static bool SameNames(string[] names, string[] expected) =>
        names.Length == expected.Length && !expected.Except(names).Any();

    // A file read while the application writes it is read again on the next poll.
    private static bool? ReadFlag(UiTestProfile profile, string name)
    {
        try
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(profile.SettingsPath));
            var found = settings.RootElement.TryGetProperty(name, out var flag);
            return found && flag.ValueKind is JsonValueKind.True or JsonValueKind.False ? flag.GetBoolean() : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }
}
