using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Interop.UIAutomationClient;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D;
using Livestream.Monitor.Direct2D.Windows.Monitor;
using Xunit;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>
/// End-to-end user journeys through Settings, driven through UI Automation, each in one launch without a Twitch token.
/// One covers an autosaved preference, saving a platform draft and Twitch asking for authorization where it needs it;
/// the other covers editing quality preferences. They share nothing with the main window's journey and each application
/// runs on its own data directory, so this class is its own collection and runs alongside that journey.
/// </summary>
[Trait("Category", "UI")]
public class Direct2DSettingsShould
{
    // Poll callbacks complete synchronously before the window is disposed.
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
        using var window = FrontendWindow.Launch(profile);

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
        RevealQualitySave();
        window.WaitUntilDisabled("settings.save");
        const string AllInput = "settings.quality.input.all";
        window.InvokeUntil("settings.scrollup", () => window.Find(AllInput) != null, "All input scrolled into view");

        window.SetText(AllInput, "global-source");

        window.InvokeUntil(AddQuality, () => window.ShowsText("global-source") || window.ShowsText("1   global-source"), "All quality added");
        RevealQualitySave();
        window.InvokeUntil("settings.save", AllSaved, "All quality saved");
        SelectPlatform(window, "Twitch");
        Assert.True(window.ShowsText("Custom preferences here replace 'All'."));
        RevealQualitySave();
        window.WaitUntilDisabled("settings.save");

        // A new quality takes the first rank; one is removed and one moved down before saving.
        window.InvokeUntil("settings.scrollup", () => window.Find(TwitchInput) != null, "Quality input scrolled into view");

        Add("720p60");
        Add("1080p60");
        Add("480p");
        Add("720p");
        ActOnRow("remove", 1, () => !HasQuality("480p"));
        ActOnRow("down", 0, () => HasRank(1, "1080p60"));

        window.InvokeUntil("settings.scrolldown", () => window.Find("settings.quality.worst") != null, "Fallback scrolled into view");

        var worst = window.Element("settings.quality.worst");
        FrontendWindow.Choose(worst, worst);
        RevealQualitySave();
        window.InvokeUntil("settings.save", Saved, "Quality preferences saved");
        window.WaitUntilDisabled("settings.save");

        // Closing with an unsaved quality and typed text asks before discarding them.
        window.InvokeUntil("settings.scrollup", () => window.Find(TwitchInput) != null, "Quality input scrolled into view");

        Add("360p");
        RevealInput();
        window.SetText(TwitchInput, "typed-but-not-added");
        const string Close = "settings.close";
        window.InvokeUntil(Close, () => window.Find("dialog.cancel") != null, "Unsaved quality confirmation");
        window.InvokeUntil("dialog.cancel", () => window.Find("dialog.cancel") == null, "Keep editing");
        window.Element(TwitchInput);
        window.InvokeUntil(Close, () => window.Find("dialog.accept") != null, "Discard quality confirmation");
        window.InvokeUntil("dialog.accept", () => window.Find("dialog.accept") == null, "Discarded quality changes");
        window.WaitFor(() => window.Find(Close) == null ? window : null, FrontendWindow.StepTimeout, "Settings closed after discard");
        window.InvokeUntil("settings", () => window.Find("settings.section.0") != null, "Settings reopened after discard");
        FrontendWindow.ExpandCollapse(window.Element("settings.section.0")).Collapse();
        FrontendWindow.ExpandCollapse(window.Element("settings.section.4")).Expand();
        SelectPlatform(window, "Twitch");
        RevealQualitySave();
        window.WaitUntilDisabled("settings.save");
        Assert.False(HasQuality("360p"));
        window.InvokeUntil("settings.scrollup", () => window.Find(TwitchInput) != null, "Discarded input scrolled into view");

        var discardedInput = window.Element(TwitchInput);
        Assert.Equal("", FrontendWindow.Value(discardedInput).CurrentValue);

        void RevealQualitySave()
        {
            window.InvokeUntil("settings.scrolldown", () => window.Find("settings.save") != null, "Quality save scrolled into view");
        }

        bool AllSaved()
        {
            try
            {
                using var file = JsonDocument.Parse(File.ReadAllText(profile.SettingsPath));
                if (!file.RootElement.TryGetProperty("FavoriteApiQualities", out var qualities)) return false;
                if (!qualities.TryGetProperty("all", out _)) return false;

                Assert.Equal(JsonValueKind.Null, qualities.GetProperty("twitchtv").ValueKind);
                return true;
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                return false;
            }
        }

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
                var saved = twitch.GetProperty("Qualities").EnumerateArray().Select(item => item.GetString());
                return saved.SequenceEqual(["1080p60", "720p", "720p60"]) && twitch.GetProperty("FallbackQuality").GetString() == "Worst";
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                return false;
            }
        }
    }

    [Fact]
    public void SaveSettingsAndAskForTwitchAuthorizationWithoutAToken()
    {
        const string GeneralSection = "settings.section.0";
        const string KickSection = "settings.section.2";
        const string KickSave = "settings.Kick.save";
        const string InitialArgs = "{url}";
        const string SavedArgs = "{url} --example";
        var executable = Assert.IsType<string>(Environment.ProcessPath);
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

        // An independent preference saves as it changes, and so does one changed from the tray popup while Settings is open.
        // The application shows the new state only after its synchronous save, so the file is read once after that.
        window.InvokeUntil("settings", () => window.Find(Close) != null, "Settings opened");
        ToggleAndCheckTheSave("settings.output", nameof(Settings.HideStreamOutputMessageBoxOnLoad), toggle => toggle.Toggle());
        var disablePopup = new ToastActivation(TrayToastAction.DisablePopup);
        var toastPipe = ToastActivationPipe.GetPipeName(Program.GetProfileScope(profile.DataDirectory));
        ToggleAndCheckTheSave(
            "settings.traypopup",
            nameof(Settings.DisableMinimizeToTrayNotification),
            _ => ToastActivationPipe.Forward(disablePopup, toastPipe));

        // A platform draft blocks closing Settings until it is saved.
        Section(GeneralSection).Collapse();
        Section(KickSection).Expand();
        Assert.Equal("Stream command file path", window.Element("Kick.stream.path").CurrentName);
        var kickArgs = window.Element("Kick.stream.args");
        Assert.Equal("Stream command arguments", kickArgs.CurrentName);
        FrontendWindow.Value(kickArgs).SetValue(SavedArgs);
        window.WaitFor(() => window.Element(KickSection).CurrentName.Contains("unsaved", StringComparison.Ordinal) ? kickArgs : null,
            FrontendWindow.StepTimeout, "Kick draft marked unsaved");
        Section(KickSection).Collapse();
        Assert.Equal(InitialArgs, ReadKickArgs());
        window.InvokeUntil(Close, () => window.Find(KickSave) is { CurrentIsEnabled: not 0 }, "Close reveals the edited platform");
        window.InvokeUntil(KickSave, () => window.Find(KickSave) is { CurrentIsEnabled: 0 }, "Kick saved");
        Assert.Equal(SavedArgs, ReadKickArgs());
        window.InvokeUntil(Close, () => window.Find(Close) == null, "Settings dismissed after saving");

        // Twitch asks for authorization to add a channel or browse top streams.
        window.SetText("channel", UiTestProfile.FollowedChannels[0]);
        window.DeclineAuthorizationAfter("add", checkMessageRendering: true);
        window.DeclineAuthorizationAfter("top");

        IUIAutomationExpandCollapsePattern Section(string id) =>
            FrontendWindow.ExpandCollapse(window.Element(id));

        void ToggleAndCheckTheSave(string checkboxId, string setting, Action<IUIAutomationTogglePattern> change)
        {
            var checkbox = window.Element(checkboxId);
            var toggle = FrontendWindow.Toggle(checkbox);
            Assert.Equal(ToggleState.ToggleState_Off, toggle.CurrentToggleState);

            change(toggle);

            window.WaitFor(
                () => toggle.CurrentToggleState == ToggleState.ToggleState_On ? checkbox : null,
                FrontendWindow.StepTimeout,
                $"{setting} shown on");
            Assert.True(profile.ReadFlag(setting));
        }

        string ReadKickArgs() => profile.ReadSetting("Kick")?.GetProperty("StreamCommand").GetProperty("Args").GetString();
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
}
