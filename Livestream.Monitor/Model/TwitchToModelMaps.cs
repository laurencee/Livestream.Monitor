using System;
using TwitchTv.Dto;

namespace Livestream.Monitor.Model
{
    public static class TwitchToModelMaps
    {
        public static ChannelData ToChannelData(this Channel channel)
        {
            return new ChannelData()
            {
                ChannelName = channel.Name,
                ChannelDescription = channel.Status,
                Game = channel.Game,
            };
        }

        public static ChannelData ToChannelData(this ChannelFileData channelFileData)
        {
            return new ChannelData()
            {
                ChannelName = channelFileData.ChannelName,
                ImportedBy = channelFileData.ImportedBy
            };
        }

        public static void PopulateWithStreamDetails(this ChannelData channelData, Stream streamDetails)
        {
            if (streamDetails == null) return;

            channelData.Live = streamDetails.Viewers.HasValue;
            channelData.Viewers = streamDetails.Viewers ?? 0;
            if (streamDetails.CreatedAt != null)
            {
                channelData.StartTime = DateTimeOffset.Parse(streamDetails.CreatedAt);
                channelData.NotifyOfPropertyChange(nameof(channelData.Uptime));
            }
        }

        public static void PopulateWithChannel(this ChannelData channelData, Channel channel)
        {
            if (channel == null) return;

            channelData.ChannelDescription = channel.Status;
            channelData.Game = channel.Game;
        }
    }
}
