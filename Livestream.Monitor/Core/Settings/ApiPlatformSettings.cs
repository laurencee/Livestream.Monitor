using Caliburn.Micro;
using Newtonsoft.Json;
using static Livestream.Monitor.Core.Settings;

namespace Livestream.Monitor.Core;

public abstract class ApiPlatformSettings : PropertyChangedBase
{
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
}

public class TwitchSettings : ApiPlatformSettings
{
    private string authToken;
    private bool passthroughClientId;

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
            Args = $"{UrlReplacementToken} best --twitch-disable-ads --twitch-api-header=Authorization=Bearer {{access_token}}",
            CaptureStandardOutput = true,
        };
        VodCommand = new ExecCommand
        {
            FilePath = "streamlink",
            Args = $"{UrlReplacementToken} best --twitch-api-header=Authorization=Bearer {{access_token}}",
            CaptureStandardOutput = true,
        };
        ChatCommand = new ExecCommand
        {
            FilePath = "chat_replay_downloader",
            Args = $"{UrlReplacementToken} --twitch-client-id your_client_id --twitch-access-token {{access_token}}",
            CaptureStandardOutput = false,
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
        };
        VodCommand = new ExecCommand
        {
            FilePath = "streamlink",
            Args = $"{UrlReplacementToken} best",
            CaptureStandardOutput = true,
        };
        ChatCommand = new ExecCommand
        {
            FilePath = "kick_chat_tool",
            Args = $"{UrlReplacementToken}",
            CaptureStandardOutput = false,
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
            CaptureStandardOutput = true,
        };
        VodCommand = new ExecCommand
        {
            FilePath = "vlc",
            Args = $"{UrlReplacementToken}",
            CaptureStandardOutput = true,
        };
        ChatCommand = new ExecCommand
        {
            FilePath = "yt_chat_tool",
            Args = $"{UrlReplacementToken}",
            CaptureStandardOutput = false,
        };
    }
}