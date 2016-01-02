namespace TwitchTv
{
    public static class RequestConstants
    {
        public const string AcceptHeader = "application/vnd.twitchtv.v3+json";
        public const string TwitchTvRootApi = "https://api.twitch.tv/kraken";
        public const string UserFollows = TwitchTvRootApi + "/users/{0}/follows/channels";
        public const string Streams = TwitchTvRootApi + "/streams";
        public const string Channels = TwitchTvRootApi + "/channels";
        public const string ChannelVideos = Channels + "/{0}/videos";
        public const string TopGames = TwitchTvRootApi + "/games/top";
        public const string SearchStreams = TwitchTvRootApi + "/search/streams?q={0}&type=suggest";
        public const string SearchGames = TwitchTvRootApi + "/search/games?q={0}&type=suggest";
    }
}