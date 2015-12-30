using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TwitchTv.Dto;
using TwitchTv.Dto.QueryRoot;
using static System.String;

namespace TwitchTv
{
    public class TwitchTvReadonlyClient : ITwitchTvReadonlyClient
    {
        const int ItemsPerQuery = 100; // 25 is default, 100 is maximum

        public async Task<UserFollows> GetUserFollows(string username)
        {
            if (IsNullOrWhiteSpace(username)) throw new ArgumentNullException(nameof(username));

            var request = $"{RequestConstants.UserFollows.Replace("{0}", username)}?limit={ItemsPerQuery}";
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
        /// <param name="skip">Number of streams to skip</param>
        /// <param name="take">Number of streams to take (max 100)</param>
        public async Task<List<Stream>> GetTopStreams(int skip, int take = 25)
        {
            if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take), "Top stream query minimum request size is 1");
            if (take > 100) throw new ArgumentOutOfRangeException(nameof(take), "Top stream query maximum request size is 100");
            if (skip < 0) skip = 0;

            var request = $"{RequestConstants.Streams}?offset={skip}&limit={take}";
            var streamRoot = await ExecuteRequest<StreamsRoot>(request);
            return streamRoot.Streams;
        }

        /// <summary> Gets the top 100 streams by <paramref name="gameName"/> </summary>
        public async Task<List<Stream>> GetTopStreamsByGame(string gameName)
        {
            if (IsNullOrWhiteSpace(gameName)) throw new ArgumentNullException(nameof(gameName));

            var request = $"{RequestConstants.Streams}?game={gameName}&limit={ItemsPerQuery}";
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
            
            var request = $"{RequestConstants.Streams}?channel={Join(",", streamNames)}&limit={ItemsPerQuery}";
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