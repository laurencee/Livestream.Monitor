using ExternalAPIs.TwitchTv.Helix.Dto;

namespace Livestream.Monitor.Model
{
    public static class TypeMappings
    {
        public static LivestreamModel PopulateWithStreamDetails(
            this LivestreamModel livestreamModel, 
            Stream stream)
        {
            if (stream == null) return livestreamModel;
            
            livestreamModel.Viewers = stream.ViewerCount;
            // the values here are what the v5 api returned for large/medium/small
            var largeThumbnail = stream.ThumbnailTemplateUrl.Replace("{width}", "640").Replace("{height}", "360");
            var mediumThumbnail = stream.ThumbnailTemplateUrl.Replace("{width}", "320").Replace("{height}", "180");
            var smallThumbnail = stream.ThumbnailTemplateUrl.Replace("{width}", "80").Replace("{height}", "45");
            livestreamModel.ThumbnailUrls = new ThumbnailUrls()
            {
                Large = largeThumbnail,
                Medium = mediumThumbnail,
                Small = smallThumbnail,
            };
            livestreamModel.StartTime = stream.StartedAt;

            // need to update other details before flipping the stream to online
            livestreamModel.Live = stream.Type == "live";

            livestreamModel.DisplayName = stream.UserName;
            livestreamModel.Description = stream.Title;
            livestreamModel.BroadcasterLanguage = stream.Language;
            livestreamModel.Language = stream.Language;

            return livestreamModel;
        }

        public static void PopulateSelf(this LivestreamModel livestreamModel, LivestreamModel consume)
        {
            livestreamModel.BroadcasterLanguage = consume.BroadcasterLanguage;
            livestreamModel.Description = consume.Description?.Trim();
            livestreamModel.DisplayName = consume.DisplayName;
            livestreamModel.Game = consume.Game;
            livestreamModel.IsPartner = consume.IsPartner;
            livestreamModel.Language = consume.Language;
            livestreamModel.ThumbnailUrls = consume.ThumbnailUrls;

            livestreamModel.Viewers = consume.Viewers;
            livestreamModel.StartTime = consume.StartTime;
            livestreamModel.Live = consume.Live;
        }

        public static UniqueStreamKey ToExcludeNotify(this LivestreamModel livestreamModel)
        {
            return new UniqueStreamKey(livestreamModel.ApiClient.ApiName, livestreamModel.Id);
        }
    }
}

