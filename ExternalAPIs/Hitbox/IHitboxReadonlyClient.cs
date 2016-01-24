using System.Collections.Generic;
using System.Threading.Tasks;
using ExternalAPIs.Hitbox.Dto;
using ExternalAPIs.Hitbox.Query;

namespace ExternalAPIs.Hitbox
{
    public interface IHitboxReadonlyClient
    {
        Task<List<Livestream>> GetTopStreams(TopStreamsQuery topStreamsQuery);

        Task<Mediainfo> GetStreamDetails(string streamId);

        Task<Livestream> GetChannelDetails(string channelName);

        Task<List<Video>> GetChannelVideos(ChannelVideosQuery channelVideosQuery);

        Task<List<Following>> GetUserFollows(string username);
    }
}