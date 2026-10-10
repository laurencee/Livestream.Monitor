using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Livestream.Monitor.Core.Settings;

namespace Livestream.Monitor.Tests.Ui;

/// <summary>
/// A throwaway data folder a UI test starts an application in, deleted on dispose. A profile with a token follows two
/// well-known Twitch channels, saved in the format of users' livestream files, and takes its Twitch token from the
/// required git-ignored twitchaccesstoken.local. As in ExternalAPIs.Tests, that file is created next to the test
/// project file, here from the committed twitchaccesstoken.local.example, and copied to the build output. The token is
/// never written to test output.
/// </summary>
internal sealed class UiTestProfile : IDisposable
{
    private const string TokenFileName = "twitchaccesstoken.local";
    private const string ExampleExtension = ".example";
    private const string FolderPrefix = "Livestream.Monitor.UiTest.";
    private const string StreamsFileName = "livestreams.json";
    private const int StreamsFileVersion = 2;
    private const string TwitchProvider = "twitchtv";

    // Twitch user ids, so a profile with a token queries channels that exist.
    private static readonly (string Id, string Name)[] FollowedStreams =
        [("37402112", "shroud"), ("51496027", "loltyler1")];

    // A profile left behind by an aborted test run may hold a token; it is removed once this old.
    private static readonly TimeSpan StaleAge = TimeSpan.FromHours(1);

    private UiTestProfile(string dataDirectory) => DataDirectory = dataDirectory;

    public string DataDirectory { get; }

    /// <summary>The display names of the followed channels, as saved in the profile.</summary>
    public static string[] FollowedChannels => FollowedStreams.Select(stream => stream.Name).ToArray();

    public string SettingsPath => Path.Combine(DataDirectory, SettingsHandler.SettingsFileName);

    /// <summary>A profile with the local token and followed streams.</summary>
    public static UiTestProfile Create()
    {
        var twitch = new JsonObject { ["AuthToken"] = ReadToken() };
        var settings = new JsonObject { ["Twitch"] = twitch };
        return Create(settings, followStreams: true);
    }

    /// <summary>A profile without a token or followed streams, so nothing asks for authorization at startup.</summary>
    public static UiTestProfile CreateWithoutToken() => Create([], followStreams: false);

    public void Dispose() => TryDelete(DataDirectory);

    // The profile skips the startup release check and refresh error dialogs, which could otherwise open over the
    // window under test at any moment.
    private static UiTestProfile Create(JsonObject settings, bool followStreams)
    {
        RemoveStaleProfiles();

        // The settings use the current format; unversioned settings are migrated from the first version, which
        // replaces the Twitch token with a legacy setting.
        settings["SettingsVersion"] = Settings.CurrentSettingsVersion;
        settings["CheckForNewVersions"] = false;
        settings["DisableRefreshErrorDialogs"] = true;

        var dataDirectory = Path.Combine(Path.GetTempPath(), FolderPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, SettingsHandler.SettingsFileName), settings.ToJsonString());
        if (followStreams) File.WriteAllText(Path.Combine(dataDirectory, StreamsFileName), FollowedStreamsJson());
        return new UiTestProfile(dataDirectory);
    }

    // Written by hand rather than with the application's types, so a renamed or reshaped property fails the tests as it
    // would fail to load users' files.
    private static string FollowedStreamsJson()
    {
        var entries = new JsonArray();

        foreach (var (id, name) in FollowedStreams)
        {
            var entry = new JsonObject
            {
                ["LivestreamId"] = id,
                ["DisplayName"] = name,
                ["StreamProvider"] = TwitchProvider,
            };

            entries.Add(entry);
        }

        var streams = new JsonObject { ["FileVersion"] = StreamsFileVersion, ["LivestreamFileEntries"] = entries };
        return streams.ToJsonString();
    }

    // The message names the file, never its content, which is a token. Surrounding whitespace, such as the final
    // newline an editor adds, is not part of the token. The build copies the file next to the tests, where it is read.
    private static string ReadToken()
    {
        var instructions =
            $"UI tests need a Twitch token. Copy {TokenFileName}{ExampleExtension} to {TokenFileName} next to the " +
            "test project file, replace its placeholder with the Twitch AuthToken from the settings.json of a " +
            "signed-in Livestream Monitor, and build the tests; the copy is git-ignored.";

        var path = Path.Combine(AppContext.BaseDirectory, TokenFileName);
        var token = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        if (token.Length == 0) throw new InvalidOperationException(instructions);

        // The example's text, left unreplaced.
        const string PlaceholderToken = "REPLACE_WITH_TWITCH_AUTH_TOKEN";
        if (token == PlaceholderToken) throw new InvalidOperationException(instructions);

        return token;
    }

    private static void RemoveStaleProfiles()
    {
        var staleBefore = DateTime.UtcNow - StaleAge;

        foreach (var folder in new DirectoryInfo(Path.GetTempPath()).EnumerateDirectories(FolderPrefix + "*"))
            if (folder.CreationTimeUtc < staleBefore)
                TryDelete(folder.FullName);
    }

    // A file still held open, for example by a virus scanner, leaves the folder for a later run to remove; the test's
    // own outcome stands.
    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
