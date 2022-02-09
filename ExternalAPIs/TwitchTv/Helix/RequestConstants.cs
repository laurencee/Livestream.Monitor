namespace ExternalAPIs.TwitchTv.Helix
{
    public static class RequestConstants
    {
        public const string ClientIdHeaderKey = "Client-ID";
        public const string ClientIdHeaderValue = "lf8xspujnqfqcdlj11zq77dfen2tqjo";

        public const string TwitchTvApiRoot = "https://api.twitch.tv/helix";
        public const string UserFollows = TwitchTvApiRoot + "/users/follows?from_id={0}";
        public const string Streams = TwitchTvApiRoot + "/streams";
        public const string SearchCategories = TwitchTvApiRoot + "/search/categories";
        public const string Games = TwitchTvApiRoot + "/games";
        public const string Videos = TwitchTvApiRoot + "/videos";
        public const string TopGames = TwitchTvApiRoot + "/games/top";
        public const string Users = TwitchTvApiRoot + "/users";
    }
}