using System;
using Livestream.Monitor.Model.Monitoring;
using TwitchTv.Dto;

namespace Livestream.Monitor.Model
{
    public static class TwitchToModelMaps
    {
        public static LivestreamModel ToLivestreamModel(this Stream stream)
        {
            var livestreamModel = new LivestreamModel();
            livestreamModel.PopulateWithStreamDetails(stream);
            return livestreamModel;
        }

        public static LivestreamModel ToLivestreamModel(this Follow follow, string importedBy = null)
        {
            return new LivestreamModel()
            {
                Id = follow.Channel?.Name,
                DisplayName = follow.Channel?.Name,
                Description = follow.Channel?.Status,
                Game = follow.Channel?.Game,
                IsPartner = follow.Channel?.Partner != null && follow.Channel.Partner.Value,
                ImportedBy = importedBy,
                StreamProvider = StreamProviders.TWITCH_STREAM_PROVIDER,
            };
        }

        public static LivestreamModel ToLivestreamModel(this LivestreamFileData livestreamFileData)
        {
            return new LivestreamModel()
            {
                Id = livestreamFileData.LivestreamId,
                StreamProvider = livestreamFileData.StreamProvider,
                ImportedBy = livestreamFileData.ImportedBy
            };
        }

        public static LivestreamFileData ToLivestreamFileData(this LivestreamModel channelFileData)
        {
            return new LivestreamFileData()
            {
                LivestreamId = channelFileData.Id,
                StreamProvider = channelFileData.StreamProvider,
                ImportedBy = channelFileData.ImportedBy
            };
        }

        public static void PopulateWithStreamDetails(this LivestreamModel livestreamModel, Stream streamDetails)
        {
            if (streamDetails == null) return;

            livestreamModel.Id = streamDetails.Channel?.Name;
            livestreamModel.StreamProvider = StreamProviders.TWITCH_STREAM_PROVIDER;
            livestreamModel.Viewers = streamDetails.Viewers ?? 0;
            livestreamModel.PreviewImage = streamDetails.Preview;
            if (streamDetails.CreatedAt != null)
            {
                livestreamModel.StartTime = DateTimeOffset.Parse(streamDetails.CreatedAt);
                livestreamModel.NotifyOfPropertyChange(nameof(livestreamModel.Uptime));
            }

            // need to update other details before flipping the stream to online
            livestreamModel.Live = streamDetails.Viewers.HasValue;

            if (streamDetails.Channel != null)
                livestreamModel.PopulateWithChannel(streamDetails.Channel);
        }

        public static void PopulateWithChannel(this LivestreamModel livestreamModel, Channel channel)
        {
            if (channel == null) return;

            livestreamModel.DisplayName = channel.DisplayName;
            livestreamModel.Description = channel.Status;
            livestreamModel.Game = channel.Game;
            livestreamModel.IsPartner = channel.Partner.HasValue && channel.Partner.Value;
        }
    }
}

