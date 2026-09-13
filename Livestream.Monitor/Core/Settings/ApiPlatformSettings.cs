using System.Collections.Generic;
using Caliburn.Micro;
using Newtonsoft.Json;
using static ExternalAPIs.TwitchTv.Helix.RequestConstants;
using static Livestream.Monitor.Core.Settings;

namespace Livestream.Monitor.Core;

public abstract class ApiPlatformSettings : PropertyChangedBase
{
    public virtual IReadOnlyDictionary<string, string> GetReplacements(string url)
    {
        // every platform at least needs the URL token
        return new Dictionary<string, string>
        {
            { UrlReplacementToken, url }
        };
    }

    [JsonProperty]
    public ExecCommand StreamCommand { get; set; } = new();

    [JsonProperty]
    public ExecCommand VodCommand { get; set; } = new();

    [JsonProperty]
    public ExecCommand ChatCommand { get; set; } = new();
}

public class ExecCommand : PropertyChangedBase
{
    [JsonProperty]
    public string FilePath
    {
        get;
        set => Set(ref field, value);
    }

    [JsonProperty]
    public string Args
    {
        get;
        set => Set(ref field, value);
    }

    [JsonProperty]
    public bool CaptureStandardOutput
    {
        get;
        set => Set(ref field, value);
    }

    [JsonProperty]
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

    [JsonProperty]
    public string AuthToken
    {
        get;
        set
        {
            if (Set(ref field, value)) NotifyOfPropertyChange(nameof(IsAuthTokenSet));
        }
    }

    [JsonProperty]
    public bool PassthroughClientId
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>
    /// Flag to indicate if the twitch oauth token has been defined either in livestream monitor settings
    /// or in the livestreamer/streamlink configuration file
    /// </summary>
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