using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    // Poll callbacks complete synchronously before either window is disposed.
    // ReSharper disable AccessToDisposedClosure
    [Theory]
    [InlineData(FrontendKind.Wpf)]
    [InlineData(FrontendKind.Direct2D)]
    public void EditQualityPreferencesInSettingsWithoutChangingOtherPlatforms(FrontendKind kind)
    {
        const string MissingVodPlayer = "missing-unused-vod-player.exe";
        using var profile = UiTestProfile.CreateWithoutToken();
        var settings = JsonNode.Parse(File.ReadAllText(profile.SettingsPath))?.AsObject()
                       ?? throw new InvalidDataException("Missing test settings.");
        settings["FavoriteApiQualities"] = new JsonObject { ["twitchtv"] = null };
        foreach (var platform in new[] { "Twitch", "Kick", "YouTube" })
        {
            var commands = new JsonObject();
            foreach (var command in new[] { "StreamCommand", "VodCommand", "ChatCommand" })
            {
                var path = platform == "YouTube" && command == "VodCommand" ? MissingVodPlayer : Environment.ProcessPath;
                commands[command] = new JsonObject { ["FilePath"] = path, ["Args"] = "{url}" };
            }

            settings[platform] = commands;
        }

        File.WriteAllText(profile.SettingsPath, settings.ToJsonString());
        using (var window = FrontendWindow.Launch(kind, profile))
        {
            var ids = window.Ids;
            window.Element(ids.ChannelToAdd, FrontendWindow.StartupTimeout);
            var platformId = kind == FrontendKind.Wpf ? "ApiClients" : "provider";
            var platformChoices = window.OpenItems(platformId, platformId, "Friendly platform names");
            var displayedNames = platformChoices.Select(FrontendWindow.DisplayedText).Order().ToArray();
            Assert.Equal(new[] { "Kick", "Twitch", "YouTube" }, displayedNames);
            var twitchChoice = platformChoices.Single(choice => FrontendWindow.DisplayedText(choice) == "Twitch");
            FrontendWindow.Choose(twitchChoice, window.Element(platformId));
            Assert.Null(window.Find(kind == FrontendKind.Wpf ? "OpenQualities" : "qualities"));
            window.InvokeUntil(ids.OpenSettings, () => window.Find("settings.section.0") != null, "Settings opened");
            var general = window.Element("settings.section.0");
            FrontendWindow.Pattern<ExpandCollapsePattern>(general, ExpandCollapsePattern.Pattern).Collapse();
            var section = window.Element("settings.section.4");
            var expand = FrontendWindow.Pattern<ExpandCollapsePattern>(section, ExpandCollapsePattern.Pattern);
            Assert.Equal(ExpandCollapseState.Collapsed, expand.Current.ExpandCollapseState);
            expand.Expand();
            if (kind == FrontendKind.Direct2D)
                window.InvokeUntil("settings.scrolldown", () => window.Find("settings.quality.best") != null, "Fallback scrolled into view");

            var best = window.Element("settings.quality.best");
            Assert.True(FrontendWindow.Pattern<SelectionItemPattern>(best, SelectionItemPattern.Pattern).Current.IsSelected);
            window.WaitUntilDisabled(ids.SaveSettings);
            var allInput = kind == FrontendKind.Wpf ? "NewQuality" : "settings.quality.input.all";
            if (kind == FrontendKind.Direct2D)
                window.InvokeUntil("settings.scrollup", () => window.Find(allInput) != null, "All input scrolled into view");

            window.SetText(allInput, "global-source");
            var allAdd = kind == FrontendKind.Wpf ? "AddQuality" : "settings.quality.add";
            window.InvokeUntil(allAdd, () => window.ShowsText("global-source") || window.ShowsText("1   global-source"), "All quality added");
            window.InvokeUntil(ids.SaveSettings, AllSaved, "All quality saved");
            SelectPlatform(window, "Twitch");
            Assert.True(window.ShowsText("Custom preferences here replace 'All'."));
            window.WaitUntilDisabled(ids.SaveSettings);
            var inputId = kind == FrontendKind.Wpf ? "NewQuality" : "settings.quality.input.twitchtv";

            bool AllSaved()
            {
                try
                {
                    using var file = JsonDocument.Parse(File.ReadAllText(profile.SettingsPath));
                    if (!file.RootElement.TryGetProperty("FavoriteApiQualities", out var qualities) || !qualities.TryGetProperty("all", out _)) return false;

                    Assert.Equal(JsonValueKind.Null, qualities.GetProperty("twitchtv").ValueKind);
                    return true;
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    return false;
                }
            }
            var addId = kind == FrontendKind.Wpf ? "AddQuality" : "settings.quality.add";
            if (kind == FrontendKind.Direct2D)
                window.InvokeUntil("settings.scrollup", () => window.Find(inputId) != null, "Quality input scrolled into view");

            Add("720p60");
            Add("1080p60");
            Add("480p");
            Add("720p");
            RevealInput();
            window.SetText(inputId, "720P");
            window.WaitUntilDisabled(addId);
            window.SetText(inputId, "");
            var first = kind == FrontendKind.Wpf ? 1 : 0;
            ActOnRow("remove", first + 1, () => !HasQuality("480p"));
            ActOnRow("down", first, () => HasRank(1, "1080p60"));
            ActOnRow("up", first + 2, () => HasRank(2, "720p60"));

            void RevealInput()
            {
                if (kind == FrontendKind.Direct2D)
                    window.InvokeUntil("settings.scrolldown", () => window.Find(inputId) != null, "Quality input scrolled into view");
            }

            void Add(string quality)
            {
                RevealInput();
                window.SetText(inputId, quality);
                window.InvokeUntil(addId, () => HasQuality(quality), "Quality added");
            }

            bool HasQuality(string quality) => window.ShowsText(quality) ||
                Enumerable.Range(1, 4).Any(rank => window.ShowsText($"{rank}   {quality}"));

            bool HasRank(int rank, string quality)
            {
                if (kind == FrontendKind.Direct2D) return window.ShowsText($"{rank}   {quality}");

                var remove = window.Find("settings.quality.remove." + rank);
                return remove?.Current.Name == "Remove " + quality;
            }

            void ActOnRow(string action, int index, Func<bool> done)
            {
                var id = "settings.quality." + action + "." + index;
                if (kind == FrontendKind.Direct2D)
                    window.InvokeUntil("settings.scrollup", () => window.Find(id) != null, "Quality row scrolled into view");

                window.InvokeUntil(id, done, "Quality " + action);
            }
            if (kind == FrontendKind.Direct2D)
                window.InvokeUntil("settings.scrolldown", () => window.Find("settings.quality.worst") != null, "Fallback scrolled into view");

            var worst = window.Element("settings.quality.worst");
            FrontendWindow.Choose(worst, worst);
            window.InvokeUntil(ids.SaveSettings, Saved, "Quality preferences saved");
            window.WaitUntilDisabled(ids.SaveSettings);

            bool Saved()
            {
                try
                {
                    using var file = JsonDocument.Parse(File.ReadAllText(profile.SettingsPath));
                    if (!file.RootElement.TryGetProperty("FavoriteApiQualities", out var qualities)) return false;
                    if (!qualities.TryGetProperty("twitchtv", out var twitch) || twitch.ValueKind != JsonValueKind.Object) return false;

                    Assert.Equal("global-source", qualities.GetProperty("all").GetProperty("Qualities").EnumerateArray().Single().GetString());
                    Assert.False(qualities.TryGetProperty("kick", out _));
                    Assert.False(qualities.TryGetProperty("youtube", out _));
                    return twitch.GetProperty("Qualities").EnumerateArray().Select(item => item.GetString()).SequenceEqual(["1080p60", "720p60", "720p"]) &&
                           twitch.GetProperty("FallbackQuality").GetString() == "Worst";
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    return false;
                }
            }
        }

        using var restored = FrontendWindow.Launch(kind, profile);
        restored.Element(restored.Ids.ChannelToAdd, FrontendWindow.StartupTimeout);
        restored.InvokeUntil(restored.Ids.OpenSettings, () => restored.Find("settings.section.0") != null, "Settings reopened after restart");
        var restoredGeneral = restored.Element("settings.section.0");
        FrontendWindow.Pattern<ExpandCollapsePattern>(restoredGeneral, ExpandCollapsePattern.Pattern).Collapse();
        var restoredSection = restored.Element("settings.section.4");
        FrontendWindow.Pattern<ExpandCollapsePattern>(restoredSection, ExpandCollapsePattern.Pattern).Expand();
        SelectPlatform(restored, "Twitch");
        foreach (var (rank, quality) in new[] { (1, "1080p60"), (2, "720p60"), (3, "720p") })
        {
            if (kind == FrontendKind.Direct2D)
            {
                restored.InvokeUntil("settings.scrolldown", () => restored.ShowsText($"{rank}   {quality}"), "Saved priority restored");
            }
            else
            {
                var rowButton = restored.Element("settings.quality.remove." + rank);
                Assert.Equal("Remove " + quality, rowButton.Current.Name);
            }
        }

        if (kind == FrontendKind.Direct2D)
            restored.InvokeUntil("settings.scrolldown", () => restored.Find("settings.quality.worst") != null, "Saved fallback scrolled into view");

        var restoredWorst = restored.Element("settings.quality.worst");
        Assert.True(FrontendWindow.Pattern<SelectionItemPattern>(restoredWorst, SelectionItemPattern.Pattern).Current.IsSelected);
        restored.WaitUntilDisabled(restored.Ids.SaveSettings);

        var restoredInput = kind == FrontendKind.Wpf ? "NewQuality" : "settings.quality.input.twitchtv";
        if (kind == FrontendKind.Direct2D)
            restored.InvokeUntil("settings.scrollup", () => restored.Find(restoredInput) != null, "Quality input scrolled into view");

        restored.SetText(restoredInput, "360p");
        var restoredAdd = kind == FrontendKind.Wpf ? "AddQuality" : "settings.quality.add";
        restored.InvokeUntil(restoredAdd, () => restored.ShowsText("360p") || restored.ShowsText("1   360p"), "Unsaved quality added");
        if (kind == FrontendKind.Direct2D)
            restored.InvokeUntil("settings.scrolldown", () => restored.Find(restoredInput) != null, "Quality input scrolled into view");

        restored.SetText(restoredInput, "typed-but-not-added");
        var close = kind == FrontendKind.Wpf ? "PART_BackButton" : "settings.close";
        restored.InvokeUntil(close, () => restored.Find(restored.Ids.DeclinePrompt) != null, "Unsaved quality confirmation");
        restored.InvokeUntil(restored.Ids.DeclinePrompt, () => restored.Find(restored.Ids.DeclinePrompt) == null, "Keep editing");
        restored.Element(restoredInput);
        restored.InvokeUntil(close, () => restored.Find(restored.Ids.AcceptMessage) != null, "Discard quality confirmation");
        restored.InvokeUntil(restored.Ids.AcceptMessage, () => restored.Find(restored.Ids.AcceptMessage) == null, "Discarded quality changes");
        restored.Element(restored.Ids.OpenSettings);
        restored.InvokeUntil(restored.Ids.OpenSettings, () => restored.Find("settings.section.0") != null, "Settings reopened after discard");
        var generalAfterDiscard = restored.Element("settings.section.0");
        FrontendWindow.Pattern<ExpandCollapsePattern>(generalAfterDiscard, ExpandCollapsePattern.Pattern).Collapse();
        var qualityAfterDiscard = restored.Element("settings.section.4");
        FrontendWindow.Pattern<ExpandCollapsePattern>(qualityAfterDiscard, ExpandCollapsePattern.Pattern).Expand();
        SelectPlatform(restored, "Twitch");
        restored.WaitUntilDisabled(restored.Ids.SaveSettings);
        Assert.False(restored.ShowsText("360p"));
        Assert.False(restored.ShowsText("1   360p"));
        if (kind == FrontendKind.Direct2D)
            restored.InvokeUntil("settings.scrolldown", () => restored.Find(restoredInput) != null, "Discarded input scrolled into view");
        else
        {
            var bar = restored.Element("PART_VerticalScrollBar");
            var range = FrontendWindow.Pattern<RangeValuePattern>(bar, RangeValuePattern.Pattern);
            range.SetValue(range.Current.Maximum);
        }

        var discardedInput = restored.Element(restoredInput);
        Assert.Equal("", FrontendWindow.Pattern<ValuePattern>(discardedInput, ValuePattern.Pattern).Current.Value);
    }

    private static void SelectPlatform(FrontendWindow window, string name)
    {
        const string PlatformId = "settings.quality.platform";
        if (window.Ids.Executable.Contains("Direct2D", StringComparison.Ordinal))
            window.InvokeUntil("settings.scrollup", () => window.Find(PlatformId) != null, "Platform selector scrolled into view");

        var options = window.OpenItems(PlatformId, PlatformId, "Quality platforms");
        var selected = options.Single(option => FrontendWindow.DisplayedText(option) == name);
        var selector = window.Element(PlatformId);
        if (window.Ids.Executable.Contains("Direct2D", StringComparison.Ordinal))
        {
            FrontendWindow.Pattern<SelectionItemPattern>(selected, SelectionItemPattern.Pattern).Select();
            var expand = FrontendWindow.Pattern<ExpandCollapsePattern>(selector, ExpandCollapsePattern.Pattern);
            window.WaitFor(() => expand.Current.ExpandCollapseState == ExpandCollapseState.Collapsed ? selector : null,
                FrontendWindow.StepTimeout, "Quality platform list closed after selection");
        }
        else FrontendWindow.Choose(selected, selector);
    }

    // ReSharper restore AccessToDisposedClosure
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
