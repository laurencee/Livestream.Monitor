using System;
using System.Collections.Generic;
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
        public async Task<UserFollows> GetUserFollows(string username)
        {
            if (IsNullOrWhiteSpace(username))
                throw new ArgumentException("Argument is null or whitespace", nameof(username));

            const int itemsPerPage = 100; // 25 is default, 100 is maximum

            var request = $"{RequestConstants.UserFollows.Replace("{0}", username)}?limit={itemsPerPage}";
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
            if (IsNullOrWhiteSpace(streamName))
                throw new ArgumentException("Argument is null or whitespace", nameof(streamName));

            var request = RequestConstants.ChannelDetails.Replace("{0}", streamName);
            var channelDetails = await ExecuteRequest<Channel>(request);
            return channelDetails;
        }

        public async Task<Stream> GetStreamDetails(string streamName)
        {
            if (IsNullOrWhiteSpace(streamName))
                throw new ArgumentException("Argument is null or whitespace", nameof(streamName));

            var request = RequestConstants.StreamDetails.Replace("{0}", streamName);
            var streamRoot = await ExecuteRequest<StreamRoot>(request);
            return streamRoot.Stream;
        }

        public async Task<List<Game>> GetTopGames()
        {
            var request = RequestConstants.TopGames;
            var gamesRoot = await ExecuteRequest<TopGamesRoot>(request);
            return gamesRoot.Top;
        }

        public async Task<List<Stream>> SearchStreams(string streamName)
        {
            if (IsNullOrWhiteSpace(streamName))
                throw new ArgumentException("Argument is null or whitespace", nameof(streamName));

            var request = RequestConstants.SearchStreams.Replace("{0}", streamName);
            var streamsRoot = await ExecuteRequest<StreamsRoot>(request);
            return streamsRoot.Streams;
        }

        public async Task<List<Game>> SearchGames(string gameName)
        {
            if (IsNullOrWhiteSpace(gameName))
                throw new ArgumentException("Argument is null or whitespace", nameof(gameName));

            var request = RequestConstants.SearchGames.Replace("{0}", gameName);
            var gamesRoot = await ExecuteRequest<GamesRoot>(request);
            return gamesRoot.Games;
        }

        private async Task<T> ExecuteRequest<T>(string request)
        {
            // we create a new client each time as it will execute much faster (at the expense of some additional memory)
            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(RequestConstants.AcceptHeader));
                var responseString = await httpClient.GetStringAsync(request);
                return JsonConvert.DeserializeObject<T>(responseString);
            }
        }
    }
}