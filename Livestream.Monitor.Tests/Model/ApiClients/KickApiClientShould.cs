using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using ExternalAPIs.Kick;
using ExternalAPIs.Kick.Dto;
using ExternalAPIs.Kick.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Model.ApiClients;

public sealed class KickApiClientShould : IDisposable
{
    private readonly IKickReadonlyClient client = Substitute.For<IKickReadonlyClient>();
    private readonly string cachePath = Path.Combine(Path.GetTempPath(), $"Livestream.Monitor.Cache-{Guid.NewGuid():N}.bin");
    private readonly CacheHandler cacheHandler;
    private int tokenRequests;
    private Exception tokenFailure;

    public KickApiClientShould() => cacheHandler = new CacheHandler(cachePath);

    public void Dispose() => File.Delete(cachePath);

    // A saved token is reused until it is empty or about to expire.
    [Theory]
    [InlineData("saved", 720, "saved")]
    [InlineData("saved", 1, "new")]
    [InlineData("", 720, "new")]
    public async Task ReuseASavedTokenOrRequestAndSaveANewOneWhenItIsUnusable(string savedToken, int hoursUntilExpiry, string expectedToken)
    {
        SaveToken(savedToken, DateTimeOffset.UtcNow.AddHours(hoursUntilExpiry));
        var apiClient = CreateApiClient();

        await apiClient.Authorize(screen: null);

        Assert.True(apiClient.IsAuthorized);
        client.Received(1).SetAccessToken(expectedToken);
        var saved = new CacheHandler(cachePath).Cache.KickAppAccessToken;
        Assert.Equal(expectedToken, saved.Token);

        if (expectedToken == savedToken)
        {
            Assert.Equal(0, tokenRequests);
            return;
        }

        Assert.Equal(1, tokenRequests);
        Assert.True(saved.ExpiresAt > DateTimeOffset.UtcNow.AddDays(59));
    }

    [Fact]
    public async Task RenewARejectedTokenOnceAndRetryTheQuery()
    {
        SaveToken("revoked", DateTimeOffset.UtcNow.AddDays(30));
        var apiClient = CreateApiClient();
        await apiClient.Authorize(screen: null);
        apiClient.AddChannelWithoutQuerying(new ChannelIdentifier(apiClient, "1"));

        var unauthorized = new HttpRequestWithStatusException(HttpStatusCode.Unauthorized, "Unauthorized");
        var channels = new ChannelsRoot { Data = [CreateChannel()] };
        client.GetChannels(Arg.Any<GetChannelsQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw unauthorized, _ => Task.FromResult(channels));

        var results = await apiClient.QueryChannels(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(1, tokenRequests);
        client.Received(1).SetAccessToken("new");
        Assert.Equal("new", cacheHandler.Cache.KickAppAccessToken.Token);
    }

    [Fact]
    public async Task RetryAFailedAuthorizationOnTheNextQuery()
    {
        var apiClient = CreateApiClient();
        tokenFailure = new HttpRequestException("token endpoint down");
        await Assert.ThrowsAsync<HttpRequestException>(() => apiClient.Authorize(screen: null));

        apiClient.AddChannelWithoutQuerying(new ChannelIdentifier(apiClient, "1"));
        var channels = new ChannelsRoot { Data = [CreateChannel()] };
        client.GetChannels(Arg.Any<GetChannelsQuery>(), Arg.Any<CancellationToken>()).Returns(channels);

        var results = await apiClient.QueryChannels(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(2, tokenRequests);
        Assert.True(apiClient.IsAuthorized);
    }

    private void SaveToken(string token, DateTimeOffset expiresAt) =>
        cacheHandler.Update(cache => cache with { KickAppAccessToken = new KickAppAccessToken(token, expiresAt) });

    private KickApiClient CreateApiClient()
    {
        const int TokenLifetimeSeconds = 5184000;
        return new KickApiClient(client, cacheHandler, RequestAccessToken);

        Task<AuthResponse> RequestAccessToken()
        {
            tokenRequests++;
            if (tokenFailure is { } failure)
            {
                tokenFailure = null;
                return Task.FromException<AuthResponse>(failure);
            }

            return Task.FromResult(new AuthResponse { AccessToken = "new", ExpiresIn = TokenLifetimeSeconds });
        }
    }

    private static Channel CreateChannel() => new()
    {
        BroadcasterUserId = 1,
        Slug = "channel",
        Category = new Category(),
        Stream = new StreamDetails(),
    };
}
