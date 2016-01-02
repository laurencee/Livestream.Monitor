using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TwitchTv.Dto;
using TwitchTv.Dto.QueryRoot;
using TwitchTv.Query;
using static System.String;

namespace TwitchTv
{
    public class TwitchTvReadonlyClient : ITwitchTvReadonlyClient
    {
        public const int DefaultItemsPerQuery = 100; // 25 is default, 100 is maximum

        public async Task<UserFollows> GetUserFollows(string username)
        {
            if (IsNullOrWhiteSpace(username)) throw new ArgumentNullException(nameof(username));

            var request = $"{RequestConstants.UserFollows.Replace("{0}", username)}?limit={DefaultItemsPerQuery}";
            var userFollows = await ExecuteRequest<UserFollows>(request);
            // if necessary, page until we get all followed streams
            while (userFollows.Total > 0 && userFollows.Follows.Count < userFollows.Total)
            {
                var pagedRequest = $"{request}&offset={userFollows.Follows.Count}";
                var pagedFollows = await ExecuteRequest<UserFollows>(pagedRequest);
                userFollows.Follows.AddRange(pagedFollows.Follows);
            }
            return userFollows;
        }

        public async Task<Channel> GetChannelDetails(string streamName)
        {
            if (IsNullOrWhiteSpace(streamName)) throw new ArgumentNullException(nameof(streamName));

            var request = $"{RequestConstants.Channels}/{streamName}";
            var channelDetails = await ExecuteRequest<Channel>(request);
            return channelDetails;
        }

        /// <summary> Gets the top streams </summary>
        public async Task<List<Stream>> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            if (topStreamQuery == null) throw new ArgumentNullException(nameof(topStreamQuery));

            var request = $"{RequestConstants.Streams}?offset={topStreamQuery.Skip}&limit={topStreamQuery.Take}";
            if (!IsNullOrWhiteSpace(topStreamQuery.GameName))
                request += $"&game={topStreamQuery.GameName}";

            var streamRoot = await ExecuteRequest<StreamsRoot>(request);
            return streamRoot.Streams;
        }

        public async Task<Stream> GetStreamDetails(string streamName)
        {
            if (IsNullOrWhiteSpace(streamName)) throw new ArgumentNullException(nameof(streamName));

            var request = $"{RequestConstants.Streams}/{streamName}";
            var streamRoot = await ExecuteRequest<StreamRoot>(request);
            return streamRoot.Stream;
        }

        public async Task<List<Stream>> GetStreamsDetails(List<string> streamNames)
        {
            if (streamNames == null) throw new ArgumentNullException(nameof(streamNames));
            
            var request = $"{RequestConstants.Streams}?channel={Join(",", streamNames)}&limit={DefaultItemsPerQuery}";
            var streamRoot = await ExecuteRequest<StreamsRoot>(request);

            // if necessary, page until we get all followed streams
            while (streamRoot.Total > 0 && streamRoot.Streams.Count < streamRoot.Total)
            {
                var pagedRequest = $"{request}&offset={streamRoot.Streams.Count}";
                var pagedStreamsDetails = await ExecuteRequest<StreamsRoot>(pagedRequest);
                streamRoot.Streams.AddRange(pagedStreamsDetails.Streams);
            }

            return streamRoot.Streams;
        }

        public async Task<List<Game>> GetTopGames()
        {
            var request = RequestConstants.TopGames;
            var gamesRoot = await ExecuteRequest<TopGamesRoot>(request);
            return gamesRoot.Top;
        }

        public async Task<List<Stream>> SearchStreams(string streamName)
        {
            if (IsNullOrWhiteSpace(streamName)) throw new ArgumentNullException(nameof(streamName));

            var request = RequestConstants.SearchStreams.Replace("{0}", streamName);
            var streamsRoot = await ExecuteRequest<StreamsRoot>(request);
            return streamsRoot.Streams;
        }

        public async Task<List<Game>> SearchGames(string gameName)
        {
            if (IsNullOrWhiteSpace(gameName)) throw new ArgumentNullException(nameof(gameName));

            var request = RequestConstants.SearchGames.Replace("{0}", gameName);
            var gamesRoot = await ExecuteRequest<GamesRoot>(request);
            return gamesRoot.Games;
        }

        public async Task<List<Video>> GetChannelVideos(ChannelVideosQuery channelVideosQuery)
        {
            if (channelVideosQuery == null) throw new ArgumentNullException(nameof(channelVideosQuery));
            if (IsNullOrWhiteSpace(channelVideosQuery.ChannelName)) throw new ArgumentNullException(nameof(channelVideosQuery.ChannelName));

            var request = RequestConstants.ChannelVideos.Replace("{0}", channelVideosQuery.ChannelName);
            request += $"?offset={channelVideosQuery.Skip}&limit={channelVideosQuery.Take}";
            if (channelVideosQuery.BroadcastsOnly)
                request += "&broadcasts=true";
            if (channelVideosQuery.HLSVodsOnly)
                request += "&hls=true";

            var channelVideosRoot = await ExecuteRequest<ChannelVideosRoot>(request);
            return channelVideosRoot.Videos;
        }

        private async Task<T> ExecuteRequest<T>(string request)
        {
            // we create a new client each time as it will execute much faster (at the expense of some additional memory)
            using (HttpClient httpClient = new HttpClient(new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            }))
            {
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(RequestConstants.AcceptHeader));
                var responseString = await httpClient.GetStringAsync(request);
                return JsonConvert.DeserializeObject<T>(responseString);
            }
        }
    }
}