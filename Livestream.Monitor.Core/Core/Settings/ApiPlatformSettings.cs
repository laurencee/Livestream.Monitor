using System.Collections.Generic;
using System.Text.Json.Serialization;
using static ExternalAPIs.TwitchTv.Helix.RequestConstants;
using static Livestream.Monitor.Core.Settings.Settings;

namespace Livestream.Monitor.Core.Settings;

public abstract class ApiPlatformSettings : ObservableModel
{
    public virtual IReadOnlyDictionary<string, string> GetReplacements(string url)
    {
        // every platform at least needs the URL token
        return new Dictionary<string, string>
        {
            { UrlReplacementToken, url }
        };
    }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ExecCommand StreamCommand { get; set; } = new();

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ExecCommand VodCommand { get; set; } = new();

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ExecCommand ChatCommand { get; set; } = new();

    protected static ExecCommand CreatePlayerCommand(string filePath, string args) => new()
    {
        FilePath = filePath,
        Args = args,
        CaptureStandardOutput = true,
        CaptureErrorOutput = true,
    };

    protected static ExecCommand CreateChatCommand() => new()
    {
        FilePath = DefaultEdgePath,
        Args = UrlReplacementToken,
        CaptureStandardOutput = false,
        CaptureErrorOutput = false,
    };
}

public class ExecCommand : ObservableModel
{
    public string FilePath
    {
        get;
        set => Set(ref field, value);
    }

    public string Args
    {
        get;
        set => Set(ref field, value);
    }

    public bool CaptureStandardOutput
    {
        get;
        set => Set(ref field, value);
    }

    public bool CaptureErrorOutput
    {
        get;
        set => Set(ref field, value);
    }
}

public class TwitchSettings : ApiPlatformSettings
{
    public override IReadOnlyDictionary<string, string> GetReplacements(string url)
    {
        var map = new Dictionary<string, string>
        {
            { UrlReplacementToken, url }
        };

        map["{auth_token}"] = AuthToken ?? "";

        if (PassthroughClientId)
        {
            map["{client_id_header}"] =
                $"--http-header {ClientIdHeaderKey}={ClientIdHeaderValue}";
        }

        return map;
    }

    public string AuthToken
    {
        get;
        set
        {
            if (Set(ref field, value)) NotifyOfPropertyChange(nameof(IsAuthTokenSet));
        }
    }

    public bool PassthroughClientId
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>
    /// Flag to indicate if the twitch oauth token has been defined either in livestream monitor settings
    /// or in the livestreamer/streamlink configuration file
    /// </summary>
    [JsonIgnore]
    public bool IsAuthTokenSet => !string.IsNullOrWhiteSpace(AuthToken);

    public TwitchSettings()
    {
        var args = $"{UrlReplacementToken} best --twitch-api-header=Authorization=Bearer={{auth_token}}";

        StreamCommand = CreatePlayerCommand("streamlink", args);
        VodCommand = CreatePlayerCommand("streamlink", args);
        ChatCommand = CreateChatCommand();
        AuthToken = "";
    }
}

public class KickSettings : ApiPlatformSettings
{
    public KickSettings()
    {
        var args = $"{UrlReplacementToken} best";

        StreamCommand = CreatePlayerCommand("streamlink", args);
        VodCommand = CreatePlayerCommand("streamlink", args);
        ChatCommand = CreateChatCommand();
    }
}

public class YouTubeSettings : ApiPlatformSettings
{
    public YouTubeSettings()
    {
        var args = $"{UrlReplacementToken} best";

        StreamCommand = CreatePlayerCommand("streamlink", args);
        VodCommand = CreatePlayerCommand("mpc-hc64", UrlReplacementToken);
        ChatCommand = CreateChatCommand();
    }
}
