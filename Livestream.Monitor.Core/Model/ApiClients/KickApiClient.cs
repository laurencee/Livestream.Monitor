using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using ExternalAPIs.Kick;
using ExternalAPIs.Kick.Dto;
using ExternalAPIs.Kick.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients;

public class KickApiClient : IApiClient
{
    public const string API_NAME = "kick";

    private const string BaseUrl = @"https://www.kick.com/";

    private readonly IKickReadonlyClient client;
    private readonly ICacheHandler cacheHandler;
    private readonly Func<Task<AuthResponse>> requestAccessToken;
    private readonly HashSet<ChannelIdentifier> monitoredChannels = [];

    public KickApiClient(IKickReadonlyClient client, ICacheHandler cacheHandler)
        : this(client, cacheHandler, RequestAccessToken)
    {
    }

    internal KickApiClient(IKickReadonlyClient client, ICacheHandler cacheHandler, Func<Task<AuthResponse>> requestAccessToken)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.cacheHandler = cacheHandler ?? throw new ArgumentNullException(nameof(cacheHandler));
        this.requestAccessToken = requestAccessToken ?? throw new ArgumentNullException(nameof(requestAccessToken));
    }

    public string ApiName => "kick";
    public bool HasChatSupport => true;
    public bool HasVodViewerSupport => false;
    public bool HasTopStreamsSupport => true;
    public bool HasTopStreamGameFilterSupport => false;
    public bool HasFollowedChannelsQuerySupport => false;
    public bool IsAuthorized { get; private set; }
    public bool CanAuthorizeWithoutUserInput => true;
    public bool HasQuickQuerySupport => false;
    public List<string> VodTypes { get; } = [];

    public async Task Authorize(IUserDialogs screen)
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

        // Reuse the saved token until shortly before it expires.
        var renewalMargin = TimeSpan.FromDays(1);
        var saved = cacheHandler.Cache.KickAppAccessToken;
        if (!string.IsNullOrWhiteSpace(saved?.Token) && saved.ExpiresAt > DateTimeOffset.UtcNow + renewalMargin)
        {
            client.SetAccessToken(saved.Token);
            IsAuthorized = true;
            return;
        }

        await RenewAccessToken();
    }

    private async Task RenewAccessToken()
    {
        var authResponse = await requestAccessToken();
        if (string.IsNullOrWhiteSpace(authResponse?.AccessToken)) throw new InvalidDataException("Kick returned no app access token.");

        client.SetAccessToken(authResponse.AccessToken);
        IsAuthorized = true;

        var token = new KickAppAccessToken(authResponse.AccessToken, DateTimeOffset.UtcNow.AddSeconds(authResponse.ExpiresIn));
        cacheHandler.Update(cache => cache with { KickAppAccessToken = token });
    }

    private static async Task<AuthResponse> RequestAccessToken()
    {
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
        return JsonSerializer.Deserialize<AuthResponse>(content, JsonDefaults.Options);
    }

    // A token can be revoked before expiry, so renew it and retry once on 401.
    private async Task<T> WithTokenRenewal<T>(Func<Task<T>> request)
    {
        try
        {
            return await request();
        }
        catch (HttpRequestWithStatusException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
        {
            await RenewAccessToken();
            return await request();
        }
    }

    public Task<string> FindChannelIconUrl(ChannelIdentifier channel, CancellationToken cancellationToken)
    {
        if (!long.TryParse(channel.ChannelId, out var userId)) return Task.FromResult<string>(null);

        return WithTokenRenewal(() => client.FindProfilePictureUrl(userId, cancellationToken));
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

        var channelRoot = await WithTokenRenewal(() => client.GetChannels(query));

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

    public Task<List<LivestreamQueryResult>> QueryChannelsQuickly(CancellationToken cancellationToken) => QueryChannels(cancellationToken);

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
        var livestreamsRoot = await WithTokenRenewal(() => client.GetLivestreams(query));

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

        // Retry failed authorization so Kick can recover without a restart.
        if (!IsAuthorized) await Authorize(screen: null);

        var query = new GetChannelsQuery()
        {
            BroadcasterUserIds = channelIdentifiers.Select(x => x.ChannelId.ToInt()).ToList(),
        };
        var channelsRoot = await WithTokenRenewal(() => client.GetChannels(query, cancellationToken));

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
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; }
}
