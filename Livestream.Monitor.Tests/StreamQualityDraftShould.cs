using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.WPF.ViewModels;
using NSubstitute;
using Livestream.Monitor.Tests.TestFramework;
using Xunit;

namespace Livestream.Monitor.Tests;

public class StreamQualityDraftShould
{
    [Fact]
    public Task SaveOnlyQualityChangesWhenAnUnrelatedCommandIsInvalid() => StaThread.Run(async () =>
    {
        const string MissingCommand = "missing-unused-vod-player.exe";
        var settings = new Settings();
        foreach (var platform in new ApiPlatformSettings[] { settings.Twitch, settings.Kick, settings.YouTube })
        {
            platform.StreamCommand.FilePath = Environment.ProcessPath;
            platform.VodCommand.FilePath = Environment.ProcessPath;
            platform.ChatCommand.FilePath = Environment.ProcessPath;
        }

        settings.YouTube.VodCommand.FilePath = MissingCommand;
        var handler = Substitute.For<ISettingsHandler>();
        handler.Settings.Returns(settings);
        var model = new SettingsViewModel(handler, new ThemeSelectorViewModel(handler));
        await model.ActivateAsync();
        Assert.True(model.HasValidationErrors);
        Assert.False(model.CanSave);
        var all = model.StreamQualities.Items.Single(item => item.DisplayName == "All");
        all.NewQuality = "720p";
        all.AddQuality();
        Assert.True(model.CanSave);
        model.DisableNotifications = !settings.DisableNotifications;
        Assert.False(model.CanSave);
        model.DisableNotifications = settings.DisableNotifications;
        model.Save();

        handler.Received(1).SaveSettings();
        Assert.Equal(new[] { "720p" }, settings.FavoriteApiQualities["all"].Qualities);
        Assert.Equal(MissingCommand, settings.YouTube.VodCommand.FilePath);
        Assert.False(model.CanSave);
        await model.DeactivateAsync(true);
    });

    [Theory]
    [InlineData("{\"twitchtv\":null}")]
    [InlineData("{\"all\":null}")]
    [InlineData("{\"twitchtv\":{\"Qualities\":null,\"FallbackQuality\":\"Best\"}}")]
    [InlineData("{\"twitchtv\":{\"Qualities\":[],\"FallbackQuality\":null}}")]
    public void ReadNullSavedValuesAsDefaultsWithoutRewritingThem(string preferences)
    {
        var json = "{\"FavoriteApiQualities\":" + preferences + "}";
        var settings = JsonSerializer.Deserialize<Settings>(json, JsonDefaults.Options);
        var saved = JsonSerializer.Serialize(settings.FavoriteApiQualities, JsonDefaults.Options);
        var editor = new ApiClientsQualitiesViewModel(settings);
        Assert.False(editor.HasChanges);
        Assert.Equal("Best", settings.GetLaunchReplacements("twitchtv", "url")["{qualities}"]);
        editor.SaveChanges();
        settings.CheckForNewVersions = !settings.CheckForNewVersions;
        var serialized = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        using var document = JsonDocument.Parse(serialized);

        Assert.Equal(saved, document.RootElement.GetProperty("FavoriteApiQualities").GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandleDuplicateSavedQualitiesByPosition(bool move)
    {
        var preferences = new FavoriteQualities { Qualities = ["720p", "480p", "720p"] };
        var settings = new Settings { FavoriteApiQualities = { ["twitchtv"] = preferences } };
        var editor = new ApiClientsQualitiesViewModel(settings);
        var twitch = editor.Items.Single(item => item.DisplayName == "Twitch");
        var third = twitch.QualityRows[2];

        if (move)
        {
            Assert.True(twitch.CanMoveQualityUp(third));
            twitch.MoveQualityUp(third);
        }
        else twitch.RemoveQuality(third);

        editor.SaveChanges();
        string[] expected = move ? ["720p", "720p", "480p"] : ["720p", "480p"];
        Assert.Equal(expected, settings.FavoriteApiQualities["twitchtv"].Qualities);
    }

    [Fact]
    public void UseBestWithoutCreatingSavedEntries()
    {
        var settings = new Settings();
        var draft = new StreamQualityDraft(settings);

        foreach (var (apiName, _) in StreamQualityDraft.Platforms)
        {
            Assert.Empty(draft[apiName].Qualities);
            Assert.Equal(FavoriteQualities.FallbackQualityOption.Best, draft[apiName].FallbackQuality);
        }

        Assert.False(draft.HasChanges);
        draft.SaveChanges();
        Assert.Empty(settings.FavoriteApiQualities);
    }

    [Fact]
    public void PreserveUntouchedPlatformsAndSaveIndependentCopies()
    {
        var settings = new Settings();
        var saved = new FavoriteQualities { Qualities = ["Source_Custom", "720p60"], FallbackQuality = "Worst" };
        settings.FavoriteApiQualities["twitchtv"] = saved;
        var unknown = new FavoriteQualities { Qualities = ["custom"] };
        settings.FavoriteApiQualities["other"] = unknown;
        var draft = new StreamQualityDraft(settings);
        var originalLaunch = settings.GetLaunchReplacements("twitchtv", "https://example.com")["{qualities}"];
        draft["kick"].Qualities.Add("480p");

        Assert.Empty(settings.GetStreamQualities("kick").Qualities);
        draft.SaveChanges();

        Assert.Same(saved, settings.FavoriteApiQualities["twitchtv"]);
        Assert.Same(unknown, settings.FavoriteApiQualities["other"]);
        Assert.False(settings.FavoriteApiQualities.ContainsKey("youtube"));
        Assert.Equal(originalLaunch, settings.GetLaunchReplacements("twitchtv", "https://example.com")["{qualities}"]);
        Assert.False(draft.HasChanges);
        draft["kick"].Qualities.Add("360p");
        Assert.Equal(new[] { "480p" }, settings.FavoriteApiQualities["kick"].Qualities);
        draft.Revert();
        Assert.False(draft.HasChanges);
        Assert.Equal(new[] { "480p" }, draft["kick"].Qualities);
    }

    [Fact]
    public void KeepExistingPreferencesWhenReloadingAndEditingInWpf()
    {
        var twitchSettings = new FavoriteQualities
        {
            Qualities = ["Source_Custom", "720p60"],
            FallbackQuality = "Worst",
        };
        var settings = new Settings { FavoriteApiQualities = { ["twitchtv"] = twitchSettings } };
        var editor = new ApiClientsQualitiesViewModel(settings);
        var twitch = editor.Items.Single(item => item.DisplayName == "Twitch");
        Assert.False(editor.HasChanges);
        Assert.True(twitch.FallbackQualityWorstChecked);
        Assert.Equal(new[] { "Source_Custom", "720p60" }, twitch.Qualities);
        twitch.NewQuality = " 480p ";
        twitch.AddQuality();
        editor.SelectedPlatform = editor.Items.Single(item => item.DisplayName == "Kick");
        editor.SelectedPlatform = twitch;
        Assert.Equal("480p", twitch.Qualities[0]);
        twitch.NewQuality = "SOURCE_CUSTOM";
        Assert.False(twitch.CanAddQuality);
        twitch.MoveQualityDown(twitch.QualityRows.Single(row => row.Value == "480p"));
        twitch.RemoveQuality(twitch.QualityRows.Single(row => row.Value == "720p60"));
        Assert.Equal([1, 2], twitch.QualityRows.Select(row => row.Priority));
        Assert.True(editor.HasChanges);

        editor.Reload();

        Assert.False(editor.HasChanges);
        Assert.Equal(new[] { "Source_Custom", "720p60" }, twitch.Qualities);
        twitch.FallbackQualityBestChecked = true;
        Assert.False(twitch.FallbackQualityWorstChecked);
        editor.SaveChanges();
        Assert.Equal("Best", settings.FavoriteApiQualities["twitchtv"].FallbackQuality);
        Assert.False(settings.FavoriteApiQualities.ContainsKey("kick"));
        Assert.False(settings.FavoriteApiQualities.ContainsKey("youtube"));
    }

    [Fact]
    public void ApplyAllWithoutMergingOrCopyingItIntoProviderDrafts()
    {
        var settings = new Settings();
        var twitch = new FavoriteQualities { Qualities = ["480p"], FallbackQuality = "Worst" };
        var kick = new FavoriteQualities();
        settings.FavoriteApiQualities["twitchtv"] = twitch;
        settings.FavoriteApiQualities["kick"] = kick;
        var draft = new StreamQualityDraft(settings);
        draft["all"].Qualities.AddRange(["1080p60", "720p60"]);
        draft.SaveChanges();

        Assert.Same(twitch, settings.FavoriteApiQualities["twitchtv"]);
        Assert.Same(kick, settings.FavoriteApiQualities["kick"]);
        Assert.Equal("480p,Worst", settings.GetLaunchReplacements("twitchtv", "url")["{qualities}"]);
        Assert.Equal("1080p60,720p60,Best", settings.GetLaunchReplacements("kick", "url")["{qualities}"]);
        Assert.Equal("1080p60,720p60,Best", settings.GetLaunchReplacements("youtube", "url")["{qualities}"]);
        Assert.Empty(draft["kick"].Qualities);
        Assert.Empty(draft["youtube"].Qualities);
        Assert.False(settings.FavoriteApiQualities.ContainsKey("youtube"));
        Assert.False(draft.HasChanges);

        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var restored = JsonSerializer.Deserialize<Settings>(json, JsonDefaults.Options);
        Assert.Equal("1080p60,720p60,Best", restored.GetLaunchReplacements("youtube", "url")["{qualities}"]);
        Assert.Equal("480p,Worst", restored.GetLaunchReplacements("twitchtv", "url")["{qualities}"]);
        Assert.DoesNotContain("IsConfigured", json);
    }

    [Fact]
    public void OverrideTheWholeAllConfigurationAndReturnToItWhenCleared()
    {
        var settings = new Settings();
        var draft = new StreamQualityDraft(settings);
        draft["all"].Qualities.Add("720p");
        draft["all"].FallbackQuality = "Worst";
        draft["twitchtv"].Qualities.Add("480p");
        draft["kick"].FallbackQuality = "Worst";
        draft.SaveChanges();
        Assert.Equal("480p,Best", settings.GetLaunchReplacements("twitchtv", "url")["{qualities}"]);
        Assert.Equal("Worst", settings.GetLaunchReplacements("kick", "url")["{qualities}"]);

        draft["twitchtv"].Qualities.Clear();
        draft["kick"].FallbackQuality = "Best";
        draft.SaveChanges();
        Assert.Equal("720p,Worst", settings.GetLaunchReplacements("twitchtv", "url")["{qualities}"]);
        Assert.Equal("720p,Worst", settings.GetLaunchReplacements("kick", "url")["{qualities}"]);
    }

    [Fact]
    public void ShowTheOverrideNoteOnlyForProvidersWhenAllIsConfigured()
    {
        var editor = new ApiClientsQualitiesViewModel(new Settings());
        var all = editor.Items.Single(item => item.DisplayName == "All");
        var twitch = editor.Items.Single(item => item.DisplayName == "Twitch");
        editor.SelectedPlatform = twitch;
        Assert.False(editor.ShowOverrideNote);
        all.NewQuality = "720p";
        all.AddQuality();
        Assert.True(editor.ShowOverrideNote);
        Assert.Equal("Using 'All' settings.", twitch.EmptyQualityHint);
        editor.SelectedPlatform = all;
        Assert.False(editor.ShowOverrideNote);
        all.RemoveQuality(Assert.Single(all.QualityRows));
        editor.SelectedPlatform = twitch;
        Assert.False(editor.ShowOverrideNote);
        all.FallbackQualityWorstChecked = true;
        Assert.True(editor.ShowOverrideNote);
        editor.Reload();
        Assert.False(editor.ShowOverrideNote);
    }
}
