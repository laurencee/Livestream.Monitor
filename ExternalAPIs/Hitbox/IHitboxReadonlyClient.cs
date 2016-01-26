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

        /// <summary> Gets top games current on hitbox with an optional game name filter </summary>
        /// <param name="gameName">Optional value to filter games by</param>
        Task<List<Category>> GetTopGames(string gameName = null);
    }
}