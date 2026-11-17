using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Windows.Monitor;
using Xunit;
using Win32 = Livestream.Monitor.Direct2D.Native.Win32;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>
/// End-to-end user journeys through each frontend, driven through UI Automation. The main journey runs as most users
/// do, with a Twitch token and followed streams, and covers in one launch what updates most often break: loading
/// saved data, list bindings and drawing, enabled states, provider queries, paging, data shared between pages and
/// saving settings. A journey without a token, also in one launch, covers autosaved preferences and saving or
/// reverting a platform draft, and checks that Twitch asks for authorization where it needs it.
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
    [Trait("Feature", "QualitySettings")]
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
            Assert.True(window.Find(ids.SaveSettings) is null or { Current.IsOffscreen: true });
            expand.Expand();
            if (kind == FrontendKind.Direct2D)
                window.InvokeUntil("settings.scrolldown", () => window.Find("settings.quality.best") != null, "Fallback scrolled into view");

            var best = window.Element("settings.quality.best");
            Assert.True(FrontendWindow.Pattern<SelectionItemPattern>(best, SelectionItemPattern.Pattern).Current.IsSelected);
            RevealQualitySave(window);
            window.WaitUntilDisabled(ids.SaveSettings);
            var allInput = kind == FrontendKind.Wpf ? "NewQuality" : "settings.quality.input.all";
            if (kind == FrontendKind.Direct2D)
                window.InvokeUntil("settings.scrollup", () => window.Find(allInput) != null, "All input scrolled into view");

            window.SetText(allInput, "global-source");
            var allAdd = kind == FrontendKind.Wpf ? "AddQuality" : "settings.quality.add";
            window.InvokeUntil(allAdd, () => window.ShowsText("global-source") || window.ShowsText("1   global-source"), "All quality added");
            RevealQualitySave(window);
            window.InvokeUntil(ids.SaveSettings, AllSaved, "All quality saved");
            SelectPlatform(window, "Twitch");
            Assert.True(window.ShowsText("Custom preferences here replace 'All'."));
            RevealQualitySave(window);
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
            RevealQualitySave(window);
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
        RevealQualitySave(restored);
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
        restored.WaitFor(() => restored.Find(close) == null ? restored : null, FrontendWindow.StepTimeout, "Settings closed after discard");
        restored.Element(restored.Ids.OpenSettings);
        restored.InvokeUntil(restored.Ids.OpenSettings, () => restored.Find("settings.section.0") != null, "Settings reopened after discard");
        var generalAfterDiscard = restored.Element("settings.section.0");
        FrontendWindow.Pattern<ExpandCollapsePattern>(generalAfterDiscard, ExpandCollapsePattern.Pattern).Collapse();
        var qualityAfterDiscard = restored.Element("settings.section.4");
        FrontendWindow.Pattern<ExpandCollapsePattern>(qualityAfterDiscard, ExpandCollapsePattern.Pattern).Expand();
        SelectPlatform(restored, "Twitch");
        RevealQualitySave(restored);
        restored.WaitUntilDisabled(restored.Ids.SaveSettings);
        Assert.False(restored.ShowsText("360p"));
        Assert.False(restored.ShowsText("1   360p"));
        if (kind == FrontendKind.Direct2D)
            restored.InvokeUntil("settings.scrollup", () => restored.Find(restoredInput) != null, "Discarded input scrolled into view");
        else if (restored.Element(restoredInput).Current.IsOffscreen)
        {
            var bar = restored.Element("PART_VerticalScrollBar");
            var range = FrontendWindow.Pattern<RangeValuePattern>(bar, RangeValuePattern.Pattern);
            range.SetValue(range.Current.Maximum);
        }

        var discardedInput = restored.Element(restoredInput);
        Assert.Equal("", FrontendWindow.Pattern<ValuePattern>(discardedInput, ValuePattern.Pattern).Current.Value);

        void RevealQualitySave(FrontendWindow target)
        {
            if (kind != FrontendKind.Direct2D) return;

            target.InvokeUntil("settings.scrolldown", () => target.Find(target.Ids.SaveSettings) != null, "Quality save scrolled into view");
        }
    }

    [Theory]
    [InlineData(FrontendKind.Wpf)]
    [InlineData(FrontendKind.Direct2D)]
    public void SaveSettingsAndAskForTwitchAuthorizationWithoutAToken(FrontendKind kind)
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
        var direct2D = kind == FrontendKind.Direct2D;
        var hideOutput = direct2D ? "settings.output" : "HideStreamOutputOnLoad";
        var categoryChanges = direct2D ? "settings.categorychangenotifications" : CategoryChangesSetting;
        var close = direct2D ? "settings.close" : "PART_BackButton";

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

        using var window = FrontendWindow.Launch(kind, profile);
        var ids = window.Ids;

        if (direct2D)
        {
            // Without followed channels the VOD viewer has none to pick.
            FrontendWindow.Invoke(window.Element(ids.VodViewer, FrontendWindow.StartupTimeout));
            window.WaitUntilDisabled(ids.KnownChannelsOpener);
            window.InvokeUntil("back", () => window.Find(ids.ChannelToAdd) != null, "The followed channels page");
        }

        // Independent preferences save as they change; in Direct2D, so does one changed from the tray popup while Settings
        // is open.
        Open();
        ToggleAndCheckTheSave(hideOutput, HideOutputSetting);
        Section(StreamNotificationsSection).Expand();
        ToggleAndCheckTheSave(categoryChanges, CategoryChangesSetting);
        Section(StreamNotificationsSection).Collapse();
        if (direct2D) DisableTheTrayPopupFromItsToast();

        window.InvokeUntil(close, () => window.Find(close) == null, "Settings dismissed after autosave");

        Open();
        var shownPreference = FrontendWindow.Pattern<TogglePattern>(window.Element(hideOutput), TogglePattern.Pattern);
        Assert.Equal(ToggleState.On, shownPreference.Current.ToggleState);

        // A platform draft blocks closing Settings until it is saved or reverted.
        var kickArgs = ShowKickArgs();
        FrontendWindow.Pattern<ValuePattern>(kickArgs, ValuePattern.Pattern).SetValue(SavedArgs);
        window.WaitFor(() => window.Element(KickSection).Current.Name.Contains("unsaved", StringComparison.Ordinal) ? kickArgs : null,
            FrontendWindow.StepTimeout, "Kick draft marked unsaved");
        Section(KickSection).Collapse();
        Assert.Equal(InitialArgs, ReadKickArgs());
        window.InvokeUntil(close, () => window.Find(KickSave) is { Current.IsEnabled: true }, "Close reveals the edited platform");
        window.InvokeUntil(KickSave, () => window.Find(KickSave) is { Current.IsEnabled: false }, "Kick saved");
        Assert.Equal(SavedArgs, ReadKickArgs());
        window.InvokeUntil(close, () => window.Find(close) == null, "Settings dismissed after saving");

        Open();
        var editedArgs = ShowKickArgs();
        var kickArgsValue = FrontendWindow.Pattern<ValuePattern>(editedArgs, ValuePattern.Pattern);
        Assert.Equal(SavedArgs, kickArgsValue.Current.Value);
        kickArgsValue.SetValue("second edit");
        Section(KickSection).Collapse();
        window.InvokeUntil(close, () => window.Find(KickRevert) is { Current.IsEnabled: true }, "Kick draft blocks close again");
        window.InvokeUntil(KickRevert, () => window.Find(KickRevert) is { Current.IsEnabled: false }, "Kick reverted");
        Assert.Equal(SavedArgs, ReadKickArgs());
        window.InvokeUntil(close, () => window.Find(close) == null, "Settings dismissed after reverting");

        Open();
        var revertedArgs = ShowKickArgs();
        Assert.Equal(SavedArgs, FrontendWindow.Pattern<ValuePattern>(revertedArgs, ValuePattern.Pattern).Current.Value);
        window.InvokeUntil(close, () => window.Find(close) == null, "Settings dismissed after checking the reverted draft");

        // Twitch asks for authorization to add a channel or browse top streams.
        window.SetText(ids.ChannelToAdd, UiTestProfile.FollowedChannels[0]);
        window.DeclineAuthorizationAfter(ids.AddChannel, checkMessageRendering: direct2D);
        window.DeclineAuthorizationAfter(ids.TopStreams);

        void Open()
        {
            window.Element(ids.OpenSettings, FrontendWindow.StartupTimeout);
            window.InvokeUntil(ids.OpenSettings, () => window.Find(close) != null, "Settings opened");
        }

        ExpandCollapsePattern Section(string id) =>
            FrontendWindow.Pattern<ExpandCollapsePattern>(window.Element(id), ExpandCollapsePattern.Pattern);

        // Each frontend shows the new state only after its synchronous save, so the file is read once after that.
        void ToggleAndCheckTheSave(string checkboxId, string setting)
        {
            var checkbox = window.Element(checkboxId);
            var toggle = FrontendWindow.Pattern<TogglePattern>(checkbox, TogglePattern.Pattern);
            Assert.Equal(ToggleState.Off, toggle.Current.ToggleState);

            toggle.Toggle();

            window.WaitFor(() => toggle.Current.ToggleState == ToggleState.On ? checkbox : null, FrontendWindow.StepTimeout, $"{setting} shown on");
            Assert.True(ReadFlag(profile, setting));
        }

        void DisableTheTrayPopupFromItsToast()
        {
            var checkbox = window.Element("settings.traypopup");
            var toggle = FrontendWindow.Pattern<TogglePattern>(checkbox, TogglePattern.Pattern);
            Assert.Equal(ToggleState.Off, toggle.Current.ToggleState);

            ToastActivationPipe.Forward(new ToastActivation(TrayToastAction.DisablePopup));

            window.WaitFor(() => toggle.Current.ToggleState == ToggleState.On ? checkbox : null,
                FrontendWindow.StepTimeout, "The tray popup setting checked");
            Assert.True(ReadFlag(profile, nameof(Settings.DisableMinimizeToTrayNotification)));
        }

        // Shows the Kick section of the open Settings and returns its stream command arguments field. Direct2D fields are
        // found by automation id and their names checked against WPF's; WPF fields are found by those names, which checks
        // WPF's names.
        AutomationElement ShowKickArgs()
        {
            Section(GeneralSection).Collapse();
            Section(KickSection).Expand();

            const string PathName = "Stream command file path";
            const string ArgsName = "Stream command arguments";
            if (direct2D)
            {
                Assert.Equal(PathName, window.Element("Kick.stream.path").Current.Name);
                var field = window.Element("Kick.stream.args");
                Assert.Equal(ArgsName, field.Current.Name);
                return field;
            }

            var expander = window.Element(KickSection);
            var path = new PropertyCondition(AutomationElement.NameProperty, PathName);
            window.WaitFor(() => expander.FindFirst(TreeScope.Descendants, path), FrontendWindow.StepTimeout, PathName);
            var args = new PropertyCondition(AutomationElement.NameProperty, ArgsName);
            return window.WaitFor(() => expander.FindFirst(TreeScope.Descendants, args), FrontendWindow.StepTimeout, ArgsName);
        }

        string ReadKickArgs() => ReadSetting(profile, "Kick")?.GetProperty("StreamCommand").GetProperty("Args").GetString();
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
        RunMainJourney(window, profile, kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SaveTheWpfNotificationImageAndHandleTheTrayPopupAction(bool disablePopup)
    {
        using var profile = UiTestProfile.CreateWithoutToken();
        using var window = FrontendWindow.Launch(FrontendKind.Wpf, profile);
        const string ChannelIconId = "ChannelIconSelected";
        var settingsButton = window.Element(window.Ids.OpenSettings, FrontendWindow.StartupTimeout);
        var process = new PropertyCondition(AutomationElement.ProcessIdProperty, settingsButton.Current.ProcessId);
        var main = AutomationElement.RootElement.FindFirst(TreeScope.Children, process);
        var mainHandle = (nint)main.Current.NativeWindowHandle;
        var mainWindow = FrontendWindow.Pattern<WindowPattern>(main, WindowPattern.Pattern);
        const string StreamNotificationsId = "settings.section.5";
        // The readiness check completes before the window is disposed.
        // ReSharper disable once AccessToDisposedClosure
        window.InvokeUntil(window.Ids.OpenSettings, () => window.Find(StreamNotificationsId) != null, "The Stream Notifications group");
        var streamNotifications = window.Element(StreamNotificationsId);
        FrontendWindow.Pattern<ExpandCollapsePattern>(streamNotifications, ExpandCollapsePattern.Pattern).Expand();
        var icon = window.Element(ChannelIconId);
        Assert.True(FrontendWindow.Pattern<SelectionItemPattern>(icon, SelectionItemPattern.Pattern).Current.IsSelected);
        var thumbnail = window.Element("LiveThumbnailSelected");
        FrontendWindow.Pattern<SelectionItemPattern>(thumbnail, SelectionItemPattern.Pattern).Select();
        window.WaitFor(() => ThumbnailSaved() ? thumbnail : null, FrontendWindow.StepTimeout, "The live thumbnail setting saved");

        mainWindow.SetWindowVisualState(WindowVisualState.Minimized);
        var action = window.WaitFor(FindTrayAction, FrontendWindow.StepTimeout, "The tray popup action");
        Assert.False(Win32.IsWindowVisible(mainHandle));
        FrontendWindow.Invoke(action);

        if (disablePopup)
        {
            window.WaitFor(OptOutSaved, FrontendWindow.StepTimeout, "The tray popup opt-out saved");
            Assert.False(Win32.IsWindowVisible(mainHandle));
        }
        else
        {
            window.WaitFor(() => Win32.IsWindowVisible(mainHandle) ? main : null, FrontendWindow.StepTimeout, "The main window restored");
            Assert.NotEqual(true, ReadFlag(profile, nameof(Settings.DisableMinimizeToTrayNotification)));
        }

        // The save check completes before the test profile is disposed.
        // ReSharper disable once AccessToDisposedClosure
        bool ThumbnailSaved() =>
            ReadSetting(profile, nameof(Settings.StreamNotificationImage))?.GetString() == nameof(StreamNotificationImage.LiveThumbnail);

        // The save check completes before the test profile is disposed.
        // ReSharper disable once AccessToDisposedClosure
        AutomationElement OptOutSaved() => ReadFlag(profile, nameof(Settings.DisableMinimizeToTrayNotification)) == true ? action : null;

        AutomationElement FindTrayAction()
        {
            var title = new PropertyCondition(AutomationElement.NameProperty, "Livestream Monitor minimized to tray");
            var popup = AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(process, title));
            var actionId = disablePopup ? "DisableTrayPopup" : "RestoreTrayWindow";
            var button = new PropertyCondition(AutomationElement.AutomationIdProperty, actionId);
            return popup?.FindFirst(TreeScope.Descendants, button);
        }
    }

    /// <summary>The main journey through a launched frontend.</summary>
    private static void RunMainJourney(FrontendWindow window, UiTestProfile profile, FrontendKind kind)
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
        Assert.Equal(["Kick", "Twitch"], shown.Order());
        var seen = "";
        var oneSelected = "One of the shown providers selected";
        window.WaitFor(ShownSelection, FrontendWindow.StepTimeout, oneSelected, Seen);

        // Closed without choosing, which would query the provider again.
        FrontendWindow.Pattern<ExpandCollapsePattern>(providers, ExpandCollapsePattern.Pattern).Collapse();
        window.InvokeUntil(ids.NextPage, () => window.ShowsText("Page 2"), "Page 2 of the top streams");
        WaitForStreamTile(window, firstPage);

        if (kind == FrontendKind.Wpf)
        {
            ChooseWpfPageSize(window, "30");
            window.WaitFor(() => window.ShowsText("Page 1") ? window : null, FrontendWindow.StepTimeout, "Page 1 after changing page size");
            WaitForStreamTile(window, previous: null);
        }

        // The VOD viewer offers the followed channels and lists a chosen channel's VODs.
        window.InvokeUntil(ids.VodViewer, () => window.Find(ids.KnownChannelsOpener) != null, "The VOD viewer");
        if (kind == FrontendKind.Wpf) ChooseWpfPageSize(window, "50");

        window.Element(ids.VodTypes);
        var vodProviders = window.OpenItems(ids.VodProviders, ids.VodProviders, "VOD providers");
        Assert.Equal(["Twitch", "YouTube"], vodProviders.Select(FrontendWindow.DisplayedText).Order());
        var youtube = Assert.Single(vodProviders, item => FrontendWindow.DisplayedText(item) == "YouTube");
        FrontendWindow.Choose(youtube, window.Element(ids.VodProviders));
        window.WaitUntilDisabled(ids.VodTypes);

        vodProviders = window.OpenItems(ids.VodProviders, ids.VodProviders, "VOD providers");
        var twitch = Assert.Single(vodProviders, item => FrontendWindow.DisplayedText(item) == "Twitch");
        FrontendWindow.Choose(twitch, window.Element(ids.VodProviders));
        var vodTypes = window.OpenItems(ids.VodTypes, ids.VodTypes, "Twitch VOD types");
        Assert.Equal(["archive", "highlight", "upload"], vodTypes.Select(FrontendWindow.DisplayedText));
        FrontendWindow.Pattern<ExpandCollapsePattern>(window.Element(ids.VodTypes), ExpandCollapsePattern.Pattern).Collapse();

        var knownChannels = "The followed channels in the VOD viewer";
        var known = window.OpenItems(ids.KnownChannelsOpener, ids.KnownChannels, knownChannels, ready: KnownChannelsShown);
        FrontendWindow.Choose(known[0], window.Element(ids.KnownChannels));
        var vod = window.Element(ids.VodTile, FrontendWindow.QueryTimeout);
        Assert.False(string.IsNullOrWhiteSpace(FrontendWindow.DisplayedText(vod)), "The first VOD shows no text.");
        if (kind == FrontendKind.Direct2D) VerifyVodProviderFallback(window);

        // Settings show the saved values and save changes to the settings file.
        window.InvokeUntil(ids.OpenSettings, () => window.Find(ids.CheckForNewVersions) != null, "The settings");
        var versionCheck = window.Element(ids.CheckForNewVersions);
        var toggle = FrontendWindow.Pattern<TogglePattern>(versionCheck, TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, toggle.Current.ToggleState);
        window.RetryUntil(toggle.Toggle, Ticked, "The version check ticked");
        window.WaitFor(() => Saved() ? versionCheck : null, FrontendWindow.StepTimeout, "The version check saved");

        bool Ticked() => toggle.Current.ToggleState == ToggleState.On;
        bool Saved() => ReadFlag(profile, "CheckForNewVersions") == true;
        bool KnownChannelsShown(string[] labels) => labels.Order(StringComparer.CurrentCultureIgnoreCase).SequenceEqual(ascending);

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

    private static void ChooseWpfPageSize(FrontendWindow window, string size)
    {
        const string Selector = "PageSize";
        var choices = window.OpenItems(Selector, Selector, "Items per page choices");
        Assert.Equal(["15", "30", "50"], choices.Select(FrontendWindow.DisplayedText));
        var choice = Assert.Single(choices, item => FrontendWindow.DisplayedText(item) == size);
        FrontendWindow.Choose(choice, window.Element(Selector));
    }

    [Fact]
    public void PreserveTheWpfGameFilterAcrossProviderChanges()
    {
        using var profile = UiTestProfile.Create();
        using var window = FrontendWindow.Launch(FrontendKind.Wpf, profile);
        VerifyGameFilterSurvivesProviderChange(window);
    }

    private static void VerifyGameFilterSurvivesProviderChange(FrontendWindow window)
    {
        const string Game = "Minecraft";
        const string GameInput = "PART_EditableTextBox";
        var providerId = window.Ids.Providers;

        window.InvokeUntil(window.Ids.TopStreams, () => window.Find(GameInput) != null, "The Top Streams game filter");
        WaitForStreamTile(window, previous: null);

        const string GameChoices = "PossibleGameNames";
        var games = window.OpenItems(GameChoices, GameChoices, "Twitch game suggestions");
        Assert.Contains(games, item => FrontendWindow.DisplayedText(item) == Game);
        FrontendWindow.Pattern<ExpandCollapsePattern>(window.Element(GameChoices), ExpandCollapsePattern.Pattern).Collapse();

        var gameText = FrontendWindow.Pattern<ValuePattern>(window.Element(GameInput), ValuePattern.Pattern);
        var kick = FindProvider("Kick");
        var switchToKick = FrontendWindow.Pattern<SelectionItemPattern>(kick, SelectionItemPattern.Pattern);
        gameText.SetValue(Game);
        switchToKick.Select();

        window.WaitUntilDisabled("PossibleGameNames");
        FrontendWindow.Choose(FindProvider("Twitch"), window.Element(providerId));
        window.WaitFor(RestoredInput, FrontendWindow.StepTimeout, "The game filter restored on Twitch");
        window.WaitFor(FilteredTile, FrontendWindow.QueryTimeout, "Top Streams filtered by the restored game");

        AutomationElement FilteredTile() => window.Find(window.Ids.TopStreamTile) is { } tile && FrontendWindow.Texts(tile).Contains(Game)
            ? tile : null;

        AutomationElement RestoredInput() => window.Find(GameInput) is { Current.IsEnabled: true } input &&
                                             FrontendWindow.Pattern<ValuePattern>(input, ValuePattern.Pattern).Current.Value == Game
            ? input : null;

        AutomationElement FindProvider(string name)
        {
            var choices = window.OpenItems(providerId, providerId, "Top Streams providers");
            return choices.Single(item => FrontendWindow.DisplayedText(item) == name);
        }
    }

    private static void VerifyVodProviderFallback(FrontendWindow window)
    {
        var ids = window.Ids;
        window.InvokeUntil("back", () => window.Find(ids.ChannelToAdd) != null, "The followed channels page");
        var providers = window.OpenItems(ids.Providers, ids.Providers, "Followed channel providers");
        Assert.Equal(["Kick", "Twitch", "YouTube"], providers.Select(FrontendWindow.DisplayedText).Order());
        ChooseProvider(providers, "Kick");

        window.InvokeUntil(ids.VodViewer, () => window.Find(ids.KnownChannelsOpener) != null, "The VOD viewer");
        AssertSelectedProvider("Twitch");

        window.InvokeUntil("back", () => window.Find(ids.ChannelToAdd) != null, "The followed channels page");
        AssertSelectedProvider("Kick");

        window.InvokeUntil(ids.VodViewer, () => window.Find(ids.KnownChannelsOpener) != null, "The VOD viewer");
        var vodProviders = window.OpenItems(ids.VodProviders, ids.VodProviders, "VOD providers");
        ChooseProvider(vodProviders, "YouTube");
        AssertSelectedProvider("YouTube");

        void ChooseProvider(AutomationElement[] choices, string name)
        {
            var choice = choices.Single(item => FrontendWindow.DisplayedText(item) == name);
            FrontendWindow.Choose(choice, window.Element(ids.Providers));
        }

        void AssertSelectedProvider(string name)
        {
            var choices = window.OpenItems(ids.Providers, ids.Providers, "Providers with a selection");
            var selected = Assert.Single(choices, item =>
                FrontendWindow.Pattern<SelectionItemPattern>(item, SelectionItemPattern.Pattern).Current.IsSelected);
            Assert.Equal(name, FrontendWindow.DisplayedText(selected));
            FrontendWindow.Pattern<ExpandCollapsePattern>(window.Element(ids.Providers), ExpandCollapsePattern.Pattern).Collapse();
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
