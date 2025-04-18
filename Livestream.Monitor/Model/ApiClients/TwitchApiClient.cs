using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
using ExternalAPIs.TwitchTv.Helix;
using ExternalAPIs.TwitchTv.Helix.Dto;
using ExternalAPIs.TwitchTv.Helix.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;
using MahApps.Metro.Controls.Dialogs;
using RequestConstants = ExternalAPIs.TwitchTv.Helix.RequestConstants;

namespace Livestream.Monitor.Model.ApiClients
{
    public class TwitchApiClient : IApiClient
    {
        public const string API_NAME = "twitchtv";
        private const string BroadcastVodType = "Broadcasts";
        private const string HighlightVodType = "Highlights";
        private const string RedirectUri = @"https://github.com/laurencee/Livestream.Monitor";

        private readonly ITwitchTvHelixReadonlyClient twitchTvHelixClient;
        private readonly ISettingsHandler settingsHandler;
        private readonly HashSet<ChannelIdentifier> monitoredChannels = new HashSet<ChannelIdentifier>();
        private readonly Dictionary<string, string> gameNameToIdMap = new Dictionary<string, string>();
        private readonly Dictionary<string, string> gameIdToNameMap = new Dictionary<string, string>();
        private readonly Dictionary<string, User> streamDisplayNameToUserMap = new Dictionary<string, User>();
        private readonly Dictionary<TopStreamQuery, string> topStreamsPaginationKeyMap = new Dictionary<TopStreamQuery, string>();
        private readonly Dictionary<VodQuery, string> vodsPaginationKeyMap = new Dictionary<VodQuery, string>();

        public TwitchApiClient(
            ITwitchTvHelixReadonlyClient twitchTvHelixClient,
            ISettingsHandler settingsHandler)
        {
            this.twitchTvHelixClient = twitchTvHelixClient ?? throw new ArgumentNullException(nameof(twitchTvHelixClient));
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
        }

        public string ApiName => API_NAME;

        public string BaseUrl => @"https://www.twitch.tv/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => true;

        public bool HasTopStreamGameFilterSupport => true;

        public bool HasFollowedChannelsQuerySupport => true;

        public bool IsAuthorized => settingsHandler.Settings.Twitch.IsAuthTokenSet || settingsHandler.Settings.Twitch.PassthroughClientId;

        public List<string> VodTypes { get; } = new List<string>()
        {
            BroadcastVodType,
            HighlightVodType
        };

        public string LivestreamerAuthorizationArg => null;

        public async Task Authorize(IViewAware screen)
        {
            var messageDialogResult = await screen.ShowMessageAsync(title: "Twitch Authorization",
                message: "Twitch requires authorization for querying livestreams.\nWould you like to authorize this application now?",
                messageDialogStyle: MessageDialogStyle.AffirmativeAndNegative,
                dialogSettings: new MetroDialogSettings()
                {
                    AffirmativeButtonText = "Yes",
                    NegativeButtonText = "No"
                });

            if (messageDialogResult == MessageDialogResult.Negative) return;

            RequestAuthorization();

            var input = await screen.ShowDialogAsync(title: "Authorization Token",
                message:
                "Once you've approved me, please copy the full redirect url to here so we can save your access token for future use." +
                Environment.NewLine +
                Environment.NewLine +
                $"*Note* The url should start with '{RedirectUri}'");

            if (string.IsNullOrWhiteSpace(input))
            {
                await screen.ShowMessageAsync("Authorization cancelled", "Authorization process was cancelled.");
                return;
            }

            if (input.StartsWith(RedirectUri))
            {
                var match = Regex.Match(input, "#access_token=(?<token>.*?)&");
                if (match.Groups["token"].Success)
                {
                    settingsHandler.Settings.Twitch.AuthToken = match.Groups["token"].Value;
                    settingsHandler.SaveSettings();
                    twitchTvHelixClient.SetAccessToken(settingsHandler.Settings.Twitch.AuthToken);
                    await Initialize();
                    return;
                }
            }

            await screen.ShowMessageAsync("Bad url provided",
                $"Please make sure you copy the full url, it should start with '{RedirectUri}'");
        }

        public async Task<string> GetStreamUrl(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));

            var urlSuffix = await GetStreamUrlSuffix(livestreamModel.ChannelIdentifier);
            return $"{BaseUrl}{urlSuffix}/";
        }

        public async Task<string> GetChatUrl(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));

            var urlSuffix = await GetStreamUrlSuffix(livestreamModel.ChannelIdentifier);
            return $"{BaseUrl}{urlSuffix}/chat?popout=true";
        }

        public async Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel)
        {
            if (newChannel == null) throw new ArgumentNullException(nameof(newChannel));

            // shorter implementation of QueryChannels
            var queryResults = new List<LivestreamQueryResult>();
            User user;
            if (long.TryParse(newChannel.ChannelId, out var _))
            {
                var users = await twitchTvHelixClient.GetUsers(new GetUsersQuery() { UserIds = new List<string>() { newChannel.ChannelId } });
                user = users.FirstOrDefault();
            }
            else
            {
                user = await GetUserByUsername(newChannel.ChannelId);
            }

            if (user == null) throw new InvalidOperationException("No user found for id " + newChannel.ChannelId);

            newChannel.OverrideChannelId(user.Id);
            newChannel.DisplayName = user.DisplayName;
            var livestream = new LivestreamModel(user.Id, newChannel) { DisplayName = user.DisplayName };

            var streamsRoot = await twitchTvHelixClient.GetStreams(new GetStreamsQuery() { UserIds = new List<string>() { user.Id } });
            var onlineStream = streamsRoot.Streams.FirstOrDefault();
            if (onlineStream != null)
            {
                livestream.PopulateWithStreamDetails(onlineStream);
                livestream.Game = await GetGameNameById(onlineStream.GameId);
            }

            queryResults.Add(new LivestreamQueryResult(newChannel)
            {
                LivestreamModel = livestream
            });

            if (queryResults.All(x => x.IsSuccess))
            {
                monitoredChannels.Add(newChannel);
            }
            return queryResults;
        }

        public void AddChannelWithoutQuerying(ChannelIdentifier newChannel)
        {
            if (newChannel == null) throw new ArgumentNullException(nameof(newChannel));
            monitoredChannels.Add(newChannel);
        }

        public Task RemoveChannel(ChannelIdentifier channelIdentifier)
        {
            monitoredChannels.Remove(channelIdentifier);
            return Task.CompletedTask;
        }

        public async Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken)
        {
            var queryResults = new List<LivestreamQueryResult>();
            if (monitoredChannels.Count == 0) return queryResults;

            // Twitch "get streams" call only returns online streams so to determine if the stream actually exists/is still valid, we must specifically ask for channel details.
            List<Stream> onlineStreams = new List<Stream>();

            int retryCount = 0;
            bool success = false;
            while (!success && !cancellationToken.IsCancellationRequested && retryCount < 3)
            {
                try
                {
                    var query = new GetStreamsQuery();
                    query.UserIds.AddRange(monitoredChannels.Select(x => x.ChannelId));
                    var streamsRoot = await twitchTvHelixClient.GetStreams(query, cancellationToken);
                    onlineStreams = streamsRoot.Streams;
                    success = true;
                }
                catch (HttpRequestWithStatusException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    await Task.Delay(2000, cancellationToken);
                    retryCount++;
                }
            }

            foreach (var onlineStream in onlineStreams)
            {
                if (cancellationToken.IsCancellationRequested) return queryResults;

                var channelIdentifier = monitoredChannels.First(x => x.ChannelId.IsEqualTo(onlineStream.UserId));
                var gameName = await GetGameNameById(onlineStream.GameId);

                var livestream = new LivestreamModel(onlineStream.UserId, channelIdentifier);
                livestream.PopulateWithStreamDetails(onlineStream);
                livestream.Game = gameName;
                queryResults.Add(new LivestreamQueryResult(channelIdentifier)
                {
                    LivestreamModel = livestream
                });
            }

            var offlineStreams = monitoredChannels.Where(x => onlineStreams.All(y => y.UserId != x.ChannelId)).ToList();
            foreach (var offlineStream in offlineStreams)
            {
                var queryResult = new LivestreamQueryResult(offlineStream)
                {
                    LivestreamModel = new LivestreamModel(offlineStream.ChannelId, offlineStream)
                    {
                        DisplayName = offlineStream.DisplayName ?? offlineStream.ChannelId
                    }
                };

                queryResults.Add(queryResult);
            }

            return queryResults;
        }

        public async Task<IReadOnlyCollection<VodDetails>> GetVods(VodQuery vodQuery)
        {
            if (vodQuery == null) throw new ArgumentNullException(nameof(vodQuery));
            if (string.IsNullOrWhiteSpace(vodQuery.StreamDisplayName)) throw new ArgumentNullException(nameof(vodQuery.StreamDisplayName));

            var user = await GetUserByUsername(vodQuery.StreamDisplayName);
            if (user == null) return new List<VodDetails>();

            vodsPaginationKeyMap.TryGetValue(vodQuery, out var paginationKey);
            var getVideosQuery = new GetVideosQuery()
            {
                UserId = user.Id,
                First = vodQuery.Take,
                CursorPagination = new CursorPagination()
                {
                    After = paginationKey
                }
            };
            var videosRoot = await twitchTvHelixClient.GetVideos(getVideosQuery);

            var nextPageKeyLookup = new VodQuery()
            {
                StreamDisplayName = user.Id,
                Skip = vodQuery.Skip + vodQuery.Take,
                Take = vodQuery.Take,
                VodTypes = vodQuery.VodTypes,
            };
            vodsPaginationKeyMap[nextPageKeyLookup] = videosRoot.Pagination.Cursor;

            var vods = videosRoot.Videos.Select(video =>
            {
                var largeThumbnail = video.ThumbnailTemplateUrl.Replace("%{width}", "640").Replace("%{height}", "360");
                // stupid fucking new duration format from twitch instead of just returning seconds or some other sensible value
                var match = Regex.Match(video.Duration, @"((?<hours>\d+)?h)?((?<mins>\d+)?)m?(?<secs>\d+)s");
                var hours = match.Groups["hours"].Value;
                var mins = match.Groups["mins"].Value;
                var secs = match.Groups["secs"].Value;
                var timespan = new TimeSpan(hours.ToInt(), mins.ToInt(), secs.ToInt());

                var singleLineTitle = video.Title.Replace("\r\n", " ").Replace('\n', ' ').TrimEnd();
                var singleLineDesc = video.Description.Replace("\r\n", " ").Replace('\n', ' ').TrimEnd();

                return new VodDetails
                {
                    Url = video.Url,
                    Length = timespan,
                    RecordedAt = video.CreatedAt,
                    Views = video.ViewCount,
                    //Game = video.Game,
                    Description = singleLineDesc,
                    Title = singleLineTitle,
                    PreviewImage = largeThumbnail,
                    ApiClient = this,
                };
            }).ToList();

            return vods;
        }

        public async Task<TopStreamsResponse> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            if (topStreamQuery == null) throw new ArgumentNullException(nameof(topStreamQuery));

            topStreamsPaginationKeyMap.TryGetValue(topStreamQuery, out var paginationKey);
            var query = new GetStreamsQuery()
            {
                // twitch sometimes returns less streams than we ask for some unknown reason
                // ask for a few more in the query to try avoid pagination issues
                First = topStreamQuery.Take + 5,
                Pagination = new CursorPagination()
                {
                    After = paginationKey
                }
            };
            if (!string.IsNullOrWhiteSpace(topStreamQuery.GameName))
            {
                var gameId = await GetGameIdByName(topStreamQuery.GameName);
                if (gameId != null) query.GameIds.Add(gameId);
            }

            var topStreams = await twitchTvHelixClient.GetStreams(query);
            var nextPageKeyLookup = new TopStreamQuery()
            {
                GameName = topStreamQuery.GameName,
                Skip = topStreamQuery.Skip + topStreamQuery.Take,
                Take = topStreamQuery.Take
            };
            topStreamsPaginationKeyMap[nextPageKeyLookup] = topStreams.Pagination?.Cursor;

            var livestreamModels = new List<LivestreamModel>();
            foreach (var stream in topStreams.Streams.Take(topStreamQuery.Take))
            {
                var channelIdentifier = new ChannelIdentifier(this, stream.UserId) { DisplayName = stream.UserName };
                var livestreamModel = new LivestreamModel(stream.UserId, channelIdentifier);
                livestreamModel.PopulateWithStreamDetails(stream);
                livestreamModel.Game = await GetGameNameById(stream.GameId);

                livestreamModels.Add(livestreamModel);
            }

            return new TopStreamsResponse()
            {
                LivestreamModels = livestreamModels,
                HasNextPage = topStreams.Pagination?.Cursor != null,
            };
        }

        public async Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            if (string.IsNullOrEmpty(filterGameName))
            {
                var topGames = await twitchTvHelixClient.GetTopGames();
                foreach (var topGame in topGames)
                {
                    var gameId = topGame.Id;
                    gameNameToIdMap[topGame.Id] = gameId;
                    gameIdToNameMap[gameId] = topGame.Name;
                }

                return topGames.Select(x => new KnownGame()
                {
                    GameName = x.Name,
                    ThumbnailUrls = new ThumbnailUrls()
                    {
                        Medium = x.BoxArtUrl,
                        Small = x.BoxArtUrl,
                        Large = x.BoxArtUrl
                    }
                }).ToList();
            }

            var twitchGames = await twitchTvHelixClient.SearchCategories(filterGameName);
            if (twitchGames == null) return new List<KnownGame>();

            foreach (var game in twitchGames)
            {
                var gameId = game.Id;
                gameNameToIdMap[game.Name] = gameId;
                gameIdToNameMap[gameId] = game.Name;
            }
            return twitchGames.Select(x => new KnownGame()
            {
                GameName = x.Name,
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Medium = x.BoxArtUrl,
                    Small = x.BoxArtUrl,
                    Large = x.BoxArtUrl,
                }
            }).ToList();
        }

        public async Task<List<LivestreamQueryResult>> GetFollowedChannels(string userName)
        {
            var user = await GetUserByUsername(userName);
            if (user == null) throw new InvalidOperationException("Could not find user with username: " + userName);

            streamDisplayNameToUserMap[user.DisplayName] = user;
            var userFollows = await twitchTvHelixClient.GetFollowedChannels(user.Id);
            return (from follow in userFollows
                    let channelIdentifier = new ChannelIdentifier(this, follow.BroadcasterId) { DisplayName = follow.BroadcasterName }
                    select new LivestreamQueryResult(channelIdentifier)
                    {
                        LivestreamModel = new LivestreamModel(follow.BroadcasterId, channelIdentifier)
                        {
                            DisplayName = follow.BroadcasterName,
                            //Description = follow.Channel?.Status,
                            //Game = follow.Channel?.Game,
                            //IsPartner = follow.Channel?.Partner != null && follow.Channel.Partner.Value,
                            ImportedBy = userName,
                            //BroadcasterLanguage = follow.Channel?.BroadcasterLanguage
                        }
                    }).ToList();
        }

        public async Task<InitializeApiClientResult> Initialize(CancellationToken cancellationToken = default)
        {
            var result = new InitializeApiClientResult();
            if (string.IsNullOrWhiteSpace(settingsHandler.Settings.Twitch.AuthToken)) return result;

            twitchTvHelixClient.SetAccessToken(settingsHandler.Settings.Twitch.AuthToken);
            try
            {
                // sets up initial cache of game id/name maps
                await GetKnownGameNames(null);
            }
            catch
            {
                // not important enough to prevent the app from initializing if this fails
            }

            try
            {
                // initialize user cache
                var usersQuery = new GetUsersQuery();
                usersQuery.UserIds.AddRange(monitoredChannels.Select(x => x.ChannelId));
                var users = await twitchTvHelixClient.GetUsers(usersQuery, cancellationToken);
                foreach (var user in users)
                {
                    streamDisplayNameToUserMap[user.DisplayName] = user;
                    if (monitoredChannels.TryGetValue(new ChannelIdentifier(this, user.Id),
                            out var existingChannelIdentifier))
                    {
                        if (existingChannelIdentifier.DisplayName != user.DisplayName)
                        {
                            existingChannelIdentifier.DisplayName = user.DisplayName;
                            result.ChannelIdentifierDataDirty = true;
                        }
                    }
                }
            }
            catch
            {
                // not important enough to prevent the app from initializing if this fails
            }

            return result;
        }

        private async Task<string> GetStreamUrlSuffix(ChannelIdentifier channelId)
        {
            if (streamDisplayNameToUserMap.TryGetValue(channelId.DisplayName, out var user)) return user.Login;

            var query = new GetUsersQuery();
            query.UserIds.Add(channelId.ChannelId);
            var users = await twitchTvHelixClient.GetUsers(query);
            streamDisplayNameToUserMap[channelId.DisplayName] = users[0];
            return users[0].Login;
        }

        private async Task<string> GetGameIdByName(string gameName)
        {
            if (!gameNameToIdMap.TryGetValue(gameName, out var gameId))
            {
                var query = new GetGamesQuery();
                query.GameNames.Add(gameName);
                var games = await twitchTvHelixClient.GetGames(query);
                if (!games.Any()) return null;

                gameId = games[0].Id;
                gameNameToIdMap[gameName] = gameId;
                gameIdToNameMap[gameId] = gameName;
            }

            return gameId;
        }

        private async Task<string> GetGameNameById(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;

            if (!gameIdToNameMap.TryGetValue(gameId, out var gameName))
            {
                var query = new GetGamesQuery();
                query.GameIds.Add(gameId);
                var games = await twitchTvHelixClient.GetGames(query);
                if (!games.Any()) return null;

                gameName = games[0].Name;
                gameNameToIdMap[gameName] = gameId;
                gameIdToNameMap[gameId] = gameName;
            }

            return gameName;
        }

        /// <summary> Launches browser for user to authorize us </summary>
        private void RequestAuthorization()
        {
            const string scopes = "user:read:follows";

            var request =
                "https://id.twitch.tv/oauth2/authorize?response_type=token" +
                $"&client_id={RequestConstants.ClientIdHeaderValue}" +
                $"&redirect_uri={RedirectUri}" +
                $"&scope={scopes}";

            System.Diagnostics.Process.Start(request);
        }

        private async Task<User> GetUserByUsername(string username)
        {
            if (streamDisplayNameToUserMap.TryGetValue(username, out var user))
                return user;

            user = await twitchTvHelixClient.GetUserByUsername(username);
            streamDisplayNameToUserMap[username] = user;
            return user;
        }
    }
}