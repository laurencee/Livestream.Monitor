using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Hitbox.Dto;
using ExternalAPIs.Hitbox.Dto.QueryRoot;
using ExternalAPIs.Hitbox.Query;
using static System.String;

namespace ExternalAPIs.Hitbox
{
    public class HitboxReadonlyClient : IHitboxReadonlyClient
    {
        public async Task<List<Livestream>> GetTopStreams(TopStreamsQuery topStreamsQuery, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (topStreamsQuery == null) throw new ArgumentNullException(nameof(topStreamsQuery));

            var request = $"{RequestConstants.TopStreams}?start={topStreamsQuery.Skip}&limit={topStreamsQuery.Take}";
            if (!IsNullOrWhiteSpace(topStreamsQuery.GameName))
                request += $"&game={topStreamsQuery.GameName}";

            var streamRoot = await HttpClientExtensions.ExecuteRequest<StreamsRoot>(request, cancellationToken);
            return streamRoot.Livestreams;
        }

        public async Task<Mediainfo> GetStreamDetails(string streamId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (IsNullOrWhiteSpace(streamId)) throw new ArgumentNullException(nameof(streamId));

            var request = $"{RequestConstants.Streams}/{streamId}";
            var streamRoot = await HttpClientExtensions.ExecuteRequest<StreamRoot>(request, cancellationToken);
            return streamRoot.Mediainfo;
        }

        public async Task<Livestream> GetChannelDetails(string channelName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (IsNullOrWhiteSpace(channelName)) throw new ArgumentNullException(nameof(channelName));

            var request = RequestConstants.LiveChannel.Replace("{0}", channelName);
            var channelDetails = await HttpClientExtensions.ExecuteRequest<ChannelRoot>(request, cancellationToken);
            return channelDetails.Livestreams.FirstOrDefault();
        }

        public async Task<List<Video>> GetChannelVideos(ChannelVideosQuery channelVideosQuery, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (channelVideosQuery == null) throw new ArgumentNullException(nameof(channelVideosQuery));
            if (IsNullOrWhiteSpace(channelVideosQuery.ChannelName)) throw new ArgumentNullException(nameof(channelVideosQuery.ChannelName));

            var request = RequestConstants.ChannelVideos.Replace("{0}", channelVideosQuery.ChannelName);
            // hitbox doesn't actually have any offset/start value specified in their documentation 
            // so maybe they dont have any paging available through the api: http://developers.hitbox.tv/#list-videos
            request += $"?offset={channelVideosQuery.Skip}&limit={channelVideosQuery.Take}";

            var channelVideosRoot = await HttpClientExtensions.ExecuteRequest<ChannelVideosRoot>(request, cancellationToken);
            return channelVideosRoot.Videos;
        }

        public async Task<List<Following>> GetUserFollows(string username, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (IsNullOrWhiteSpace(username)) throw new ArgumentNullException(nameof(username));

            var request = $"{RequestConstants.UserFollows.Replace("{0}", username)}";
            var userFollows = await HttpClientExtensions.ExecuteRequest<UserFollowsRoot>(request, cancellationToken);
            // if necessary, page until we get all followed streams
            while (userFollows.MaxResults > 0 && userFollows.Following.Count < userFollows.MaxResults)
            {
                var pagedRequest = $"{request}&offset={userFollows.Following.Count}";
                var pagedFollows = await HttpClientExtensions.ExecuteRequest<UserFollowsRoot>(pagedRequest, cancellationToken);
                userFollows.Following.AddRange(pagedFollows.Following);
            }

            return userFollows.Following;
        }

        public async Task<List<Category>> GetTopGames(string gameName = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var request = $"{RequestConstants.Games}?liveonly=true";
            if (gameName != null)
                request += $"&q={gameName}";

            var topGamesRoot = await HttpClientExtensions.ExecuteRequest<TopGamesRoot>(request, cancellationToken);

            return topGamesRoot.Categories;
        }
    }
}
