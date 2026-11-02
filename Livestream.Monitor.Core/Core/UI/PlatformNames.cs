using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Core.UI;

internal static class PlatformNames
{
    public static string GetDisplayName(string apiName) => apiName switch
    {
        TwitchApiClient.API_NAME => "Twitch",
        KickApiClient.API_NAME => "Kick",
        YoutubeApiClient.API_NAME => "YouTube",
        _ => apiName,
    };
}
