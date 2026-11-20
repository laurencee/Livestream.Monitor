namespace Livestream.Monitor.Model.Monitoring
{
    public sealed class LivestreamNotification
    {
        internal static LivestreamNotification CreateForFollowedStream(LivestreamModel stream, string newCategory = null)
        {
            var title = newCategory == null ? $"{stream.DisplayName} Online" : $"{stream.DisplayName} - {newCategory}";
            return new LivestreamNotification
            {
                Title = title,
                Message = stream.Description,
                ImageUrl = stream.ThumbnailUrls?.Small,
                LivestreamModel = stream,
            };
        }

        public string Message { get; init; }
        public string ImageUrl { get; init; }
        public string Title { get; init; }
        public LivestreamModel LivestreamModel { get; init; }
    }
}
