namespace Livestream.Monitor.Model.ApiClients
{
    public class KnownGame
    {
        public string GameName { get; set; }

        public int TotalViewers { get; set; }

        public ThumbnailUrls ThumbnailUrls { get; set; }
    }
}