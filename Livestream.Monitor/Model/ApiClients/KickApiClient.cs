using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
using ExternalAPIs.Kick;
using ExternalAPIs.Kick.Dto;
using ExternalAPIs.Kick.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;
using Newtonsoft.Json;

namespace Livestream.Monitor.Model.ApiClients;

public class KickApiClient : IApiClient
{
    public const string API_NAME = "kick";

    private const string BaseUrl = @"https://www.kick.com/";

    private readonly IKickReadonlyClient client;
    private readonly HashSet<ChannelIdentifier> monitoredChannels = [];

    public KickApiClient(IKickReadonlyClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public string ApiName => "kick";
    public bool HasChatSupport => true;
    public bool HasVodViewerSupport => false;
    public bool HasTopStreamsSupport => true;
    public bool HasTopStreamGameFilterSupport => false;
    public bool HasFollowedChannelsQuerySupport => false;
    public bool IsAuthorized { get; private set; }
    public List<string> VodTypes { get; } = [];

    public async Task Authorize(IViewAware screen)
    {
        /*
https://docs.kick.com/getting-started/generating-tokens-oauth2-flow#authorization-endpoint

GET
https://id.kick.com/oauth/authorize?
    response_type=code&
    client_id=<your_client_id>&
    redirect_uri=<https://yourapp.com/callback>&
    scope=<scopes>&
    code_challenge=<code_challenge>&
    code_challenge_method=S256&
    state=<random_value>

NOTE: code_challenge is a base64 encoded SHA256 hash of a 43-128 character string (allows A-Z, a-z, 0-9, "-._~")

    returns
    https://yourapp.com/callback?code=<code>&state=random-state
---

https://docs.kick.com/getting-started/generating-tokens-oauth2-flow#token-endpoint

POST
https://id.kick.com/oauth/token

Headers:
Content-Type: application/x-www-form-urlencoded

Body:
{
    grant_type=authorization_code
    client_id=<client_id>
    client_secret=<client_secret>
    redirect_uri=<redirect_uri>
    code_verifier=<code_verifier>
    code=<CODE>
}

ISSUE: this currently requires client secret so it's basically pointless for us to do
the only advantage would be if we could get the user's followed channels but there's no such api for that that atm
so we might as well just do machine-to-machine auth
         */

        if (IsAuthorized) return;

        var requestUri = "https://id.kick.com/oauth/token";
        byte[] data = Convert.FromBase64String("N2MzNDQyMWYzNTc2N2M5OWM3YzEyMjdlY2NjODRiYzgyNmYwYWNhNjBkMzkyNDFhZTYxZmJhZjIwYjMwNjdiNg==");
        string decodedString = Encoding.UTF8.GetString(data);
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", RequestConstants.ClientIdHeaderValue },
            { "client_secret", decodedString },
        });

        using var httpClient = HttpClientExtensions.CreateCompressionHttpClient();
        var response = await httpClient.PostAsync(requestUri, formData);
        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync();
        var authResponse = JsonConvert.DeserializeObject<AuthResponse>(content);

        client.SetAccessToken(authResponse.access_token);

        IsAuthorized = true;
    }

    public Task<string> GetStreamUrl(LivestreamModel livestreamModel) =>
        Task.FromResult($"{BaseUrl}{livestreamModel.ChannelIdentifier.DisplayName}");

    public Task<string> GetChatUrl(LivestreamModel livestreamModel)
        => Task.FromResult($"{BaseUrl}popout/{livestreamModel.ChannelIdentifier.DisplayName}/chat");

    public async Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel)
    {
        var query = new GetChannelsQuery();
        if (newChannel.DisplayName == null)
            query.Slugs = [newChannel.ChannelId];
        else
            query.BroadcasterUserIds = [newChannel.ChannelId.ToInt()];

        var channelRoot = await client.GetChannels(query);

        var channel = channelRoot.Data[0];
        newChannel.OverrideChannelId(channel.BroadcasterUserId.ToString());
        newChannel.DisplayName = channel.Slug;
        monitoredChannels.Add(newChannel);

        var queryResult = MapToLivestreamQueryResult(channel, this);
        return [queryResult];
    }

    public void AddChannelWithoutQuerying(ChannelIdentifier newChannel)
    {
        monitoredChannels.Add(newChannel);
    }

    public Task RemoveChannel(ChannelIdentifier channelIdentifier)
    {
        monitoredChannels.Remove(channelIdentifier);
        return Task.CompletedTask;
    }

    public Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken) =>
        QueryChannels(monitoredChannels, cancellationToken);

    public Task<IReadOnlyCollection<VodDetails>> GetVods(VodQuery vodQuery) =>
        throw new NotImplementedException();

    public async Task<TopStreamsResponse> GetTopStreams(TopStreamQuery topStreamQuery)
    {
        if (!IsAuthorized) return new TopStreamsResponse();

        var query = new GetLivestreamsQuery()
        {
            Sort = LivestreamsSort.ViewerCount,
            Limit = topStreamQuery.Take,
            // todo categoryid lookup from topStreamQuery.GameName
        };
        var livestreamsRoot = await client.GetLivestreams(query);

        var livestreamModels = livestreamsRoot.Data.Select(x =>
        {
            var broadcasterUserId = x.BroadcasterUserId.ToString();
            var channelIdentifier = new ChannelIdentifier(this, broadcasterUserId)
            {
                DisplayName = x.Slug,
            };
            return new LivestreamModel(broadcasterUserId, channelIdentifier)
            {
                DisplayName = x.Slug,
                Description = x.StreamTitle,
                Game = x.Category.Name,
                Viewers = x.ViewerCount,
                StartTime = x.StartedAt,
                Language = x.Language,
                BroadcasterLanguage = x.Language,
                Live = true,
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Large = x.Thumbnail,
                    Medium = x.Thumbnail,
                    Small = x.Thumbnail,
                },
            };
        }).ToList();

        return new TopStreamsResponse()
        {
            LivestreamModels = livestreamModels,
            HasNextPage = false,
        };
    }

    // TODO - use /categories endpoint when it's working
    public Task<List<KnownGame>> GetKnownGameNames(string filterGameName) =>
        throw new NotImplementedException();

    public Task<List<LivestreamQueryResult>> GetFollowedChannels(string userName) =>
        throw new NotImplementedException();

    public Task<InitializeApiClientResult> Initialize(CancellationToken cancellationToken = default) =>
        Task.FromResult(new InitializeApiClientResult());

    private async Task<List<LivestreamQueryResult>> QueryChannels(
        IReadOnlyCollection<ChannelIdentifier> channelIdentifiers,
        CancellationToken cancellationToken)
    {
        if (channelIdentifiers.Count == 0) return [];
        if (!IsAuthorized) return [];

        var query = new GetChannelsQuery()
        {
            BroadcasterUserIds = channelIdentifiers.Select(x => x.ChannelId.ToInt()).ToList(),
        };
        var channelsRoot = await client.GetChannels(query, cancellationToken);

        var queryResults = new List<LivestreamQueryResult>();
        foreach (var channel in channelsRoot.Data)
        {
            var livestreamQueryResult = MapToLivestreamQueryResult(channel, this);
            queryResults.Add(livestreamQueryResult);
        }

        return queryResults;
    }

    private static LivestreamQueryResult MapToLivestreamQueryResult(Channel channel, KickApiClient apiClient)
    {
        var broadcasterUserId = channel.BroadcasterUserId.ToString();
        var channelIdentifier = new ChannelIdentifier(apiClient, broadcasterUserId)
        {
            DisplayName = channel.Slug,
        };
        var livestreamQueryResult = new LivestreamQueryResult(channelIdentifier)
        {
            LivestreamModel = new LivestreamModel(broadcasterUserId, channelIdentifier)
            {
                DisplayName = channel.Slug,
                Description = channel.StreamTitle,
                Game = channel.Category.Name,
                Viewers = channel.Stream.ViewerCount,
                StartTime = channel.Stream.StartTime,
                Language = channel.Stream.Language,
                BroadcasterLanguage = channel.Stream.Language,
                Live = channel.Stream.IsLive,
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Large = channel.Stream.Thumbnail,
                    Medium = channel.Stream.Thumbnail,
                    Small = channel.Stream.Thumbnail,
                },
            },
        };
        return livestreamQueryResult;
    }
}

public class AuthResponse
{
    // ReSharper disable InconsistentNaming
    public string access_token { get; set; }
    public int expires_in { get; set; }
    public string token_type { get; set; }
    // ReSharper restore InconsistentNaming
}
