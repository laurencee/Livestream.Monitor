namespace ExternalAPIs.Youtube
{
    public static class RequestConstants
    {
        public const string API_KEY = "AIzaSyCUwXCzsbfhrGDfpo1i5RGzp4768xM044I";
        public const string GoogleApiRoot = "https://www.googleapis.com/youtube/v3/";

        public const string VideoLivestreamDetails = GoogleApiRoot + "videos?part=LiveStreamingDetails&key=" + API_KEY;
        public const string VideoSnippet = GoogleApiRoot + "videos?part=Snippet&key=" + API_KEY;
        public const string SearchChannelLiveVideos = GoogleApiRoot + "search?type=video&eventType=live&part=snippet,id&channelId={0}&key=" + API_KEY;
        public const string GetChannelIdByHandle = GoogleApiRoot + "channels?forHandle={0}&part=id&part=snippet&key=" + API_KEY;
    }
}