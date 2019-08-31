using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

        public async Task<List<UserFollow>> GetUserFollows(string userId, CancellationToken cancellationToken = default)
        {
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));

            var request = $"{RequestConstants.UserFollows.Replace("{0}", userId)}&first={DefaultItemsPerQuery}";
            var userFollowsRoot = await ExecuteRequest<UserFollowsRoot>(request, cancellationToken);
            // if necessary, page until we get all followed streams
            while (userFollowsRoot.Total > 0 && userFollowsRoot.UserFollows.Count < userFollowsRoot.Total)
            {
                var pagedRequest = $"{request}&after={userFollowsRoot.Pagination.Cursor}";
                var pagedFollows = await ExecuteRequest<UserFollowsRoot>(pagedRequest, cancellationToken);
                userFollowsRoot.UserFollows.AddRange(pagedFollows.UserFollows);
            }
            return userFollowsRoot.UserFollows;
        }

        /// <summary> Gets the top streams </summary>
        public async Task<List<Stream>> GetStreams(GetStreamsQuery getStreamsQuery, CancellationToken cancellationToken = default)
        {
            if (getStreamsQuery == null) throw new ArgumentNullException(nameof(getStreamsQuery));

            var streams = new List<Stream>();
            var request = $"{RequestConstants.Streams}?first={getStreamsQuery.First}";
            if (!string.IsNullOrWhiteSpace(getStreamsQuery.Pagination.After))
                request += $"&after={getStreamsQuery.Pagination.After}";

            if (getStreamsQuery.GameIds?.Any() == true)
            {
                if (getStreamsQuery.GameIds.Count > MaxItemsPerQuery)
                    throw new ArgumentException($"Max allowed game ids is {MaxItemsPerQuery}, attempted to query {getStreamsQuery.GameIds.Count}");

                foreach (var gameId in getStreamsQuery.GameIds)
                {
                    request += "&game_id=" + gameId;
                }
            }

            if (getStreamsQuery.Languages?.Any() == true)
            {
                if (getStreamsQuery.Languages.Count > MaxItemsPerQuery)
                    throw new ArgumentException($"Max allowed languages is {MaxItemsPerQuery}, attempted to query {getStreamsQuery.Languages.Count}");

                foreach (var language in getStreamsQuery.Languages)
                {
                    request += "&language=" + language;
                }
            }

            if (getStreamsQuery.UserIds?.Any() == true)
            {
                if (getStreamsQuery.UserIds.Count > MaxItemsPerQuery)
                {
                    var nonPagedRequest = request;
                    int taken = 0;
                    while (taken < getStreamsQuery.UserIds.Count)
                    {
                        var pagedRequest = nonPagedRequest;
                        var pagedIds = getStreamsQuery.UserIds.Skip(taken).Take(DefaultItemsPerQuery);
                        taken += DefaultItemsPerQuery;
                        foreach (var userId in pagedIds)
                        {
                            pagedRequest += "&user_id=" + userId;
                        }

                        var streamRoot = await ExecuteRequest<StreamsRoot>(pagedRequest, cancellationToken);
                        streams.AddRange(streamRoot.Streams);
                    }
                }
                else
                {
                    foreach (var userId in getStreamsQuery.UserIds)
                    {
                        request += "&user_id=" + userId;
                    }
                }
            }

            if (!streams.Any())
            {
                var streamRoot = await ExecuteRequest<StreamsRoot>(request, cancellationToken);
                streams = streamRoot.Streams;
            }
            return streams;
        }

        public async Task<List<TopGame>> GetTopGames(CancellationToken cancellationToken = default)
        {
            var request = RequestConstants.TopGames;
            var gamesRoot = await ExecuteRequest<TopGamesRoot>(request, cancellationToken);
            return gamesRoot.TopGames;
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

                foreach (var gameId in getGamesQuery.GameIds)
                {
                    if (request == RequestConstants.Games) request += "?id=" + gameId;
                    else request += "&id=" + gameId;
                }
            }

            if (getGamesQuery.GameNames?.Any() == true)
            {
                if (getGamesQuery.GameNames.Count > MaxItemsPerQuery)
                    throw new ArgumentException($"Max allowed game names is {MaxItemsPerQuery}, attempted to query {getGamesQuery.GameNames.Count}");

                foreach (var gameName in getGamesQuery.GameNames)
                {
                    if (request == RequestConstants.Games) request += "?name=" + gameName;
                    else request += "&name=" + gameName;
                }
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
                if (getUsersQuery.UserIds.Count > MaxItemsPerQuery)
                {
                    var nonPagedRequest = request;
                    int taken = 0;
                    while (taken < getUsersQuery.UserIds.Count)
                    {
                        var pagedRequest = nonPagedRequest;
                        var pagedIds = getUsersQuery.UserIds.Skip(taken).Take(DefaultItemsPerQuery);
                        taken += DefaultItemsPerQuery;
                        foreach (var userId in pagedIds)
                        {
                            if (pagedRequest == RequestConstants.Users) pagedRequest += "?id=" + userId;
                            else pagedRequest += "&id=" + userId;
                        }

                        var usersRoot = await ExecuteRequest<UsersRoot>(pagedRequest, cancellationToken);
                        users.AddRange(usersRoot.Users);
                    }
                }

                foreach (var userId in getUsersQuery.UserIds)
                {
                    if (request == RequestConstants.Users) request += "?id=" + userId;
                    else request += "&id=" + userId;
                }
            }

            if (getUsersQuery.UserNames?.Any() == true)
            {
                if (getUsersQuery.UserNames.Count > MaxItemsPerQuery)
                {
                    var nonPagedRequest = request;
                    int taken = 0;
                    while (taken < getUsersQuery.UserNames.Count)
                    {
                        var pagedRequest = nonPagedRequest;
                        var pagedUserNames = getUsersQuery.UserNames.Skip(taken).Take(DefaultItemsPerQuery);
                        taken += DefaultItemsPerQuery;
                        foreach (var userName in pagedUserNames)
                        {
                            if (pagedRequest == RequestConstants.Users) pagedRequest += "?login=" + userName;
                            else pagedRequest += "&login=" + userName;
                        }

                        var usersRoot = await ExecuteRequest<UsersRoot>(pagedRequest, cancellationToken);
                        users.AddRange(usersRoot.Users);
                    }
                }
                else
                {
                    foreach (var userName in getUsersQuery.UserNames)
                    {
                        if (request == RequestConstants.Users) request += "?login=" + userName;
                        else request += "&login=" + userName;
                    }
                }
            }

            if (!users.Any())
            {
                var usersRoot = await ExecuteRequest<UsersRoot>(request, cancellationToken);
                users = usersRoot.Users;
            }

            return users;
        }

        public async Task<List<Video>> GetVideos(GetVideosQuery getVideosQuery, CancellationToken cancellationToken = default)
        {
            if (getVideosQuery == null) throw new ArgumentNullException(nameof(getVideosQuery));
            
            var request = RequestConstants.Videos;
            if (!string.IsNullOrWhiteSpace(getVideosQuery.CursorPagination.After))
                request += "?after=" + getVideosQuery.CursorPagination.After;

            if (getVideosQuery.VideoIds?.Any() == true)
            {
                foreach (var videoId in getVideosQuery.VideoIds)
                {
                    if (request == RequestConstants.Videos) request += "?id=" + videoId;
                    else request += "&id=" + videoId;
                }
            }

            if (!string.IsNullOrWhiteSpace(getVideosQuery.GameId))
            {
                if (request == RequestConstants.Videos) request += "?game_id=" + getVideosQuery.GameId;
                else request += "&game_id=" + getVideosQuery.GameId;
            }

            if (!string.IsNullOrWhiteSpace(getVideosQuery.UserId))
            {
                if (request == RequestConstants.Videos) request += "?user_id=" + getVideosQuery.UserId;
                else request += "&user_id=" + getVideosQuery.UserId;
            }

            var channelVideosRoot = await ExecuteRequest<VideosRoot>(request, cancellationToken);
            return channelVideosRoot.Videos;
        }

        private Task<T> ExecuteRequest<T>(string request, CancellationToken cancellationToken = default)
        {
            HttpClient httpClient = HttpClientExtensions.CreateCompressionHttpClient();
            httpClient.DefaultRequestHeaders.Add(RequestConstants.ClientIdHeaderKey, RequestConstants.ClientIdHeaderValue);
            return httpClient.ExecuteRequest<T>(request, cancellationToken);
        }
    }
}