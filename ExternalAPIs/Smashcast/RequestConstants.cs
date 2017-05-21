namespace ExternalAPIs.Smashcast
{
    public static class RequestConstants
    {
        public const string SmashcastApiRoot = @"https://api.smashcast.tv";
        public const string TopStreams = SmashcastApiRoot + "/media/live/list";
        public const string Streams = SmashcastApiRoot + "/mediainfo/live";
        public const string UserFollows = SmashcastApiRoot + "/following/user?user_name={0}";
        public const string Games = SmashcastApiRoot + "/games";
        public const string ChannelVideos = SmashcastApiRoot + "/media/video/{0}/list";
        public const string LiveChannel = SmashcastApiRoot + "/media/live/{0}";
    }
}
