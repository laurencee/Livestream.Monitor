namespace ExternalAPIs.TwitchTv.V5
{
    public static class RequestConstants
    {
        public const string AcceptHeader = "application/vnd.twitchtv.v5+json";
        public const string ClientIdHeaderKey = "Client-ID";
        public const string ClientIdHeaderValue = "lf8xspujnqfqcdlj11zq77dfen2tqjo";

        public const string TwitchTvApiRoot = "https://api.twitch.tv/kraken";
        public const string TopGames = TwitchTvApiRoot + "/games/top";
        public const string SearchGames = TwitchTvApiRoot + "/search/games?query={0}";
    }
}