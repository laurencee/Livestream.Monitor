namespace TwitchTv
{
    public static class RequestConstants
    {
        public const string AcceptHeader = "application/vnd.twitchtv.v3+json";
        public const string TwitchTvRootApi = "https://api.twitch.tv/kraken";
        public const string UserFollows = TwitchTvRootApi + "/users/{0}/follows/channels";
        public const string StreamDetails = TwitchTvRootApi + "/streams/{0}";
        public const string ChannelDetails = TwitchTvRootApi + "/channels/{0}";
    }
}