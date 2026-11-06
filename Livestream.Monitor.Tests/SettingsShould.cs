using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests;

[Collection("Settings files")]
public class SettingsShould
{
    [Theory]
    [InlineData("Twitch")]
    [InlineData("Kick")]
    [InlineData("YouTube")]
    public void PreservePlatformDefaultsWhenCommandsArePartial(string platform)
    {
        var json = $$$$"""
            {"{{{{platform}}}}":{"StreamCommand":{"Args":"custom"},"VodCommand":{"Args":"custom"},"ChatCommand":{"Args":"custom"}}}
            """;
        var settings = JsonSerializer.Deserialize<Settings>(json, JsonDefaults.Options);
        var defaults = new Settings();
        var (actual, expected) = platform switch
        {
            "Twitch" => ((ApiPlatformSettings)settings.Twitch, (ApiPlatformSettings)defaults.Twitch),
            "Kick" => (settings.Kick, defaults.Kick),
            _ => (settings.YouTube, defaults.YouTube),
        };
        var commands = new[]
        {
            (actual.StreamCommand, expected.StreamCommand),
            (actual.VodCommand, expected.VodCommand),
            (actual.ChatCommand, expected.ChatCommand),
        };
        foreach (var (command, defaultCommand) in commands)
        {
            Assert.Equal("custom", command.Args);
            Assert.Equal(defaultCommand.FilePath, command.FilePath);
            Assert.Equal(defaultCommand.CaptureStandardOutput, command.CaptureStandardOutput);
            Assert.Equal(defaultCommand.CaptureErrorOutput, command.CaptureErrorOutput);
        }
    }

    [Theory]
    [InlineData("SettingsVersion")]
    [InlineData("CheckForNewVersions")]
    [InlineData("DisableRefreshErrorDialogs")]
    [InlineData("DisableMinimizeToTrayNotification")]
    [InlineData("MetroThemeBaseColour")]
    [InlineData("MetroThemeAccentColour")]
    [InlineData("MinimumEventViewers")]
    [InlineData("DisableNotifications")]
    [InlineData("HideStreamOutputMessageBoxOnLoad")]
    [InlineData("FavoriteApiQualities")]
    [InlineData("ShowOnlineOnly")]
    public void KeepDefaultsForSettingsThatIgnoreNull(string property)
    {
        var json = $$$"""{"{{{property}}}":null,"Twitch":{"AuthToken":"retained-token"}}""";
        var settings = JsonSerializer.Deserialize<Settings>(json, JsonDefaults.Options);
        var defaults = new Settings();
        Assert.Equal(defaults.SettingsVersion, settings.SettingsVersion);
        Assert.Equal(defaults.MinimumEventViewers, settings.MinimumEventViewers);
        Assert.Equal(defaults.MetroThemeBaseColour, settings.MetroThemeBaseColour);
        Assert.Equal(defaults.MetroThemeAccentColour, settings.MetroThemeAccentColour);
        Assert.False(settings.ShowOnlineOnly);
        Assert.Empty(settings.FavoriteApiQualities);
        Assert.Equal("retained-token", settings.Twitch.AuthToken);
    }

    [Fact]
    public void PersistMainWindowLocationAlongsideSettings()
    {
        InTemporaryDirectory(() =>
        {
            var json = $$"""{"SettingsVersion":{{Settings.CurrentSettingsVersion}}}""";
            File.WriteAllText(SettingsHandler.SettingsFileName, json);
            var settings = new SettingsHandler().Settings;
            Assert.True(WindowLocationFile.TryLoad(out var initialLocation));
            Assert.Null(initialLocation);

            settings.ShowOnlineOnly = true;
            var savedSettings = File.ReadAllText(SettingsHandler.SettingsFileName);

            var location = new WindowLocation(X: -1200, Y: 160);
            WindowLocationFile.Save(location);

            Assert.True(WindowLocationFile.TryLoad(out var restored));
            Assert.Equal(location, restored);
            Assert.Equal(savedSettings, File.ReadAllText(SettingsHandler.SettingsFileName));
        });
    }

    [Fact]
    public void ReportAnUnavailableWindowLocationFile()
    {
        InTemporaryDirectory(() =>
        {
            var location = new WindowLocation(X: 120, Y: 160);
            WindowLocationFile.Save(location);

            using (File.Open(WindowLocationFile.FileName, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.False(WindowLocationFile.TryLoad(out var unavailable));
                Assert.Null(unavailable);
            }

            Assert.True(WindowLocationFile.TryLoad(out var restored));
            Assert.Equal(location, restored);
        });
    }

    [Fact]
    public void PersistOnlineOnlyPreference()
    {
        InTemporaryDirectory(() =>
        {
            var json = $$"""{"SettingsVersion":{{Settings.CurrentSettingsVersion}}}""";
            File.WriteAllText(SettingsHandler.SettingsFileName, json);
            var settings = new SettingsHandler().Settings;
            Assert.False(settings.ShowOnlineOnly);

            settings.ShowOnlineOnly = true;
            var restored = new SettingsHandler().Settings;
            Assert.True(restored.ShowOnlineOnly);

            restored.ShowOnlineOnly = false;
            Assert.False(new SettingsHandler().Settings.ShowOnlineOnly);
        });
    }

    [Theory]
    [InlineData("/* retained preferences */", "")]
    [InlineData("", ",")]
    public void MigrateLegacySettingsWithPermittedJsonSyntax(string comment, string trailingComma)
    {
        InTemporaryDirectory(() =>
        {
            var json = $$"""
                { {{comment}} "SettingsVersion":3,"PassthroughClientId":true,"TwitchAuthToken":"retained-token"{{trailingComma}} }
                """;
            File.WriteAllText(SettingsHandler.SettingsFileName, json);
            var settings = new SettingsHandler().Settings;
            Assert.Equal(Settings.CurrentSettingsVersion, settings.SettingsVersion);
            Assert.True(settings.Twitch.PassthroughClientId);
            Assert.Equal("retained-token", settings.Twitch.AuthToken);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n")]
    public void StartFromSavedDefaultsWhenTheSettingsFileIsEmpty(string content)
    {
        InTemporaryDirectory(() =>
        {
            File.WriteAllText(SettingsHandler.SettingsFileName, content);
            var settings = new SettingsHandler().Settings;

            Assert.Equal(Settings.CurrentSettingsVersion, ReadSavedSettings().SettingsVersion);

            settings.ExcludeFromNotifying.Add(new UniqueStreamKey("twitchtv", "42"));

            Assert.Equal(new UniqueStreamKey("twitchtv", "42"), Assert.Single(ReadSavedSettings().ExcludeFromNotifying));
        });
    }

    [Fact]
    public void KeepDefaultsForTheSessionWithoutOverwritingAnInvalidSettingsFile()
    {
        InTemporaryDirectory(() =>
        {
            const string InvalidJson = """{"SettingsVersion":""";
            File.WriteAllText(SettingsHandler.SettingsFileName, InvalidJson);
            var handler = new SettingsHandler();

            Assert.Equal(Settings.CurrentSettingsVersion, handler.Settings.SettingsVersion);
            Assert.True(handler.Settings.CheckForNewVersions);
            Assert.Equal(InvalidJson, File.ReadAllText(SettingsHandler.SettingsFileName));

            handler.Settings.ExcludeFromNotifying.Add(new UniqueStreamKey("twitchtv", "42"));

            Assert.Equal(new UniqueStreamKey("twitchtv", "42"), Assert.Single(handler.Settings.ExcludeFromNotifying));
            Assert.Equal(new UniqueStreamKey("twitchtv", "42"), Assert.Single(ReadSavedSettings().ExcludeFromNotifying));
        });
    }

    [Fact]
    public void ReadASettingsFileThatAnotherInstanceBrieflyHoldsOpen()
    {
        InTemporaryDirectory(() =>
        {
            var json = $$"""{"SettingsVersion":{{Settings.CurrentSettingsVersion}},"MinimumEventViewers":123}""";
            File.WriteAllText(SettingsHandler.SettingsFileName, json);
            // Held exclusively, as while another instance saves it; the timer releases it well before the retry.
            var heldFile = new FileStream(SettingsHandler.SettingsFileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            const int ReleaseAfterMilliseconds = 20;
            using var release = new Timer(_ => heldFile.Dispose(), null, ReleaseAfterMilliseconds, Timeout.Infinite);

            Assert.Equal(123, new SettingsHandler().Settings.MinimumEventViewers);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n")]
    public async Task LoadNoChannelsFromAnEmptyLivestreamsFile(string content)
    {
        await InTemporaryDirectory(async () =>
        {
            await File.WriteAllTextAsync("livestreams.json", content);
            var fileHandler = new MonitoredStreamsFileHandler(Substitute.For<IApiClientFactory>());

            Assert.Empty(await fileHandler.LoadFromDisk());
        });
    }

    [Theory]
    [InlineData("""{"FileVersion":2,"LivestreamFileEntries":[{"LivestreamId":"42",""")]
    [InlineData("""{"FileVersion":2,"LivestreamFileEntries":[{"LivestreamId":"42","StreamProvider":"unknown"}]}""")]
    // Upgraded and backed up during the load, then unreadable; only that first backup of the original is kept.
    [InlineData("""{"FileVersion":1,"LivestreamFileEntries":[{"LivestreamId":"42","StreamProvider":"unknown"}]}""")]
    public async Task BackUpAnUnreadableLivestreamsFileBeforeReplacingIt(string unreadable)
    {
        await InTemporaryDirectory(async () =>
        {
            await File.WriteAllTextAsync("livestreams.json", unreadable);
            var factory = Substitute.For<IApiClientFactory>();
            factory.GetByName(Arg.Any<string>()).Returns((IApiClient)null);
            var fileHandler = new MonitoredStreamsFileHandler(factory);
            await Assert.ThrowsAnyAsync<Exception>(fileHandler.LoadFromDisk);

            var provider = Substitute.For<IApiClient>();
            provider.ApiName.Returns("twitchtv");
            fileHandler.SaveToDisk([new ChannelIdentifier(provider, "7")]);

            var backup = Assert.Single(Directory.GetFiles(".", "livestreams.json_*.bak"));
            Assert.Equal(unreadable, await File.ReadAllTextAsync(backup));
            var saved = JsonSerializer.Deserialize<LivestreamFileData>(await File.ReadAllTextAsync("livestreams.json"), JsonDefaults.Options);
            Assert.Equal("7", Assert.Single(saved.LivestreamFileEntries).ChannelId);
        });
    }

    [Theory]
    [InlineData("twitchtv", "Known_Login", "known_login", false)]
    [InlineData("twitchtv", "한동숙", "known_login", true)]
    [InlineData("twitchtv", "OldName", "known_login", true)]
    [InlineData("twitchtv", "Known_Login", null, false)]
    [InlineData("kick", "한동숙", "known_login", false)]
    public async Task SaveOnlyTwitchLoginsThatDifferFromTheDisplayName(
        string providerName,
        string displayName,
        string login,
        bool shouldStore)
    {
        await InTemporaryDirectory(async () =>
        {
            var provider = Substitute.For<IApiClient>();
            provider.ApiName.Returns(providerName);
            var factory = Substitute.For<IApiClientFactory>();
            factory.GetByName(providerName).Returns(provider);
            var channel = new ChannelIdentifier(provider, "42") { DisplayName = displayName, TwitchLogin = login };
            var fileHandler = new MonitoredStreamsFileHandler(factory);

            fileHandler.SaveToDisk([channel]);

            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync("livestreams.json"));
            var entry = saved.RootElement.GetProperty("LivestreamFileEntries")[0];
            Assert.Equal(shouldStore, entry.TryGetProperty("TwitchLogin", out var savedLogin));
            if (shouldStore) Assert.Equal(login, savedLogin.GetString());

            var loaded = Assert.Single(await new MonitoredStreamsFileHandler(factory).LoadFromDisk());
            Assert.Equal(shouldStore ? login : null, loaded.TwitchLogin);
            Assert.Equal(displayName, loaded.DisplayName);

            loaded.DisplayName = login ?? displayName;
            fileHandler.SaveToDisk([loaded]);

            Assert.DoesNotContain("\"TwitchLogin\"", await File.ReadAllTextAsync("livestreams.json"));
        });
    }

    [Fact]
    public async Task ReadOlderLivestreamEntriesWithoutATwitchLogin()
    {
        await InTemporaryDirectory(async () =>
        {
            const string Json = """{"FileVersion":2,"LivestreamFileEntries":[{"LivestreamId":"42","StreamProvider":"twitchtv","DisplayName":"Channel"}]}""";
            await File.WriteAllTextAsync("livestreams.json", Json);
            var provider = Substitute.For<IApiClient>();
            var factory = Substitute.For<IApiClientFactory>();
            factory.GetByName("twitchtv").Returns(provider);

            var channel = Assert.Single(await new MonitoredStreamsFileHandler(factory).LoadFromDisk());

            Assert.Equal("Channel", channel.DisplayName);
            Assert.Null(channel.TwitchLogin);
        });
    }

    [Fact]
    public void KeepNonAsciiAndSymbolsReadableInSavedFiles()
    {
        const string Args = "--title \"Jürgen & ゲーム's <stream>\"";
        var settings = new Settings();
        settings.Twitch.StreamCommand.Args = Args;

        var saved = JsonSerializer.Serialize(settings, JsonDefaults.IndentedOptions);

        Assert.Contains("Jürgen & ゲーム's <stream>", saved);
        Assert.Equal(Args, JsonSerializer.Deserialize<Settings>(saved, JsonDefaults.Options).Twitch.StreamCommand.Args);
    }

    [Fact]
    public void PreservePreviouslyReadSettingsWhenLaterValuesAreNull()
    {
        const string Json = """
            {"MinimumEventViewers":123,"minimumeventviewers":null,
             "FavoriteApiQualities":{"twitchtv":{"FallbackQuality":"Worst"}},"FavoriteApiQualities":null,
             "ExcludeFromNotifying":[{"ApiClientName":"twitchtv","StreamId":"42"}],"ExcludeFromNotifying":null}
            """;
        var settings = JsonSerializer.Deserialize<Settings>(Json, JsonDefaults.Options);
        Assert.Equal(123, settings.MinimumEventViewers);
        Assert.Equal("Worst", settings.FavoriteApiQualities["twitchtv"].FallbackQuality);
        Assert.Equal(new UniqueStreamKey("twitchtv", "42"), Assert.Single(settings.ExcludeFromNotifying));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"DebugMode\":null}")]
    public void RejectInvalidSettingsValues(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Settings>(json, JsonDefaults.Options));
    }

    [Fact]
    public void PreserveCommandsTokensPreferencesAndNotificationExclusions()
    {
        const string Json = """
            {
              "SettingsVersion":4,
              "MinimumEventViewers":0,
              "MetroThemeBaseColour":1,
              "MetroThemeAccentColour":3,
              "DisableNotifications":true,
              "Twitch":{"AuthToken":"retained-token","PassthroughClientId":true,
                "StreamCommand":{"FilePath":"player","Args":"{url} {qualities} {auth_token}","CaptureStandardOutput":true}},
              "FavoriteApiQualities":{"twitchtv":{"Qualities":["1080p60","720p"],"FallbackQuality":"Worst"}},
              "ExcludeFromNotifying":[{"ApiClientName":"twitchtv","StreamId":"42"}]
            }
            """;
        var settings = JsonSerializer.Deserialize<Settings>(Json, JsonDefaults.Options);
        var roundTrip = JsonSerializer.Deserialize<Settings>(
            JsonSerializer.Serialize(settings, JsonDefaults.IndentedOptions), JsonDefaults.Options);
        Assert.Equal(0, roundTrip.MinimumEventViewers);
        Assert.True(roundTrip.DisableNotifications);
        Assert.Equal(settings.MetroThemeAccentColour, roundTrip.MetroThemeAccentColour);
        Assert.Equal(settings.MetroThemeBaseColour, roundTrip.MetroThemeBaseColour);
        Assert.Equal("retained-token", roundTrip.Twitch.AuthToken);
        Assert.True(roundTrip.Twitch.PassthroughClientId);
        Assert.Equal("player", roundTrip.Twitch.StreamCommand.FilePath);
        Assert.Equal("{url} {qualities} {auth_token}", roundTrip.Twitch.StreamCommand.Args);
        Assert.True(roundTrip.Twitch.StreamCommand.CaptureStandardOutput);
        Assert.Equal(new[] { "1080p60", "720p" }, roundTrip.FavoriteApiQualities["twitchtv"].Qualities);
        Assert.Equal("Worst", roundTrip.FavoriteApiQualities["twitchtv"].FallbackQuality);
        Assert.Equal(new UniqueStreamKey("twitchtv", "42"), Assert.Single(roundTrip.ExcludeFromNotifying));
    }

    [Fact]
    public void ReadLegacyNotificationExclusions()
    {
        const string Json = """{"ExcludeFromNotifying":["42",123]}""";
        var settings = JsonSerializer.Deserialize<Settings>(Json, JsonDefaults.Options);
        Assert.Collection(settings.ExcludeFromNotifying,
            key => Assert.Equal(new UniqueStreamKey("twitchtv", "42"), key),
            key => Assert.Equal(new UniqueStreamKey("twitchtv", "123"), key));
    }

    [Fact]
    public void KeepDefaultsWhenFieldsAreAbsent()
    {
        var settings = JsonSerializer.Deserialize<Settings>("{}", JsonDefaults.Options);
        Assert.Equal(Settings.DefaultMinimumPopularEventViewers, settings.MinimumEventViewers);
        Assert.Equal("streamlink", settings.Twitch.StreamCommand.FilePath);
        Assert.Empty(settings.FavoriteApiQualities);
        Assert.Empty(settings.ExcludeFromNotifying);
    }

    [Fact]
    public void PreserveTheSavedChannelIdentifierContract()
    {
        const string Json = """
            {"FileVersion":2,"LivestreamFileEntries":[{"LivestreamId":"42","StreamProvider":"twitchtv","DisplayName":"Channel","ImportedBy":"account"}]}
            """;
        var data = JsonSerializer.Deserialize<LivestreamFileData>(Json, JsonDefaults.Options);
        var channel = Assert.Single(data.LivestreamFileEntries);
        Assert.Equal("42", channel.ChannelId);
        Assert.Equal("twitchtv", channel.StreamProvider);
        Assert.Equal("account", channel.ImportedBy);
        var saved = JsonSerializer.Serialize(data, JsonDefaults.Options);
        using var document = JsonDocument.Parse(saved);
        Assert.Equal("42", document.RootElement.GetProperty("LivestreamFileEntries")[0].GetProperty("LivestreamId").GetString());
    }

    [Theory]
    [InlineData("""{"StreamProvider":"twitchtv"}""")]
    [InlineData("""{"LivestreamId":"42"}""")]
    public void RejectChannelsMissingRequiredIdentity(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LivestreamFileEntry>(json, JsonDefaults.Options));
    }

    [Fact]
    public async Task BackUpALivestreamsFileThatCouldNotBeOpenedBeforeReplacingIt()
    {
        await InTemporaryDirectory(async () =>
        {
            const string Json = """{"FileVersion":2,"LivestreamFileEntries":[{"LivestreamId":"42","StreamProvider":"twitchtv"}]}""";
            await File.WriteAllTextAsync("livestreams.json", Json);
            var fileHandler = new MonitoredStreamsFileHandler(Substitute.For<IApiClientFactory>());

            await using (new FileStream("livestreams.json", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                await Assert.ThrowsAsync<IOException>(fileHandler.LoadFromDisk);

            var provider = Substitute.For<IApiClient>();
            provider.ApiName.Returns("twitchtv");
            fileHandler.SaveToDisk([new ChannelIdentifier(provider, "7")]);

            var backup = Assert.Single(Directory.GetFiles(".", "livestreams.json_*.bak"));
            Assert.Equal(Json, await File.ReadAllTextAsync(backup));
        });
    }

    [Fact]
    public async Task UpgradeAVersion0LivestreamsFileInOneLoadKeepingOneBackupOfTheOriginal()
    {
        await InTemporaryDirectory(async () =>
        {
            // The original format was a bare array; without Twitch entries its upgrade makes no Twitch request.
            const string Version0Json = "[]";
            await File.WriteAllTextAsync("livestreams.json", Version0Json);
            var fileHandler = new MonitoredStreamsFileHandler(Substitute.For<IApiClientFactory>());

            Assert.Empty(await fileHandler.LoadFromDisk());

            var backup = Assert.Single(Directory.GetFiles(".", "livestreams.json_*.bak"));
            Assert.Equal(Version0Json, await File.ReadAllTextAsync(backup));
            var saved = JsonSerializer.Deserialize<LivestreamFileData>(await File.ReadAllTextAsync("livestreams.json"), JsonDefaults.Options);
            Assert.Equal(LivestreamFileData.CurrentFileVersion, saved.FileVersion);
        });
    }

    private static Settings ReadSavedSettings() =>
        JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsHandler.SettingsFileName), JsonDefaults.Options);

    // The settings and livestreams files are read from and written to the current directory.
    private static void InTemporaryDirectory(Action test) =>
        InTemporaryDirectory(() =>
        {
            test();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    private static async Task InTemporaryDirectory(Func<Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Livestream.Monitor.Settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = directory;
            await test();
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(directory, recursive: true);
        }
    }
}

[CollectionDefinition("Settings files", DisableParallelization = true)]
public class SettingsFileCollection;
