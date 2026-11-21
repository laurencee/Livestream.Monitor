using System.Text.Json;
using ExternalAPIs;
using Livestream.Monitor.Core.Settings;
using Xunit;

namespace Livestream.Monitor.Tests;

[Trait("Feature", "QualitySettings")]
public class StreamQualityDraftShould
{
    [Theory]
    [InlineData("{\"twitchtv\":null}")]
    [InlineData("{\"twitchtv\":{\"Qualities\":null,\"FallbackQuality\":null}}")]
    public void ReadNullSavedValuesAsDefaultsWithoutRewritingThem(string preferences)
    {
        var json = "{\"FavoriteApiQualities\":" + preferences + "}";
        var settings = JsonSerializer.Deserialize<Settings>(json, JsonDefaults.Options);
        var saved = JsonSerializer.Serialize(settings.FavoriteApiQualities, JsonDefaults.Options);
        var editor = new StreamQualityDraft(settings);
        Assert.False(editor.HasChanges);
        Assert.Equal("Best", settings.GetLaunchReplacements("twitchtv", "url")["{qualities}"]);
        editor.SaveChanges();
        settings.CheckForNewVersions = !settings.CheckForNewVersions;
        var serialized = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        using var document = JsonDocument.Parse(serialized);

        Assert.Equal(saved, document.RootElement.GetProperty("FavoriteApiQualities").GetRawText());
    }

    [Fact]
    public void PreserveUntouchedPlatformsAndSaveIndependentCopies()
    {
        // Without saved entries every platform uses Best, and saving creates none.
        var unsaved = new Settings();
        var unsavedDraft = new StreamQualityDraft(unsaved);
        foreach (var (apiName, _) in StreamQualityDraft.Platforms)
        {
            Assert.Empty(unsavedDraft[apiName].Qualities);
            Assert.Equal(FavoriteQualities.FallbackQualityOption.Best, unsavedDraft[apiName].FallbackQuality);
        }

        Assert.False(unsavedDraft.HasChanges);
        unsavedDraft.SaveChanges();
        Assert.Empty(unsaved.FavoriteApiQualities);

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
}
