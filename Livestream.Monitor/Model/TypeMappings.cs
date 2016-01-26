using System;
using ExternalAPIs.TwitchTv.Dto;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model
{
    public static class TypeMappings
    {
        public static LivestreamModel PopulateWithStreamDetails(
            this LivestreamModel livestreamModel, 
            Stream streamDetails, 
            IApiClient twitchApiClient)
        {
            if (streamDetails == null) return livestreamModel;

            livestreamModel.Id = streamDetails.Channel?.Name;
            livestreamModel.ApiClient = twitchApiClient;
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

        public static ExcludeNotify ToExcludeNotify(this LivestreamModel livestreamModel)
        {
            return new ExcludeNotify(livestreamModel.ApiClient.ApiName, livestreamModel.Id);
        }
    }
}

