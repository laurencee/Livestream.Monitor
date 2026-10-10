using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.GitHub;
using ExternalAPIs.Kick.Dto;
using ExternalAPIs.TwitchTv.Helix.Dto;
using Xunit;

namespace ExternalAPIs.Tests;

public class JsonResponsesShould
{
    [Fact]
    public async Task PreserveTwitchWireNamesNumbersAndTimestamps()
    {
        const string Response = """
            {"data":[{"id":"live","user_id":"42","user_name":"channel","game_name":"Game",
              "viewer_count":"1234","started_at":"2026-10-08T12:34:56Z","thumbnail_url":"https://example.test/{width}x{height}.jpg"}],
             "pagination":{"cursor":"next"}}
            """;
        var response = await Read<StreamsRoot>(Response);
        var stream = Assert.Single(response.Streams);
        Assert.Equal("42", stream.UserId);
        Assert.Equal("channel", stream.UserName);
        Assert.Equal("Game", stream.GameName);
        Assert.Equal(1234, stream.ViewerCount);
        Assert.Equal("next", response.Pagination.Cursor);
        Assert.Equal(new DateTimeOffset(
            year: 2026,
            month: 10,
            day: 8,
            hour: 12,
            minute: 34,
            second: 56,
            offset: TimeSpan.Zero
        ), stream.StartedAt);
    }

    [Fact]
    public async Task PreserveKickWireNamesAndNullableDates()
    {
        const string Response = """
            {"data":[{"broadcaster_user_id":42,"slug":"channel","stream":{"is_live":true,"start_time":null,"viewer_count":12}}],"message":"OK"}
            """;
        var response = await Read<ChannelsRoot>(Response);
        var channel = Assert.Single(response.Data);
        Assert.Equal(42, channel.BroadcasterUserId);
        Assert.True(channel.Stream.IsLive);
        Assert.Null(channel.Stream.StartTime);
        Assert.Equal(12, channel.Stream.ViewerCount);
    }

    [Fact]
    public async Task PreserveReleaseWireNames()
    {
        var response = await Read<Release>("""{"tag_name":"2.17.2","html_url":"https://example.test/release"}""");
        Assert.Equal("2.17.2", response.TagName);
        Assert.Equal("https://example.test/release", response.HtmlUrl);
    }

    [Fact]
    public async Task ReportTheStatusOfAFailedResponseWithoutABody()
    {
        var exception = await Assert.ThrowsAsync<HttpRequestWithStatusException>(() => Read<Release>("", HttpStatusCode.ServiceUnavailable));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("503", exception.Message);
    }

    private static async Task<T> Read<T>(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        using var client = new HttpClient(new ResponseHandler(json, statusCode));
        return await client.ExecuteRequest<T>("https://example.test/response");
    }

    private sealed class ResponseHandler(string json, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new StringContent(json) };
            return Task.FromResult(response);
        }
    }
}
