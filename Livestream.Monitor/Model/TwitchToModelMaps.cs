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
    }
}
