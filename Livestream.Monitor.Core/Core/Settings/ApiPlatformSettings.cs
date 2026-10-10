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
        StreamCommand = new ExecCommand
        {
            FilePath = "streamlink",
            Args = $"{UrlReplacementToken} best --twitch-api-header=Authorization=Bearer={{auth_token}}",
            CaptureStandardOutput = true,
            CaptureErrorOutput = true,
        };
        VodCommand = new ExecCommand
        {
            FilePath = "streamlink",
            Args = $"{UrlReplacementToken} best --twitch-api-header=Authorization=Bearer={{auth_token}}",
            CaptureStandardOutput = true,
            CaptureErrorOutput = true,
        };
        ChatCommand = new ExecCommand
        {
            FilePath = DefaultEdgePath,
            Args = UrlReplacementToken,
            CaptureStandardOutput = false,
            CaptureErrorOutput = false,
        };
        AuthToken = "";
    }
}

public class KickSettings : ApiPlatformSettings
{
    public KickSettings()
    {
        StreamCommand = new ExecCommand
        {
            FilePath = "streamlink",
            Args = $"{UrlReplacementToken} best",
            CaptureStandardOutput = true,
            CaptureErrorOutput = true,
        };
        VodCommand = new ExecCommand
        {
            FilePath = "streamlink",
            Args = $"{UrlReplacementToken} best",
            CaptureStandardOutput = true,
            CaptureErrorOutput = true,
        };
        ChatCommand = new ExecCommand
        {
            FilePath = DefaultEdgePath,
            Args = UrlReplacementToken,
            CaptureStandardOutput = false,
            CaptureErrorOutput = false,
        };
    }
}

public class YouTubeSettings : ApiPlatformSettings
{
    public YouTubeSettings()
    {
        StreamCommand = new ExecCommand
        {
            FilePath = "streamlink",
            Args = $"{UrlReplacementToken} best",
            CaptureErrorOutput = true,
            CaptureStandardOutput = true,
        };
        VodCommand = new ExecCommand
        {
            FilePath = "mpc-hc64",
            Args = UrlReplacementToken,
            CaptureStandardOutput = true,
            CaptureErrorOutput = true,
        };
        ChatCommand = new ExecCommand
        {
            FilePath = DefaultEdgePath,
            Args = UrlReplacementToken,
            CaptureStandardOutput = false,
            CaptureErrorOutput = false,
        };
    }
}
