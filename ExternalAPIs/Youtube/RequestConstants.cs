namespace ExternalAPIs.Youtube
{
    public static class RequestConstants
    {
        public const string API_KEY = "QUl6YVN5Q1V3WEN6c2JmaHJHRGZwbzFpNVJHenA0NzY4eE0wNDRJ";
        public const string GoogleApiRoot = "https://www.googleapis.com/youtube/v3/";

        // fileDetails are only available to the video owner so we can't use that
        public const string VideoLivestreamDetails = GoogleApiRoot + "videos?part=liveStreamingDetails,snippet,contentDetails,statistics"; // maxResults not used for id lookup per api docs
        public const string SearchChannelLiveVideos = GoogleApiRoot + "search?type=video&eventType=live&part=snippet,id&maxResults=50&channelId={0}";
        public const string GetChannelByHandle = GoogleApiRoot + "channels?forHandle={0}&part=id,snippet,contentDetails";
        public const string GetChannels = GoogleApiRoot + "channels?part=id,snippet,contentDetails&maxResults=50";
        public const string GetPlaylistItems = GoogleApiRoot + "playlistItems?playlistId={0}&part=id,snippet,contentDetails,status"; // maxResults managed by PlaylistItemsQuery usage
    }
}