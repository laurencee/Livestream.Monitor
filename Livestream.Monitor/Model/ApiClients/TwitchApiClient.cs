using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
using ExternalAPIs.TwitchTv.V3;
using ExternalAPIs.TwitchTv.V3.Dto;
using ExternalAPIs.TwitchTv.V3.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;
using MahApps.Metro.Controls.Dialogs;

namespace Livestream.Monitor.Model.ApiClients
{
    public class TwitchApiClient : IApiClient
    {
        public const string API_NAME = "twitchtv";
        private const string BroadcastVodType = "Broadcasts";
        private const string HighlightVodType = "Highlights";
        private const string RedirectUri = @"https://github.com/laurencee/Livestream.Monitor";

        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly ISettingsHandler settingsHandler;
        private readonly HashSet<ChannelIdentifier> moniteredChannels = new HashSet<ChannelIdentifier>();
        private readonly List<LivestreamQueryResult> offlineQueryResultsCache = new List<LivestreamQueryResult>();

        // 1 time per application run
        private bool queryAllStreams = true;

        public TwitchApiClient(ITwitchTvReadonlyClient twitchTvClient, ISettingsHandler settingsHandler)
        {
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));

            this.twitchTvClient = twitchTvClient;
            this.settingsHandler = settingsHandler;
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
                var match = Regex.Match(input, "#access_token=(?<token>.*)&");
                if (match.Groups["token"].Success)
                {
                    settingsHandler.Settings.TwitchAuthToken = match.Groups["token"].Value;
                    settingsHandler.SaveSettings();
                    return;
                }
            }

            await screen.ShowMessageAsync("Bad url provided",
                $"Please make sure you copy the full url, it should start with '{RedirectUri}'");
        }

        public string GetStreamUrl(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentNullException(nameof(channelId));

            return $"{BaseUrl}{channelId}/";
        }

        public string GetChatUrl(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentNullException(nameof(channelId));

            return $"{BaseUrl}{channelId}/chat?popout=true";
        }

        public async Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel)
        {
            if (newChannel == null) throw new ArgumentNullException(nameof(newChannel));

            // shorter implementation of QueryChannels
            var queryResults = new List<LivestreamQueryResult>();
            var onlineStreams = await twitchTvClient.GetStreamsDetails(new[] { newChannel.ChannelId });
            var onlineStream = onlineStreams.FirstOrDefault();
            if (onlineStream != null)
            {
                var livestream = new LivestreamModel(onlineStream.Channel?.Name, newChannel);
                livestream.PopulateWithChannel(onlineStream.Channel);
                livestream.PopulateWithStreamDetails(onlineStream);
                queryResults.Add(new LivestreamQueryResult(newChannel)
                {
                    LivestreamModel = livestream
                });
            }

            // we always need to check for offline channels when attempting to add a channel for the first time
            // this is the only way to detect non-existant/banned channels
            var offlineStreams = await GetOfflineStreamQueryResults(new[] { newChannel }, CancellationToken.None);
            if (onlineStream == null || offlineStreams.Any(x => !x.IsSuccess))
                queryResults.AddRange(offlineStreams);
            else
                offlineStreams.Clear();

            if (queryResults.All(x => x.IsSuccess))
            {
                moniteredChannels.Add(newChannel);
                offlineQueryResultsCache.AddRange(offlineStreams.Where(x => x.IsSuccess));
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
            // we keep our offline query results cached so we don't need to re-query them again this application run
            // as such, we must cleanup any previous query result that has the channel identifier being removed
            var queryResult = offlineQueryResultsCache.FirstOrDefault(x => Equals(channelIdentifier, x.ChannelIdentifier));
            if (queryResult != null) offlineQueryResultsCache.Remove(queryResult);

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
                    onlineStreams = await twitchTvClient.GetStreamsDetails(moniteredChannels.Select(x => x.ChannelId), cancellationToken);
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

                var channelIdentifier = moniteredChannels.First(x => x.ChannelId.IsEqualTo(onlineStream.Channel?.Name));
                var livestream = new LivestreamModel(onlineStream.Channel?.Name, channelIdentifier);
                livestream.PopulateWithChannel(onlineStream.Channel);
                livestream.PopulateWithStreamDetails(onlineStream);
                queryResults.Add(new LivestreamQueryResult(channelIdentifier)
                {
                    LivestreamModel = livestream
                });

                // remove cached offline query result if it exists
                var cachedOfflineResult = offlineQueryResultsCache.FirstOrDefault(x => x.ChannelIdentifier.Equals(livestream.ChannelIdentifier));
                if (cachedOfflineResult != null) offlineQueryResultsCache.Remove(cachedOfflineResult);
            }

            // As offline stream querying is expensive due to no bulk call, we only do it once for the majority of streams per application run.
            var offlineChannels = moniteredChannels.Where(x => onlineStreams.All(y => !y.Channel.Name.IsEqualTo(x.ChannelId))).ToList();
            if (queryAllStreams)
            {
                var offlineStreams = await GetOfflineStreamQueryResults(offlineChannels, cancellationToken);
                // only treat offline streams as being queried if no cancel occurred
                if (!cancellationToken.IsCancellationRequested)
                {
                    offlineQueryResultsCache.AddRange(offlineStreams);
                    queryAllStreams = false;
                }
            }
            else // we also need to query stream information for streams which have gone offline since our last query
            {
                var newlyOfflineStreams = offlineChannels.Except(offlineQueryResultsCache.Select(x => x.ChannelIdentifier)).ToList();
                if (newlyOfflineStreams.Any())
                {
                    var offlineStreams = await GetOfflineStreamQueryResults(newlyOfflineStreams, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        offlineQueryResultsCache.AddRange(offlineStreams);
                    }
                }
            }

            foreach (var offlineQueryResult in offlineQueryResultsCache.Except(queryResults).Where(x => x.IsSuccess))
            {
                offlineQueryResult.LivestreamModel.Offline();
            }

            queryResults.AddRange(offlineQueryResultsCache);
            return queryResults;
        }

        public async Task<List<VodDetails>> GetVods(VodQuery vodQuery)
        {
            if (vodQuery == null) throw new ArgumentNullException(nameof(vodQuery));
            if (string.IsNullOrWhiteSpace(vodQuery.StreamId)) throw new ArgumentNullException(nameof(vodQuery.StreamId));

            var channelVideosQuery = new ChannelVideosQuery()
            {
                ChannelName = vodQuery.StreamId,
                Take = vodQuery.Take,
                Skip = vodQuery.Skip,
                HLSVodsOnly = true,
                BroadcastsOnly = vodQuery.VodTypes.Contains(BroadcastVodType)
            };
            var channelVideos = await twitchTvClient.GetChannelVideos(channelVideosQuery);
            var vods = channelVideos.Select(channelVideo => new VodDetails
            {
                // hack to correct the url path for twitch videos see github issue: https://github.com/laurencee/Livestream.Monitor/issues/24
                // was previously   twitch.tv/videos/XXXXXXX
                // now is           twitch.tv/user/v/XXXXXXX
                Url = channelVideo.Url.Replace(@"twitch.tv/videos/", @"twitch.tv/user/v/"),
                Length = TimeSpan.FromSeconds(channelVideo.Length),
                RecordedAt = channelVideo.RecordedAt ?? DateTimeOffset.MinValue,
                Views = channelVideo.Views,
                Game = channelVideo.Game,
                Description = channelVideo.Description,
                Title = channelVideo.Title,
                PreviewImage = channelVideo.Preview,
                ApiClient = this,
            }).ToList();

            return vods;
        }

        public async Task<List<LivestreamQueryResult>> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            if (topStreamQuery == null) throw new ArgumentNullException(nameof(topStreamQuery));
            var topStreams = await twitchTvClient.GetTopStreams(topStreamQuery);

            return topStreams.Select(x =>
            {
                var channelIdentifier = new ChannelIdentifier(this, x.Channel.Name);
                var queryResult = new LivestreamQueryResult(channelIdentifier);
                var livestreamModel = new LivestreamModel(x.Channel?.Name, channelIdentifier);
                livestreamModel.PopulateWithStreamDetails(x);
                livestreamModel.PopulateWithChannel(x.Channel);

                queryResult.LivestreamModel = livestreamModel;
                return queryResult;
            }).ToList();
        }

        public async Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            if (string.IsNullOrEmpty(filterGameName))
            {
                return (await twitchTvClient.GetTopGames()).Select(x => new KnownGame()
                {
                    GameName = x.Game.Name,
                    ThumbnailUrls = new ThumbnailUrls()
                    {
                        Medium = x.Game.Logo?.Medium,
                        Small = x.Game.Logo?.Small,
                        Large = x.Game.Logo?.Large
                    }
                }).ToList();
            }

            var twitchGames = await twitchTvClient.SearchGames(filterGameName);
            return twitchGames.Select(x => new KnownGame()
            {
                GameName = x.Name,
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Medium = x.Logo?.Medium,
                    Small = x.Logo?.Small,
                    Large = x.Logo?.Large
                }
            }).ToList();
        }

        public async Task<List<LivestreamQueryResult>> GetUserFollows(string userName)
        {
            var userFollows = await twitchTvClient.GetUserFollows(userName);
            return (from follow in userFollows.Follows
                    let channelIdentifier = new ChannelIdentifier(this, follow.Channel.Name)
                    select new LivestreamQueryResult(channelIdentifier)
                    {
                        LivestreamModel = new LivestreamModel(follow.Channel?.Name, channelIdentifier)
                        {
                            DisplayName = follow.Channel?.Name,
                            Description = follow.Channel?.Status,
                            Game = follow.Channel?.Game,
                            IsPartner = follow.Channel?.Partner != null && follow.Channel.Partner.Value,
                            ImportedBy = userName,
                            BroadcasterLanguage = follow.Channel?.BroadcasterLanguage
                        }
                    }).ToList();
        }

        private async Task<List<LivestreamQueryResult>> GetOfflineStreamQueryResults(
            IEnumerable<ChannelIdentifier> offlineChannels,
            CancellationToken cancellationToken)
        {
            return await offlineChannels.ExecuteInParallel(async channelIdentifier =>
            {
                var queryResult = new LivestreamQueryResult(channelIdentifier);
                try
                {
                    var channel = await twitchTvClient.GetChannelDetails(channelIdentifier.ChannelId, cancellationToken);
                    queryResult.LivestreamModel = new LivestreamModel(channelIdentifier.ChannelId, channelIdentifier);
                    queryResult.LivestreamModel.PopulateWithChannel(channel);
                    queryResult.LivestreamModel.Offline();
                }
                catch (Exception ex)
                {
                    queryResult.FailedQueryException = new FailedQueryException(channelIdentifier, ex);
                }

                return queryResult;
            }, Constants.HalfRefreshPollingTime, cancellationToken);
        }

        /// <summary> Launches browser for user to authorize us </summary>
        private void RequestAuthorization()
        {
            const string scopes = "user_read+channel_read";

            var request =
                "https://api.twitch.tv/kraken/oauth2/authorize?response_type=token" +
                $"&client_id={RequestConstants.ClientIdHeaderValue}" +
                $"&redirect_uri={RedirectUri}" +
                $"&scope={scopes}";

            System.Diagnostics.Process.Start(request);
        }
    }
}
