using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.V3.Dto;
using ExternalAPIs.TwitchTv.V3.Dto.QueryRoot;
using ExternalAPIs.TwitchTv.V3.Query;

namespace ExternalAPIs.TwitchTv.V3
{
    public class TwitchTvReadonlyClient : ITwitchTvReadonlyClient
    {
        public const int DefaultItemsPerQuery = 100; // 25 is default, 100 is maximum

        public async Task<UserFollows> GetUserFollows(string username, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (String.IsNullOrWhiteSpace(username)) throw new ArgumentNullException(nameof(username));

            var request = $"{RequestConstants.UserFollows.Replace("{0}", username)}?limit={DefaultItemsPerQuery}";
            var userFollows = await ExecuteRequest<UserFollows>(request, cancellationToken);
            // if necessary, page until we get all followed streams
            while (userFollows.Total > 0 && userFollows.Follows.Count < userFollows.Total)
            {
                var pagedRequest = $"{request}&offset={userFollows.Follows.Count}";
                var pagedFollows = await ExecuteRequest<UserFollows>(pagedRequest, cancellationToken);
                userFollows.Follows.AddRange(pagedFollows.Follows);
            }
            return userFollows;
        }

        public async Task<Channel> GetChannelDetails(string streamName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (String.IsNullOrWhiteSpace(streamName)) throw new ArgumentNullException(nameof(streamName));

            var request = $"{RequestConstants.Channels}/{streamName}";
            var channelDetails = await ExecuteRequest<Channel>(request, cancellationToken);
            return channelDetails;
        }

        /// <summary> Gets the top streams </summary>
        public async Task<List<Stream>> GetTopStreams(TopStreamQuery topStreamQuery, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (topStreamQuery == null) throw new ArgumentNullException(nameof(topStreamQuery));

            var request = $"{RequestConstants.Streams}?offset={topStreamQuery.Skip}&limit={topStreamQuery.Take}";
            if (!String.IsNullOrWhiteSpace(topStreamQuery.GameName))
                request += $"&game={topStreamQuery.GameName}";

            var streamRoot = await ExecuteRequest<StreamsRoot>(request, cancellationToken);
            return streamRoot.Streams;
        }

        public async Task<Stream> GetStreamDetails(string streamName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (String.IsNullOrWhiteSpace(streamName)) throw new ArgumentNullException(nameof(streamName));

            var request = $"{RequestConstants.Streams}/{streamName}";
            var streamRoot = await ExecuteRequest<StreamRoot>(request, cancellationToken);
            return streamRoot.Stream;
        }

        public async Task<List<Stream>> GetStreamsDetails(IEnumerable<string> streamNames, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (streamNames == null) throw new ArgumentNullException(nameof(streamNames));
            
            var request = $"{RequestConstants.Streams}?channel={String.Join(",", streamNames)}&limit={DefaultItemsPerQuery}";
            var streamRoot = await ExecuteRequest<StreamsRoot>(request, cancellationToken);

            // if necessary, page until we get all followed streams
            while (streamRoot.Total > 0 && streamRoot.Streams.Count < streamRoot.Total)
            {
                var pagedRequest = $"{request}&offset={streamRoot.Streams.Count}";
                var pagedStreamsDetails = await ExecuteRequest<StreamsRoot>(pagedRequest, cancellationToken);
                streamRoot.Streams.AddRange(pagedStreamsDetails.Streams);
            }

            return streamRoot.Streams;
        }

        public async Task<List<TopGame>> GetTopGames(CancellationToken cancellationToken = default(CancellationToken))
        {
            var request = RequestConstants.TopGames;
            var gamesRoot = await ExecuteRequest<TopGamesRoot>(request, cancellationToken);
            return gamesRoot.TopGames;
        }

        public async Task<List<Stream>> SearchStreams(string streamName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (String.IsNullOrWhiteSpace(streamName)) throw new ArgumentNullException(nameof(streamName));

            var request = RequestConstants.SearchStreams.Replace("{0}", streamName);
            var streamsRoot = await ExecuteRequest<StreamsRoot>(request, cancellationToken);
            return streamsRoot.Streams;
        }

        public async Task<List<Game>> SearchGames(string gameName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (String.IsNullOrWhiteSpace(gameName)) throw new ArgumentNullException(nameof(gameName));

            var request = RequestConstants.SearchGames.Replace("{0}", gameName);
            var gamesRoot = await ExecuteRequest<GamesRoot>(request, cancellationToken);
            return gamesRoot.Games;
        }

        public async Task<List<Video>> GetChannelVideos(ChannelVideosQuery channelVideosQuery, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (channelVideosQuery == null) throw new ArgumentNullException(nameof(channelVideosQuery));
            if (String.IsNullOrWhiteSpace(channelVideosQuery.ChannelName)) throw new ArgumentNullException(nameof(channelVideosQuery.ChannelName));

            var request = RequestConstants.ChannelVideos.Replace("{0}", channelVideosQuery.ChannelName);
            request += $"?offset={channelVideosQuery.Skip}&limit={channelVideosQuery.Take}";
            if (channelVideosQuery.BroadcastsOnly)
                request += "&broadcasts=true";
            if (channelVideosQuery.HLSVodsOnly)
                request += "&hls=true";

            var channelVideosRoot = await ExecuteRequest<ChannelVideosRoot>(request, cancellationToken);
            return channelVideosRoot.Videos;
        }

        private Task<T> ExecuteRequest<T>(string request, CancellationToken cancellationToken = default(CancellationToken))
        {
            HttpClient httpClient = HttpClientExtensions.CreateCompressionHttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(RequestConstants.AcceptHeader));
            httpClient.DefaultRequestHeaders.Add(RequestConstants.ClientIdHeaderKey, RequestConstants.ClientIdHeaderValue);
            return httpClient.ExecuteRequest<T>(request, cancellationToken);
        }
    }
}