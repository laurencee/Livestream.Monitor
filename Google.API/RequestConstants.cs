namespace Google.API
{
    public static class RequestConstants
    {
        public const string API_KEY = "AIzaSyCUwXCzsbfhrGDfpo1i5RGzp4768xM044I";
        public const string GoogleApiRoot = "https://www.googleapis.com/youtube/v3/";
        public const string VideoLivestreamDetails = GoogleApiRoot + @"videos?part=LiveStreamingDetails&key=" + API_KEY;
        public const string VideoSnippet = GoogleApiRoot + @"videos?part=Snippet&key=" + API_KEY;
    }
}