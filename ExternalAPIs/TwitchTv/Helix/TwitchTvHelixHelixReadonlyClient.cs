using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Helix.Dto;
using ExternalAPIs.TwitchTv.Helix.Query;

namespace ExternalAPIs.TwitchTv.Helix
{
    public class TwitchTvHelixHelixReadonlyClient : ITwitchTvHelixReadonlyClient
    {
        public const int DefaultItemsPerQuery = MaxItemsPerQuery; // 20 is default, 100 is maximum
        public const int MaxItemsPerQuery = 100; // 100 is maximum
        private const int MaxConcurrentRequests = 4;
        private readonly SemaphoreSlim requestSlots = new(MaxConcurrentRequests);
        private string accessToken;

        public async Task<List<FollowedChannel>> GetFollowedChannels(string userId, CancellationToken cancellationToken = default)
        {
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));

            var request = $"{RequestConstants.FollowedChannels.Replace("{0}", userId)}&first={DefaultItemsPerQuery}";
            var followedChannelsRoot = await ExecuteRequest<FollowedChannelsRoot>(request, cancellationToken);
            // if necessary, page until we get all followed streams
            while (followedChannelsRoot.Total > 0 && followedChannelsRoot.FollowedChannels.Count < followedChannelsRoot.Total)
            {
                var pagedRequest = $"{request}&after={followedChannelsRoot.Pagination.Cursor}";
                var pagedFollows = await ExecuteRequest<FollowedChannelsRoot>(pagedRequest, cancellationToken);
                followedChannelsRoot.FollowedChannels.AddRange(pagedFollows.FollowedChannels);
            }
            return followedChannelsRoot.FollowedChannels;
        }

        public async Task<StreamsRoot> GetStreams(GetStreamsQuery getStreamsQuery, CancellationToken cancellationToken = default)
        {
            if (getStreamsQuery == null) throw new ArgumentNullException(nameof(getStreamsQuery));

            var request = $"{RequestConstants.Streams}?first={getStreamsQuery.First}";
            if (!string.IsNullOrWhiteSpace(getStreamsQuery.Pagination.After))
                request += $"&after={getStreamsQuery.Pagination.After}";

            if (getStreamsQuery.GameIds?.Any() == true)
            {
                if (getStreamsQuery.GameIds.Count > MaxItemsPerQuery)
                    throw new ArgumentException($"Max allowed game ids is {MaxItemsPerQuery}, attempted to query {getStreamsQuery.GameIds.Count}");

                request = request.AppendQueryStringValues("game_id", getStreamsQuery.GameIds, isFirstParam: false);
            }

            if (getStreamsQuery.Languages?.Any() == true)
            {
                if (getStreamsQuery.Languages.Count > MaxItemsPerQuery)
                    throw new ArgumentException($"Max allowed languages is {MaxItemsPerQuery}, attempted to query {getStreamsQuery.Languages.Count}");

                request = request.AppendQueryStringValues("language", getStreamsQuery.Languages, isFirstParam: false);
            }

            var streamsRoot = new StreamsRoot() { Streams = new List<Stream>() };
            if (getStreamsQuery.UserIds?.Any() == true)
            {
                var responses = await ExecuteBatches<StreamsRoot>(
                    request,
                    "user_id",
                    getStreamsQuery.UserIds,
                    cancellationToken);
                foreach (var response in responses)
                {
                    streamsRoot.Pagination = response.Pagination;
                    streamsRoot.Streams.AddRange(response.Streams);
                }
            }
            else
            {
                streamsRoot = await ExecuteRequest<StreamsRoot>(request, cancellationToken);
            }
            return streamsRoot;
        }

        public async Task<List<TwitchCategory>> GetTopGames(CancellationToken cancellationToken = default)
        {
            var request = RequestConstants.TopGames;
            var topCategoriesRoot = await ExecuteRequest<TopCategoriesRoot>(request, cancellationToken);
            return topCategoriesRoot.TopCategories;
        }

        public async Task<User> GetUserByUsername(string username, CancellationToken cancellationToken = default)
        {
            var users = await GetUsers(new GetUsersQuery() { UserNames = new List<string>() { username } }, cancellationToken);
            return users.FirstOrDefault();
        }

        public async Task<List<Game>> GetGames(GetGamesQuery getGamesQuery, CancellationToken cancellationToken = default)
        {
            if (getGamesQuery == null) throw new ArgumentNullException(nameof(getGamesQuery));
            if (!getGamesQuery.GameIds.Any() && !getGamesQuery.GameNames.Any())
                throw new ArgumentException("At least one game id or game name must be specified");

            var request = RequestConstants.Games;
            if (getGamesQuery.GameIds?.Any() == true)
            {
                if (getGamesQuery.GameIds.Count > MaxItemsPerQuery)
                    throw new ArgumentException($"Max allowed game ids is {MaxItemsPerQuery}, attempted to query {getGamesQuery.GameIds.Count}");

                request = request.AppendQueryStringValues("id", getGamesQuery.GameIds);
            }

            if (getGamesQuery.GameNames?.Any() == true)
            {
                if (getGamesQuery.GameNames.Count > MaxItemsPerQuery)
                    throw new ArgumentException($"Max allowed game names is {MaxItemsPerQuery}, attempted to query {getGamesQuery.GameNames.Count}");

                request = request.AppendQueryStringValues("name", getGamesQuery.GameNames);
            }

            var gamesRoot = await ExecuteRequest<GamesRoot>(request, cancellationToken);
            return gamesRoot.Games;
        }

        public async Task<List<User>> GetUsers(GetUsersQuery getUsersQuery, CancellationToken cancellationToken = default)
        {
            if (getUsersQuery == null) throw new ArgumentNullException(nameof(getUsersQuery));

            var users = new List<User>();
            var request = RequestConstants.Users;
            if (getUsersQuery.UserIds?.Any() == true)
            {
                var responses = await ExecuteBatches<UsersRoot>(
                    request,
                    "id",
                    getUsersQuery.UserIds,
                    cancellationToken);
                foreach (var response in responses) AddUsers(response);
            }

            if (getUsersQuery.UserNames?.Any() == true)
            {
                var responses = await ExecuteBatches<UsersRoot>(
                    request,
                    "login",
                    getUsersQuery.UserNames,
                    cancellationToken);
                foreach (var response in responses) AddUsers(response);
            }

            return users;

            void AddUsers(UsersRoot response)
            {
                if (response?.Users == null) throw new System.IO.InvalidDataException("Twitch returned no user data.");

                users.AddRange(response.Users);
            }
        }

        public async Task<VideosRoot> GetVideos(GetVideosQuery getVideosQuery, CancellationToken cancellationToken = default)
        {
            if (getVideosQuery == null) throw new ArgumentNullException(nameof(getVideosQuery));

            var request = RequestConstants.Videos + "?first=" + getVideosQuery.First;
            if (!string.IsNullOrWhiteSpace(getVideosQuery.CursorPagination.After))
                request += "&after=" + getVideosQuery.CursorPagination.After;

            if (getVideosQuery.VideoIds?.Any() == true)
            {
                request = request.AppendQueryStringValues("id", getVideosQuery.VideoIds, isFirstParam: false);
            }

            if (!string.IsNullOrWhiteSpace(getVideosQuery.GameId))
            {
                request += "&game_id=" + getVideosQuery.GameId;
            }

            if (!string.IsNullOrWhiteSpace(getVideosQuery.UserId))
            {
                request += "&user_id=" + getVideosQuery.UserId;
            }

            if (!string.IsNullOrWhiteSpace(getVideosQuery.Type))
            {
                request += "&type=" + getVideosQuery.Type;
            }

            var channelVideosRoot = await ExecuteRequest<VideosRoot>(request, cancellationToken);
            return channelVideosRoot;
        }

        public async Task<List<TwitchCategory>> SearchCategories(string searchCategory, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(searchCategory))
                throw new ArgumentException("Value cannot be null or empty.", nameof(searchCategory));

            var request = RequestConstants.SearchCategories + "?query=" + searchCategory;

            var topCategoriesRoot = await ExecuteRequest<TopCategoriesRoot>(request, cancellationToken);
            return topCategoriesRoot.TopCategories;
        }

        public void SetAccessToken(string token)
        {
            accessToken = token;
        }

        private async Task<T[]> ExecuteBatches<T>(
            string request,
            string parameter,
            List<string> values,
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task<T>>();
            for (var i = 0; i < values.Count; i += MaxItemsPerQuery)
            {
                var batch = values.Skip(i).Take(MaxItemsPerQuery);
                var isFirstParameter = !request.Contains("?");
                var batchRequest = request.AppendQueryStringValues(parameter, batch, isFirstParam: isFirstParameter);
                tasks.Add(ExecuteRequest<T>(batchRequest, cancellationToken));
            }

            return await Task.WhenAll(tasks);
        }

        private async Task<T> ExecuteRequest<T>(string request, CancellationToken cancellationToken = default)
        {
            await requestSlots.WaitAsync(cancellationToken);
            try
            {
                using var httpClient = HttpClientExtensions.CreateCompressionHttpClient();
                httpClient.DefaultRequestHeaders.Add(RequestConstants.ClientIdHeaderKey, RequestConstants.ClientIdHeaderValue);
                if (accessToken != null)
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                return await httpClient.ExecuteRequest<T>(request, cancellationToken);
            }
            finally
            {
                requestSlots.Release();
            }
        }
    }
}
