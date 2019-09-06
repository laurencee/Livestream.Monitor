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
using ExternalAPIs.TwitchTv.V3.Query;
using ExternalAPIs.TwitchTv.V5;
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

        private readonly ITwitchTvV5ReadonlyClient twitchTvV5ReadonlyClient;
        private readonly ITwitchTvHelixReadonlyClient twitchTvHelixClient;
        private readonly ISettingsHandler settingsHandler;
        private readonly HashSet<ChannelIdentifier> moniteredChannels = new HashSet<ChannelIdentifier>();
        private readonly Dictionary<string, string> gameNameToIdMap = new Dictionary<string, string>();
        private readonly Dictionary<string, string> gameIdToNameMap = new Dictionary<string, string>();
        private readonly Dictionary<string, User> channelIdToUserMap = new Dictionary<string, User>();

        public TwitchApiClient(
            ITwitchTvV5ReadonlyClient twitchTvV5Client,
            ITwitchTvHelixReadonlyClient twitchTvHelixClient,
            ISettingsHandler settingsHandler)
        {
            twitchTvV5ReadonlyClient = twitchTvV5Client ?? throw new ArgumentNullException(nameof(twitchTvV5Client));
            this.twitchTvHelixClient = twitchTvHelixClient ?? throw new ArgumentNullException(nameof(twitchTvHelixClient));
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
        }

        public string ApiName => API_NAME;

        public string BaseUrl => @"https://www.twitch.tv/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => true;

        public bool HasTopStreamGameFilterSupport => true;

        public bool HasUserFollowQuerySupport => true;

        public bool IsAuthorized => settingsHandler.Settings.TwitchAuthTokenSet || settingsHandler.Settings.PassthroughClientId;

        public List<string> VodTypes { get; } = new List<string>()
        {
            BroadcastVodType,
            HighlightVodType
        };

        public string LivestreamerAuthorizationArg
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(settingsHandler.Settings.TwitchAuthToken))
                {
                    return "--twitch-oauth-token " + settingsHandler.Settings.TwitchAuthToken;
                }

                return null;
            }
        }

        public async Task Authorize(IViewAware screen)
        {
            var messageDialogResult = await screen.ShowMessageAsync(title: "Authorization",
                message: $"Twitch requires authorization to connect to their services, have you set your oauth token in your {settingsHandler.Settings.LivestreamExeDisplayName} configuration file?",
                messageDialogStyle: MessageDialogStyle.AffirmativeAndNegative,
                dialogSettings: new MetroDialogSettings()
                {
                    AffirmativeButtonText = "Yes",
                    NegativeButtonText = "No"
                });

            if (messageDialogResult == MessageDialogResult.Affirmative)
            {
                settingsHandler.Settings.TwitchAuthTokenInLivestreamerConfig = true;
                settingsHandler.SaveSettings();
                return;
            }

            messageDialogResult = await screen.ShowMessageAsync(title: "Authorization",
                message: "Would you like to authorize this application now?",
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
                    settingsHandler.Settings.TwitchAuthToken = match.Groups["token"].Value;
                    settingsHandler.SaveSettings();
                    twitchTvV5ReadonlyClient.SetAccessToken(settingsHandler.Settings.TwitchAuthToken);
                    twitchTvHelixClient.SetAccessToken(settingsHandler.Settings.TwitchAuthToken);
                    return;
                }
            }

            await screen.ShowMessageAsync("Bad url provided",
                $"Please make sure you copy the full url, it should start with '{RedirectUri}'");
        }

        public async Task<string> GetStreamUrl(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentNullException(nameof(channelId));

            var urlSuffix = await GetStreamUrlSuffixById(channelId);
            return $"{BaseUrl}{urlSuffix}/";
        }

        public async Task<string> GetChatUrl(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentNullException(nameof(channelId));

            var urlSuffix = await GetStreamUrlSuffixById(channelId);
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
                user = await twitchTvHelixClient.GetUserByUsername(newChannel.ChannelId);
            }

            if (user == null) throw new InvalidOperationException("No user found for id " + newChannel.ChannelId);

            newChannel.OverrideChannelId(user.Id);
            newChannel.DisplayName = user.DisplayName;
            channelIdToUserMap[user.Id] = user;
            var livestream = new LivestreamModel(user.Id, newChannel) { DisplayName = user.DisplayName };

            var onlineStreams = await twitchTvHelixClient.GetStreams(new GetStreamsQuery() { UserIds = new List<string>() { user.Id } });
            var onlineStream = onlineStreams.FirstOrDefault();
            if (onlineStream != null)
            {
                livestream.PopulateWithStreamDetails(onlineStream);
            }

            queryResults.Add(new LivestreamQueryResult(newChannel)
            {
                LivestreamModel = livestream
            });

            if (queryResults.All(x => x.IsSuccess))
            {
                moniteredChannels.Add(newChannel);
            }
            return queryResults;
        }

        public void AddChannelWithoutQuerying(ChannelIdentifier newChannel)
        {
            if (newChannel == null) throw new ArgumentNullException(nameof(newChannel));
            moniteredChannels.Add(newChannel);
        }

        public Task RemoveChannel(ChannelIdentifier channelIdentifier)
        {
            channelIdToUserMap.Remove(channelIdentifier.ChannelId);
            moniteredChannels.Remove(channelIdentifier);
            return Task.CompletedTask;
        }

        public async Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken)
        {
            var queryResults = new List<LivestreamQueryResult>();
            if (moniteredChannels.Count == 0) return queryResults;

            // Twitch "get streams" call only returns online streams so to determine if the stream actually exists/is still valid, we must specifically ask for channel details.
            List<Stream> onlineStreams = new List<Stream>();

            int retryCount = 0;
            bool success = false;
            while (!success && !cancellationToken.IsCancellationRequested && retryCount < 3)
            {
                try
                {
                    var query = new GetStreamsQuery();
                    query.UserIds.AddRange(moniteredChannels.Select(x => x.ChannelId));
                    onlineStreams = await twitchTvHelixClient.GetStreams(query, cancellationToken);
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

                var channelIdentifier = moniteredChannels.First(x => x.ChannelId.IsEqualTo(onlineStream.UserId));
                var gameName = await GetGameNameById(onlineStream.GameId);

                var livestream = new LivestreamModel(onlineStream.UserId, channelIdentifier);
                livestream.PopulateWithStreamDetails(onlineStream);
                livestream.Game = gameName;
                queryResults.Add(new LivestreamQueryResult(channelIdentifier)
                {
                    LivestreamModel = livestream
                });
            }

            var offlineStreams = moniteredChannels.Where(x => onlineStreams.All(y => y.UserId != x.ChannelId)).ToList();
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

        public async Task<List<VodDetails>> GetVods(VodQuery vodQuery)
        {
            if (vodQuery == null) throw new ArgumentNullException(nameof(vodQuery));
            if (string.IsNullOrWhiteSpace(vodQuery.StreamId)) throw new ArgumentNullException(nameof(vodQuery.StreamId));

            var getVideosQuery = new GetVideosQuery()
            {
                UserId = vodQuery.StreamId,
                First = vodQuery.Take
            };
            var videos = await twitchTvHelixClient.GetVideos(getVideosQuery);
            var vods = videos.Select(video =>
            {
                var largeThumbnail = video.ThumbnailTemplateUrl.Replace("%{width}", "640").Replace("%{height}", "360");
                // stupid fucking new duration format from twitch instead of just returning seconds or some other sensible value
                var match = Regex.Match(video.Duration, @"((?<hours>\d+)?h)?((?<mins>\d+)?)m?(?<secs>\d+)s");
                var hours = match.Groups["hours"].Value;
                var mins = match.Groups["mins"].Value;
                var secs = match.Groups["secs"].Value;
                var timespan = new TimeSpan(hours.ToInt(), mins.ToInt(), secs.ToInt());

                return new VodDetails
                {
                    Url = video.Url,
                    Length = timespan,
                    RecordedAt = video.CreatedAt,
                    Views = video.ViewCount,
                    //Game = video.Game,
                    Description = video.Description,
                    Title = video.Title,
                    PreviewImage = largeThumbnail,
                    ApiClient = this,
                };
            }).ToList();

            return vods;
        }

        public async Task<List<LivestreamQueryResult>> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            if (topStreamQuery == null) throw new ArgumentNullException(nameof(topStreamQuery));

            var query = new GetStreamsQuery() { First = topStreamQuery.Take };
            if (!string.IsNullOrWhiteSpace(topStreamQuery.GameName))
            {
                var gameId = await GetGameIdByName(topStreamQuery.GameName);
                query.GameIds.Add(gameId);
            }

            var topStreams = await twitchTvHelixClient.GetStreams(query);

            return topStreams.Select(x =>
            {
                var channelIdentifier = new ChannelIdentifier(this, x.UserId) { DisplayName = x.UserName };
                var queryResult = new LivestreamQueryResult(channelIdentifier);
                var livestreamModel = new LivestreamModel(x.UserId, channelIdentifier);
                livestreamModel.PopulateWithStreamDetails(x);

                queryResult.LivestreamModel = livestreamModel;
                return queryResult;
            }).ToList();
        }

        public async Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            if (string.IsNullOrEmpty(filterGameName))
            {
                var topGames = await twitchTvV5ReadonlyClient.GetTopGames();
                foreach (var topGame in topGames)
                {
                    var gameId = topGame.Game.Id.ToString();
                    gameNameToIdMap[topGame.Game.Name] = gameId;
                    gameIdToNameMap[gameId] = topGame.Game.Name;
                }

                return topGames.Select(x => new KnownGame()
                {
                    GameName = x.Game.Name,
                    ThumbnailUrls = new ThumbnailUrls()
                    {
                        Medium = x.Game.Box?.Medium?.ToString(),
                        Small = x.Game.Box?.Small?.ToString(),
                        Large = x.Game.Box?.Large?.ToString()
                    }
                }).ToList();
            }

            var twitchGames = await twitchTvV5ReadonlyClient.SearchGames(filterGameName);
            foreach (var game in twitchGames)
            {
                var gameId = game.Id.ToString();
                gameNameToIdMap[game.Name] = gameId;
                gameIdToNameMap[gameId] = game.Name;
            }
            return twitchGames.Select(x => new KnownGame()
            {
                GameName = x.Name,
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Medium = x.Box?.Medium?.ToString(),
                    Small = x.Box?.Small?.ToString(),
                    Large = x.Box?.Large?.ToString()
                }
            }).ToList();
        }

        public async Task<List<LivestreamQueryResult>> GetUserFollows(string userName)
        {
            var user = await twitchTvHelixClient.GetUserByUsername(userName);
            if (user == null) throw new InvalidOperationException("Could not find user with username: " + userName);

            channelIdToUserMap[user.Id] = user;
            var userFollows = await twitchTvHelixClient.GetUserFollows(user.Id);
            return (from follow in userFollows
                    let channelIdentifier = new ChannelIdentifier(this, follow.ToId) { DisplayName = follow.ToName }
                    select new LivestreamQueryResult(channelIdentifier)
                    {
                        LivestreamModel = new LivestreamModel(follow.ToId, channelIdentifier)
                        {
                            DisplayName = follow.ToName,
                            //Description = follow.Channel?.Status,
                            //Game = follow.Channel?.Game,
                            //IsPartner = follow.Channel?.Partner != null && follow.Channel.Partner.Value,
                            ImportedBy = userName,
                            //BroadcasterLanguage = follow.Channel?.BroadcasterLanguage
                        }
                    }).ToList();
        }

        public async Task Initialize(CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(settingsHandler.Settings.TwitchAuthToken))
            {
                twitchTvV5ReadonlyClient.SetAccessToken(settingsHandler.Settings.TwitchAuthToken);
                twitchTvHelixClient.SetAccessToken(settingsHandler.Settings.TwitchAuthToken);
            }

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
                usersQuery.UserIds.AddRange(moniteredChannels.Select(x => x.ChannelId));
                var users = await twitchTvHelixClient.GetUsers(usersQuery, cancellationToken);
                foreach (var user in users)
                {
                    channelIdToUserMap[user.Id] = user;
                }
            }
            catch
            {
                // not important enough to prevent the app from initializing if this fails
            }
        }

        private async Task<string> GetStreamUrlSuffixById(string channelId)
        {
            if (channelIdToUserMap.TryGetValue(channelId, out var user)) return user.Login;

            var query = new GetUsersQuery();
            query.UserIds.Add(channelId);
            var users = await twitchTvHelixClient.GetUsers(query);
            channelIdToUserMap[channelId] = users[0];
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
            const string scopes = "user_read+user_subscriptions";

            var request =
                "https://api.twitch.tv/kraken/oauth2/authorize?response_type=token" +
                $"&client_id={RequestConstants.ClientIdHeaderValue}" +
                $"&redirect_uri={RedirectUri}" +
                $"&scope={scopes}";

            System.Diagnostics.Process.Start(request);
        }
    }
}
