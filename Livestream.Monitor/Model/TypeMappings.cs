using System;
using ExternalAPIs.TwitchTv.Dto;

namespace Livestream.Monitor.Model
{
    public static class TypeMappings
    {
        public static LivestreamModel PopulateWithStreamDetails(
            this LivestreamModel livestreamModel, 
            Stream streamDetails)
        {
            if (streamDetails == null) return livestreamModel;
            
            livestreamModel.Viewers = streamDetails.Viewers ?? 0;
            livestreamModel.ThumbnailUrls = new ThumbnailUrls()
            {
                Large = streamDetails.Preview.Large,
                Medium = streamDetails.Preview.Medium,
                Small = streamDetails.Preview.Small,
            };
            if (streamDetails.CreatedAt != null)
            {
                livestreamModel.StartTime = DateTimeOffset.Parse(streamDetails.CreatedAt);
                livestreamModel.NotifyOfPropertyChange(nameof(livestreamModel.Uptime));
            }

            // need to update other details before flipping the stream to online
            livestreamModel.Live = streamDetails.Viewers.HasValue;

            if (streamDetails.Channel != null)
                livestreamModel.PopulateWithChannel(streamDetails.Channel);

            return livestreamModel;
        }

        public static void PopulateWithChannel(this LivestreamModel livestreamModel, Channel channel)
        {
            if (channel == null) return;

            livestreamModel.DisplayName = channel.DisplayName;
            livestreamModel.Description = channel.Status;
            livestreamModel.Game = channel.Game;
            livestreamModel.IsPartner = channel.Partner.HasValue && channel.Partner.Value;
            livestreamModel.BroadcasterLanguage = channel.BroadcasterLanguage;
            livestreamModel.Language = channel.Language;
        }

        public static void PopulateSelf(this LivestreamModel livestreamModel, LivestreamModel consume)
        {
            livestreamModel.BroadcasterLanguage = consume.BroadcasterLanguage;
            livestreamModel.Description = consume.Description;
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

