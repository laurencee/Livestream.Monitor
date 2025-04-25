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
    private string filePath;
    private string args;
    private bool captureStandardOutput;
    private bool captureErrorOutput;

    [JsonProperty]
    public string FilePath
    {
        get => filePath;
        set => Set(ref filePath, value);
    }

    [JsonProperty]
    public string Args
    {
        get => args;
        set => Set(ref args, value);
    }

    [JsonProperty]
    public bool CaptureStandardOutput
    {
        get => captureStandardOutput;
        set => Set(ref captureStandardOutput, value);
    }

    [JsonProperty]
    public bool CaptureErrorOutput
    {
        get => captureErrorOutput;
        set => Set(ref captureErrorOutput, value);
    }
}

public class TwitchSettings : ApiPlatformSettings
{
    private string authToken;
    private bool passthroughClientId;

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
        get => authToken;
        set
        {
            if (Set(ref authToken, value)) NotifyOfPropertyChange(nameof(IsAuthTokenSet));
        }
    }

    [JsonProperty]
    public bool PassthroughClientId
    {
        get => passthroughClientId;
        set => Set(ref passthroughClientId, value);
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