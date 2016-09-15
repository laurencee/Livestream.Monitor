namespace ExternalAPIs.TwitchTv
{
    public static class RequestConstants
    {
        public const string AcceptHeader = "application/vnd.twitchtv.v3+json";
        public const string ClientIdHeaderKey = "Client-ID";
        public const string ClientIdHeaderValue = "lf8xspujnqfqcdlj11zq77dfen2tqjo";

        public const string TwitchTvApiRoot = "https://api.twitch.tv/kraken";
        public const string UserFollows = TwitchTvApiRoot + "/users/{0}/follows/channels";
        public const string Streams = TwitchTvApiRoot + "/streams";
        public const string Channels = TwitchTvApiRoot + "/channels";
        public const string ChannelVideos = Channels + "/{0}/videos";
        public const string TopGames = TwitchTvApiRoot + "/games/top";
        public const string SearchStreams = TwitchTvApiRoot + "/search/streams?q={0}&type=suggest";
        public const string SearchGames = TwitchTvApiRoot + "/search/games?q={0}&type=suggest";
    }
}