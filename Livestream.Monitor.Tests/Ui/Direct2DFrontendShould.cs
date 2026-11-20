using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Interop.UIAutomationClient;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Windows.Monitor;
using Xunit;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>
/// End-to-end user journeys through the application, driven through UI Automation. The main journey runs as most users
/// do, with a Twitch token and followed streams, and covers in one launch what updates most often break: loading
/// saved data, list bindings and drawing, enabled states, provider queries, paging, data shared between pages and
/// saving settings. A journey without a token, also in one launch, covers autosaved preferences and saving or
/// reverting a platform draft, and checks that Twitch asks for authorization where it needs it.
/// </summary>
[Trait("Category", "UI")]
[Collection(UiApplicationCollection.Name)]
public class Direct2DFrontendShould
{
    // Poll callbacks complete synchronously before either window is disposed.
    // ReSharper disable AccessToDisposedClosure
    [Fact]
    [Trait("Feature", "QualitySettings")]
    public void EditQualityPreferencesInSettingsWithoutChangingOtherPlatforms()
    {
        const string TwitchInput = "settings.quality.input.twitchtv";
        const string AddQuality = "settings.quality.add";
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
        using (var window = FrontendWindow.Launch(profile))
        {
            window.Element("channel", FrontendWindow.StartupTimeout);
            const string PlatformId = "provider";
            var platformChoices = window.OpenItems(PlatformId, PlatformId, "Friendly platform names");
            var displayedNames = platformChoices.Select(window.DisplayedText).Order().ToArray();
            Assert.Equal(new[] { "Kick", "Twitch", "YouTube" }, displayedNames);
            var twitchChoice = platformChoices.Single(choice => window.DisplayedText(choice) == "Twitch");
            FrontendWindow.Choose(twitchChoice, window.Element(PlatformId));
            Assert.Null(window.Find("qualities"));
            window.InvokeUntil("settings", () => window.Find("settings.section.0") != null, "Settings opened");
            var general = window.Element("settings.section.0");
            FrontendWindow.ExpandCollapse(general).Collapse();
            var section = window.Element("settings.section.4");
            var expand = FrontendWindow.ExpandCollapse(section);
            Assert.Equal(ExpandCollapseState.ExpandCollapseState_Collapsed, expand.CurrentExpandCollapseState);
            Assert.True(window.Find("settings.save") is null or { CurrentIsOffscreen: not 0 });
            expand.Expand();
            window.InvokeUntil("settings.scrolldown", () => window.Find("settings.quality.best") != null, "Fallback scrolled into view");

            var best = window.Element("settings.quality.best");
            Assert.True(FrontendWindow.SelectionItem(best).CurrentIsSelected != 0);
            RevealQualitySave(window);
            window.WaitUntilDisabled("settings.save");
            const string AllInput = "settings.quality.input.all";
            window.InvokeUntil("settings.scrollup", () => window.Find(AllInput) != null, "All input scrolled into view");

            window.SetText(AllInput, "global-source");

            window.InvokeUntil(AddQuality, () => window.ShowsText("global-source") || window.ShowsText("1   global-source"), "All quality added");
            RevealQualitySave(window);
            window.InvokeUntil("settings.save", AllSaved, "All quality saved");
            SelectPlatform(window, "Twitch");
            Assert.True(window.ShowsText("Custom preferences here replace 'All'."));
            RevealQualitySave(window);
            window.WaitUntilDisabled("settings.save");

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

            window.InvokeUntil("settings.scrollup", () => window.Find(TwitchInput) != null, "Quality input scrolled into view");

            Add("720p60");
            Add("1080p60");
            Add("480p");
            Add("720p");
            RevealInput();
            window.SetText(TwitchInput, "720P");
            window.WaitUntilDisabled(AddQuality);
            window.SetText(TwitchInput, "");
            ActOnRow("remove", 1, () => !HasQuality("480p"));
            ActOnRow("down", 0, () => HasRank(1, "1080p60"));
            ActOnRow("up", 2, () => HasRank(2, "720p60"));

            void RevealInput()
            {
                window.InvokeUntil("settings.scrolldown", () => window.Find(TwitchInput) != null, "Quality input scrolled into view");
            }

            void Add(string quality)
            {
                RevealInput();
                window.SetText(TwitchInput, quality);
                window.InvokeUntil(AddQuality, () => HasQuality(quality), "Quality added");
            }

            bool HasQuality(string quality) => window.ShowsText(quality) ||
                Enumerable.Range(1, 4).Any(rank => window.ShowsText($"{rank}   {quality}"));

            bool HasRank(int rank, string quality) => window.ShowsText($"{rank}   {quality}");

            void ActOnRow(string action, int index, Func<bool> done)
            {
                var id = "settings.quality." + action + "." + index;
                window.InvokeUntil("settings.scrollup", () => window.Find(id) != null, "Quality row scrolled into view");

                window.InvokeUntil(id, done, "Quality " + action);
            }

            window.InvokeUntil("settings.scrolldown", () => window.Find("settings.quality.worst") != null, "Fallback scrolled into view");

            var worst = window.Element("settings.quality.worst");
            FrontendWindow.Choose(worst, worst);
            RevealQualitySave(window);
            window.InvokeUntil("settings.save", Saved, "Quality preferences saved");
            window.WaitUntilDisabled("settings.save");

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

        using var restored = FrontendWindow.Launch(profile);
        restored.Element("channel", FrontendWindow.StartupTimeout);
        restored.InvokeUntil("settings", () => restored.Find("settings.section.0") != null, "Settings reopened after restart");
        var restoredGeneral = restored.Element("settings.section.0");
        FrontendWindow.ExpandCollapse(restoredGeneral).Collapse();
        var restoredSection = restored.Element("settings.section.4");
        FrontendWindow.ExpandCollapse(restoredSection).Expand();
        SelectPlatform(restored, "Twitch");
        foreach (var (rank, quality) in new[] { (1, "1080p60"), (2, "720p60"), (3, "720p") })
            restored.InvokeUntil("settings.scrolldown", () => restored.ShowsText($"{rank}   {quality}"), "Saved priority restored");

        restored.InvokeUntil("settings.scrolldown", () => restored.Find("settings.quality.worst") != null, "Saved fallback scrolled into view");

        var restoredWorst = restored.Element("settings.quality.worst");
        Assert.True(FrontendWindow.SelectionItem(restoredWorst).CurrentIsSelected != 0);
        RevealQualitySave(restored);
        restored.WaitUntilDisabled("settings.save");

        restored.InvokeUntil("settings.scrollup", () => restored.Find(TwitchInput) != null, "Quality input scrolled into view");

        restored.SetText(TwitchInput, "360p");

        restored.InvokeUntil(AddQuality, () => restored.ShowsText("360p") || restored.ShowsText("1   360p"), "Unsaved quality added");
        restored.InvokeUntil("settings.scrolldown", () => restored.Find(TwitchInput) != null, "Quality input scrolled into view");

        restored.SetText(TwitchInput, "typed-but-not-added");
        const string Close = "settings.close";
        restored.InvokeUntil(Close, () => restored.Find("dialog.cancel") != null, "Unsaved quality confirmation");
        restored.InvokeUntil("dialog.cancel", () => restored.Find("dialog.cancel") == null, "Keep editing");
        restored.Element(TwitchInput);
        restored.InvokeUntil(Close, () => restored.Find("dialog.accept") != null, "Discard quality confirmation");
        restored.InvokeUntil("dialog.accept", () => restored.Find("dialog.accept") == null, "Discarded quality changes");
        restored.WaitFor(() => restored.Find(Close) == null ? restored : null, FrontendWindow.StepTimeout, "Settings closed after discard");
        restored.Element("settings");
        restored.InvokeUntil("settings", () => restored.Find("settings.section.0") != null, "Settings reopened after discard");
        var generalAfterDiscard = restored.Element("settings.section.0");
        FrontendWindow.ExpandCollapse(generalAfterDiscard).Collapse();
        var qualityAfterDiscard = restored.Element("settings.section.4");
        FrontendWindow.ExpandCollapse(qualityAfterDiscard).Expand();
        SelectPlatform(restored, "Twitch");
        RevealQualitySave(restored);
        restored.WaitUntilDisabled("settings.save");
        Assert.False(restored.ShowsText("360p"));
        Assert.False(restored.ShowsText("1   360p"));
        restored.InvokeUntil("settings.scrollup", () => restored.Find(TwitchInput) != null, "Discarded input scrolled into view");

        var discardedInput = restored.Element(TwitchInput);
        Assert.Equal("", FrontendWindow.Value(discardedInput).CurrentValue);

        static void RevealQualitySave(FrontendWindow target)
        {
            target.InvokeUntil("settings.scrolldown", () => target.Find("settings.save") != null, "Quality save scrolled into view");
        }
    }

    [Fact]
    public void SaveSettingsAndAskForTwitchAuthorizationWithoutAToken()
    {
        const string GeneralSection = "settings.section.0";
        const string KickSection = "settings.section.2";
        const string StreamNotificationsSection = "settings.section.5";
        const string KickSave = "settings.Kick.save";
        const string KickRevert = "settings.Kick.revert";
        const string InitialArgs = "{url}";
        const string SavedArgs = "{url} --example";
        const string HideOutputSetting = nameof(Settings.HideStreamOutputMessageBoxOnLoad);
        const string CategoryChangesSetting = nameof(Settings.EnableCategoryChangeNotifications);
        var executable = Assert.IsType<string>(Environment.ProcessPath);
        const string HideOutput = "settings.output";
        const string CategoryChanges = "settings.categorychangenotifications";
        const string Close = "settings.close";

        using var profile = UiTestProfile.CreateWithoutToken();
        var settings = JsonNode.Parse(File.ReadAllText(profile.SettingsPath))!.AsObject();
        foreach (var platform in new[] { "Twitch", "Kick", "YouTube" })
        {
            var commands = new JsonObject();
            foreach (var command in new[] { "StreamCommand", "VodCommand", "ChatCommand" })
                commands[command] = new JsonObject { ["FilePath"] = executable, ["Args"] = InitialArgs };

            settings[platform] = commands;
        }

        File.WriteAllText(profile.SettingsPath, settings.ToJsonString());

        using var window = FrontendWindow.Launch(profile);

        // Without followed channels the VOD viewer has none to pick.
        FrontendWindow.Invoke(window.Element("vod", FrontendWindow.StartupTimeout));
        window.WaitUntilDisabled("vod.channels");
        window.InvokeUntil("back", () => window.Find("channel") != null, "The followed channels page");

        // Independent preferences save as they change; in Direct2D, so does one changed from the tray popup while Settings
        // is open.
        Open();
        ToggleAndCheckTheSave(HideOutput, HideOutputSetting);
        Section(StreamNotificationsSection).Expand();
        ToggleAndCheckTheSave(CategoryChanges, CategoryChangesSetting);
        Section(StreamNotificationsSection).Collapse();
        DisableTheTrayPopupFromItsToast();

        window.InvokeUntil(Close, () => window.Find(Close) == null, "Settings dismissed after autosave");

        Open();
        var shownPreference = FrontendWindow.Toggle(window.Element(HideOutput));
        Assert.Equal(ToggleState.ToggleState_On, shownPreference.CurrentToggleState);

        // A platform draft blocks closing Settings until it is saved or reverted.
        var kickArgs = ShowKickArgs();
        FrontendWindow.Value(kickArgs).SetValue(SavedArgs);
        window.WaitFor(() => window.Element(KickSection).CurrentName.Contains("unsaved", StringComparison.Ordinal) ? kickArgs : null,
            FrontendWindow.StepTimeout, "Kick draft marked unsaved");
        Section(KickSection).Collapse();
        Assert.Equal(InitialArgs, ReadKickArgs());
        window.InvokeUntil(Close, () => window.Find(KickSave) is { CurrentIsEnabled: not 0 }, "Close reveals the edited platform");
        window.InvokeUntil(KickSave, () => window.Find(KickSave) is { CurrentIsEnabled: 0 }, "Kick saved");
        Assert.Equal(SavedArgs, ReadKickArgs());
        window.InvokeUntil(Close, () => window.Find(Close) == null, "Settings dismissed after saving");

        Open();
        var editedArgs = ShowKickArgs();
        var kickArgsValue = FrontendWindow.Value(editedArgs);
        Assert.Equal(SavedArgs, kickArgsValue.CurrentValue);
        kickArgsValue.SetValue("second edit");
        Section(KickSection).Collapse();
        window.InvokeUntil(Close, () => window.Find(KickRevert) is { CurrentIsEnabled: not 0 }, "Kick draft blocks close again");
        window.InvokeUntil(KickRevert, () => window.Find(KickRevert) is { CurrentIsEnabled: 0 }, "Kick reverted");
        Assert.Equal(SavedArgs, ReadKickArgs());
        window.InvokeUntil(Close, () => window.Find(Close) == null, "Settings dismissed after reverting");

        Open();
        var revertedArgs = ShowKickArgs();
        Assert.Equal(SavedArgs, FrontendWindow.Value(revertedArgs).CurrentValue);
        window.InvokeUntil(Close, () => window.Find(Close) == null, "Settings dismissed after checking the reverted draft");

        // Twitch asks for authorization to add a channel or browse top streams.
        window.SetText("channel", UiTestProfile.FollowedChannels[0]);
        window.DeclineAuthorizationAfter("add", checkMessageRendering: true);
        window.DeclineAuthorizationAfter("top");

        void Open()
        {
            window.Element("settings", FrontendWindow.StartupTimeout);
            window.InvokeUntil("settings", () => window.Find(Close) != null, "Settings opened");
        }

        IUIAutomationExpandCollapsePattern Section(string id) =>
            FrontendWindow.ExpandCollapse(window.Element(id));

        // The application shows the new state only after its synchronous save, so the file is read once after that.
        void ToggleAndCheckTheSave(string checkboxId, string setting)
        {
            var checkbox = window.Element(checkboxId);
            var toggle = FrontendWindow.Toggle(checkbox);
            Assert.Equal(ToggleState.ToggleState_Off, toggle.CurrentToggleState);

            toggle.Toggle();

            window.WaitFor(() => toggle.CurrentToggleState == ToggleState.ToggleState_On ? checkbox : null, FrontendWindow.StepTimeout, $"{setting} shown on");
            Assert.True(ReadFlag(profile, setting));
        }

        void DisableTheTrayPopupFromItsToast()
        {
            var checkbox = window.Element("settings.traypopup");
            var toggle = FrontendWindow.Toggle(checkbox);
            Assert.Equal(ToggleState.ToggleState_Off, toggle.CurrentToggleState);

            ToastActivationPipe.Forward(new ToastActivation(TrayToastAction.DisablePopup));

            window.WaitFor(() => toggle.CurrentToggleState == ToggleState.ToggleState_On ? checkbox : null,
                FrontendWindow.StepTimeout, "The tray popup setting checked");
            Assert.True(ReadFlag(profile, nameof(Settings.DisableMinimizeToTrayNotification)));
        }

        IUIAutomationElement ShowKickArgs()
        {
            Section(GeneralSection).Collapse();
            Section(KickSection).Expand();

            const string PathName = "Stream command file path";
            const string ArgsName = "Stream command arguments";
            Assert.Equal(PathName, window.Element("Kick.stream.path").CurrentName);
            var field = window.Element("Kick.stream.args");
            Assert.Equal(ArgsName, field.CurrentName);
            return field;
        }

        string ReadKickArgs() => ReadSetting(profile, "Kick")?.GetProperty("StreamCommand").GetProperty("Args").GetString();
    }

    private static void SelectPlatform(FrontendWindow window, string name)
    {
        const string PlatformId = "settings.quality.platform";
        window.InvokeUntil("settings.scrollup", () => window.Find(PlatformId) != null, "Platform selector scrolled into view");

        var options = window.OpenItems(PlatformId, PlatformId, "Quality platforms");
        var selected = options.Single(option => window.DisplayedText(option) == name);
        var selector = window.Element(PlatformId);
        FrontendWindow.SelectionItem(selected).Select();
        var expand = FrontendWindow.ExpandCollapse(selector);
        window.WaitFor(() => expand.CurrentExpandCollapseState == ExpandCollapseState.ExpandCollapseState_Collapsed ? selector : null,
            FrontendWindow.StepTimeout, "Quality platform list closed after selection");
    }

    // ReSharper restore AccessToDisposedClosure
    [Fact]
    public void ManageFollowedStreamsBrowseTopStreamsAndVodsAndSaveSettings()
    {
        using var profile = UiTestProfile.Create();
        using var window = FrontendWindow.Launch(profile);
        RunMainJourney(window, profile);
    }

    /// <summary>The main journey through a launched frontend.</summary>
    private static void RunMainJourney(FrontendWindow window, UiTestProfile profile)
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

        // Settings show the saved values and save changes to the settings file.
        window.InvokeUntil("settings", () => window.Find("settings.versions") != null, "The settings");
        var versionCheck = window.Element("settings.versions");
        var toggle = FrontendWindow.Toggle(versionCheck);
        Assert.Equal(ToggleState.ToggleState_Off, toggle.CurrentToggleState);
        window.RetryUntil(toggle.Toggle, Ticked, "The version check ticked");
        window.WaitFor(() => Saved() ? versionCheck : null, FrontendWindow.StepTimeout, "The version check saved");

        bool Ticked() => toggle.CurrentToggleState == ToggleState.ToggleState_On;
        bool Saved() => ReadFlag(profile, "CheckForNewVersions") == true;
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

    private static bool? ReadFlag(UiTestProfile profile, string name) =>
        ReadSetting(profile, name) is { ValueKind: JsonValueKind.True or JsonValueKind.False } flag ? flag.GetBoolean() : null;

    // Holding the file open can make a concurrent save by the frontend fail, and the frontend then drops that save, so read it
    // only once the frontend shows the saved outcome. A read that meets a write in progress returns null.
    private static JsonElement? ReadSetting(UiTestProfile profile, string name)
    {
        try
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(profile.SettingsPath));
            return settings.RootElement.TryGetProperty(name, out var value) ? value.Clone() : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }
}
