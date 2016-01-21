using System;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.Model.StreamProviders;
using TwitchTv.Dto;

namespace Livestream.Monitor.Model
{
    public static class TwitchToModelMaps
    {
        public static LivestreamModel PopulateWithStreamDetails(
            this LivestreamModel livestreamModel, 
            Stream streamDetails, 
            IStreamProvider twitchStreamProvider)
        {
            if (streamDetails == null) return livestreamModel;

            livestreamModel.Id = streamDetails.Channel?.Name;
            livestreamModel.StreamProvider = twitchStreamProvider;
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
    }
}

