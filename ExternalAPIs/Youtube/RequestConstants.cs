namespace ExternalAPIs.Youtube
{
    public static class RequestConstants
    {
        public const string API_KEY = "AIzaSyCUwXCzsbfhrGDfpo1i5RGzp4768xM044I";
        public const string GoogleApiRoot = "https://www.googleapis.com/youtube/v3/";

        // fileDetails are only available to the video owner so we can't use that
        public const string VideoLivestreamDetails = GoogleApiRoot + "videos?part=liveStreamingDetails,snippet,contentDetails,statistics&key=" + API_KEY;
        public const string SearchChannelLiveVideos = GoogleApiRoot + "search?type=video&eventType=live&part=snippet,id&channelId={0}&key=" + API_KEY;
        public const string GetChannelByHandle = GoogleApiRoot + "channels?forHandle={0}&part=id,snippet,contentDetails&key=" + API_KEY;
        public const string GetChannels = GoogleApiRoot + "channels?part=id,snippet,contentDetails&key=" + API_KEY;
        public const string GetPlaylistItems = GoogleApiRoot + "playlistItems?playlistId={0}&part=id,snippet,contentDetails,status&key=" + API_KEY;
    }
}