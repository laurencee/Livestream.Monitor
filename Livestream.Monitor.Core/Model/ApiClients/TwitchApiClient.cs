using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using ExternalAPIs.TwitchTv.Helix;
using ExternalAPIs.TwitchTv.Helix.Dto;
using ExternalAPIs.TwitchTv.Helix.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model.Monitoring;
using RequestConstants = ExternalAPIs.TwitchTv.Helix.RequestConstants;

namespace Livestream.Monitor.Model.ApiClients
{
    public class TwitchApiClient(
        ITwitchTvHelixReadonlyClient twitchTvHelixClient,
        ISettingsHandler settingsHandler)
        : IApiClient
    {
        public const string API_NAME = "twitchtv";

        private static class VodType
        {
            public const string Archive = "archive";
            public const string Highlight = "highlight";
            public const string Upload = "upload";
            public const string All = "all";
        }

        private const string RedirectUri = @"https://github.com/laurencee/Livestream.Monitor";
        private const string BaseUrl = @"https://www.twitch.tv/";

        private readonly ITwitchTvHelixReadonlyClient twitchTvHelixClient = twitchTvHelixClient ?? throw new ArgumentNullException(nameof(twitchTvHelixClient));
        private readonly ISettingsHandler settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
        private readonly HashSet<ChannelIdentifier> monitoredChannels = [];
        private readonly Dictionary<string, string> gameNameToIdMap = new();
        private readonly Dictionary<string, User> streamDisplayNameToUserMap = new();
        private readonly Dictionary<TopStreamQuery, string> topStreamsPaginationKeyMap = new();
        private readonly Dictionary<VodQuery, string> vodsPaginationKeyMap = new();

        public string ApiName => API_NAME;

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => true;

        public bool HasTopStreamGameFilterSupport => true;

        public bool HasFollowedChannelsQuerySupport => true;

        public bool IsAuthorized => settingsHandler.Settings.Twitch.IsAuthTokenSet || settingsHandler.Settings.Twitch.PassthroughClientId;

        public List<string> VodTypes { get; } =
        [
            VodType.Archive,
            VodType.Highlight,
            VodType.Upload
        ];

        public async Task Authorize(IUserDialogs screen)
        {
            var authorize = await screen.Confirm("Twitch Authorization",
                "Twitch requires authorization for querying livestreams.\nWould you like to authorize this application now?",
                affirmative: "Yes",
                negative: "No");
            if (!authorize) return;

            RequestAuthorization();

            var input = await screen.Prompt(title: "Authorization Token",
                message:
                "Once you've approved me, please copy the full redirect url to here so we can save your access token for future use." +
                Environment.NewLine +
                Environment.NewLine +
                $"*Note* The url should start with '{RedirectUri}'");

            if (string.IsNullOrWhiteSpace(input))
            {
                await screen.ShowMessage("Authorization cancelled", "Authorization process was cancelled.");
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

            await screen.ShowMessage("Bad url provided",
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
            if (long.TryParse(newChannel.ChannelId, out _))
            {
                var users = await twitchTvHelixClient.GetUsers(new GetUsersQuery() { UserIds = [newChannel.ChannelId] });
                user = users.FirstOrDefault();
            }
            else
            {
                user = await FindUser(newChannel.ChannelId);
            }

            if (user == null) throw new InvalidOperationException("No user found for id " + newChannel.ChannelId);

            newChannel.OverrideChannelId(user.Id);
            newChannel.DisplayName = user.DisplayName;
            var livestream = new LivestreamModel(user.Id, newChannel) { DisplayName = user.DisplayName };

            var streamsRoot = await twitchTvHelixClient.GetStreams(new GetStreamsQuery() { UserIds = [user.Id] });
            var onlineStream = streamsRoot.Streams.FirstOrDefault();
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
            List<Stream> onlineStreams = [];

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

            cancellationToken.ThrowIfCancellationRequested();
            if (!success)
                throw new HttpRequestWithStatusException(HttpStatusCode.ServiceUnavailable, "Twitch remained unavailable after retries.");

            foreach (var onlineStream in onlineStreams)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var channelIdentifier = monitoredChannels.First(x => x.ChannelId.IsEqualTo(onlineStream.UserId));

                channelIdentifier.DisplayName = onlineStream.UserName;
                var livestream = new LivestreamModel(onlineStream.UserId, channelIdentifier);
                livestream.PopulateWithStreamDetails(onlineStream);
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

            var user = await FindUser(vodQuery.StreamDisplayName);
            if (user == null) return new List<VodDetails>();

            vodsPaginationKeyMap.TryGetValue(vodQuery, out var paginationKey);
            var vodType = MapToHelixVodType(vodQuery.VodTypes);
            var getVideosQuery = new GetVideosQuery()
            {
                UserId = user.Id,
                First = vodQuery.Take,
                Type = vodType,
                CursorPagination = new CursorPagination()
                {
                    After = paginationKey
                }
            };
            var videosRoot = await twitchTvHelixClient.GetVideos(getVideosQuery);

            var nextPageKeyLookup = new VodQuery()
            {
                StreamDisplayName = vodQuery.StreamDisplayName,
                Skip = vodQuery.Skip + vodQuery.Take,
                Take = vodQuery.Take,
                VodTypes = vodQuery.VodTypes,
            };
            vodsPaginationKeyMap[nextPageKeyLookup] = videosRoot.Pagination.Cursor;

            // fallback for active live streams that don't populate the current stream vod thumbnail correctly
            string liveThumbnailTemplate = null;
            if (videosRoot.Videos.Any(video => IsProcessingVodThumbnail(video.ThumbnailTemplateUrl)))
            {
                var streamsRoot = await twitchTvHelixClient.GetStreams(new GetStreamsQuery()
                {
                    UserIds = [user.Id],
                });
                liveThumbnailTemplate = streamsRoot.Streams.FirstOrDefault()?.ThumbnailTemplateUrl;
            }

            var vods = videosRoot.Videos.Select(video =>
            {
                var thumbnailTemplate = video.ThumbnailTemplateUrl;
                if (IsProcessingVodThumbnail(thumbnailTemplate) && !string.IsNullOrWhiteSpace(liveThumbnailTemplate))
                    thumbnailTemplate = liveThumbnailTemplate;

                var largeThumbnail = ResolveLargeThumbnailUrl(thumbnailTemplate);
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
            if (twitchGames == null) return [];

            foreach (var game in twitchGames)
            {
                var gameId = game.Id;
                gameNameToIdMap[game.Name] = gameId;
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
            var user = await FindUser(userName);
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

        public Task<InitializeApiClientResult> Initialize(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            twitchTvHelixClient.SetAccessToken(settingsHandler.Settings.Twitch.AuthToken);
            // User details are loaded on demand by GetStreamUrlSuffix/FindUser;
            // the saved display names are enough to start querying live streams.
            return Task.FromResult(new InitializeApiClientResult());
        }

        private async Task<string> GetStreamUrlSuffix(ChannelIdentifier channel)
        {
            var user = await FindUser(channel);
            if (user == null)
                throw new InvalidOperationException($"Channel {channel.DisplayName} does not exist (likely suspended or deleted)");

            return user.Login;
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
            }

            return gameId;
        }

        /// <summary> Launches browser for user to authorize us </summary>
        private void RequestAuthorization()
        {
            const string Scopes = "user:read:follows";

            var request =
                "https://id.twitch.tv/oauth2/authorize?response_type=token" +
                $"&client_id={RequestConstants.ClientIdHeaderValue}" +
                $"&redirect_uri={RedirectUri}" +
                $"&scope={Scopes}";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(request) { UseShellExecute = true });
        }

        private async Task<User> FindUser(string name)
        {
            // Saved display names may be localized or renamed; resolve monitored channels by ID.
            var channel = monitoredChannels.FirstOrDefault(x => x.ChannelId == name || x.DisplayName.IsEqualTo(name));
            if (channel != null) return await FindUser(channel);
            if (streamDisplayNameToUserMap.TryGetValue(name, out var cachedUser)) return cachedUser;

            var user = await twitchTvHelixClient.GetUserByUsername(name);
            if (user != null) streamDisplayNameToUserMap[name] = user;
            return user;
        }

        private async Task<User> FindUser(ChannelIdentifier channel)
        {
            var cacheKey = channel.DisplayName ?? channel.ChannelId;
            if (streamDisplayNameToUserMap.TryGetValue(cacheKey, out var cachedUser) && cachedUser.Id == channel.ChannelId)
                return cachedUser;

            var query = new GetUsersQuery { UserIds = [channel.ChannelId] };
            var users = await twitchTvHelixClient.GetUsers(query);
            var user = users.FirstOrDefault();
            if (user == null) return null;

            // Retain the original alias for VOD pagination already open under the saved name.
            streamDisplayNameToUserMap[cacheKey] = user;
            streamDisplayNameToUserMap[user.DisplayName] = user;
            channel.DisplayName = user.DisplayName;
            return user;
        }

        private static string MapToHelixVodType(IEnumerable<string> vodTypes)
        {
            var selectedVodType = vodTypes?.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(selectedVodType)) return null;

            if (selectedVodType.IsEqualTo(VodType.Archive)) return VodType.Archive;
            if (selectedVodType.IsEqualTo(VodType.Highlight)) return VodType.Highlight;
            if (selectedVodType.IsEqualTo(VodType.Upload)) return VodType.Upload;
            if (selectedVodType.IsEqualTo(VodType.All)) return VodType.All;

            return null;
        }

        private static bool IsProcessingVodThumbnail(string thumbnailTemplateUrl)
        {
            if (string.IsNullOrWhiteSpace(thumbnailTemplateUrl)) return false;
            return thumbnailTemplateUrl.IndexOf("404_processing_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ResolveLargeThumbnailUrl(string thumbnailTemplateUrl)
        {
            if (string.IsNullOrWhiteSpace(thumbnailTemplateUrl)) return null;

            return thumbnailTemplateUrl
                .Replace("%{width}", "640")
                .Replace("%{height}", "360")
                .Replace("{width}", "640")
                .Replace("{height}", "360");
        }
    }
}
