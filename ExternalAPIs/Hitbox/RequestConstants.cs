namespace ExternalAPIs.Hitbox
{
    public static class RequestConstants
    {
        public const string HitboxApiRoot = @"https://api.hitbox.tv";
        public const string TopStreams = HitboxApiRoot + "/media/live/list";
        public const string Streams = HitboxApiRoot + "/mediainfo/live";
        public const string UserFollows = HitboxApiRoot + "/following/user?user_name={0}";
        public const string Games = HitboxApiRoot + "/games";
        public const string ChannelVideos = HitboxApiRoot + "/media/video/{0}/list";
        public const string LiveChannel = HitboxApiRoot + "/media/live/{0}";
    }
}
