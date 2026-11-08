using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using ExternalAPIs.TwitchTv.Helix;
using ExternalAPIs.TwitchTv.Helix.Dto;
using ExternalAPIs.TwitchTv.Helix.Query;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Model.ApiClients;

public sealed class TwitchApiClientShould
{
    private const string ChannelId = "12345";
    private readonly ITwitchTvHelixReadonlyClient client = Substitute.For<ITwitchTvHelixReadonlyClient>();
    private readonly TwitchApiClient sut;

    public TwitchApiClientShould()
    {
        sut = new TwitchApiClient(client, Substitute.For<ISettingsHandler>());
        client.GetUsers(Arg.Any<GetUsersQuery>(), Arg.Any<CancellationToken>()).Returns(new List<User>());
    }

    [Theory]
    [InlineData("Saved_Name42", null, "saved_name42")]
    [InlineData("x", null, "x")]
    [InlineData("한동숙", "known_login", "known_login")]
    [InlineData("OldName", "known_login", "known_login")]
    public async Task UseTheSavedNameOrLoginForUrlsWithoutLookingUpTheAccount(string displayName, string savedLogin, string expectedLogin)
    {
        var stream = CreateStream(displayName);
        stream.ChannelIdentifier.TwitchLogin = savedLogin;

        Assert.Equal($"https://www.twitch.tv/{expectedLogin}/", await stream.GetStreamUrl());
        Assert.Equal($"https://www.twitch.tv/{expectedLogin}/chat?popout=true", await stream.GetChatUrl());
        await client.DidNotReceive().GetUsers(Arg.Any<GetUsersQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("한동숙")]
    [InlineData("bad/name")]
    [InlineData("name\n")]
    public async Task ReportAnUnavailableAccountWhenItsLoginCannotBeRecovered(string displayName)
    {
        var stream = CreateStream(displayName);

        await Assert.ThrowsAsync<InvalidOperationException>(stream.GetStreamUrl);
        await Assert.ThrowsAsync<InvalidOperationException>(stream.GetChatUrl);
    }

    [Fact]
    public async Task RecoverAMissingLocalizedLoginByChannelIdAndRetainIt()
    {
        var stream = CreateStream("한동숙");
        var users = new List<User> { new() { Id = ChannelId, Login = "current_login", DisplayName = "Current_Login" } };
        client.GetUsers(Arg.Is<GetUsersQuery>(query => query.UserIds.Contains(ChannelId)), Arg.Any<CancellationToken>()).Returns(users);

        Assert.Equal("https://www.twitch.tv/current_login/", await stream.GetStreamUrl());
        Assert.Equal("https://www.twitch.tv/current_login/chat?popout=true", await stream.GetChatUrl());
        Assert.Equal("current_login", stream.ChannelIdentifier.TwitchLogin);

        var restoredStream = CreateStream("한동숙");
        Assert.Equal("https://www.twitch.tv/current_login/", await restoredStream.GetStreamUrl());
        Assert.Equal("current_login", restoredStream.ChannelIdentifier.TwitchLogin);
        await client.Received(1).GetUsers(Arg.Any<GetUsersQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreserveAccountLookupFailures()
    {
        var stream = CreateStream("한동숙");
        var failure = new HttpRequestException("Twitch is unavailable.");
        client.GetUsers(Arg.Any<GetUsersQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<List<User>>(failure));

        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(stream.GetStreamUrl));
        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(stream.GetChatUrl));
    }

    [Fact]
    public async Task RetainTheLoginWhenAddingAChannel()
    {
        var users = new List<User> { new() { Id = ChannelId, Login = "known_login", DisplayName = "한동숙" } };
        client.GetUsers(Arg.Any<GetUsersQuery>(), Arg.Any<CancellationToken>()).Returns(users);
        client.GetStreams(Arg.Any<GetStreamsQuery>(), Arg.Any<CancellationToken>()).Returns(new StreamsRoot { Streams = [] });

        var result = Assert.Single(await sut.AddChannel(new ChannelIdentifier(sut, ChannelId)));
        client.ClearReceivedCalls();

        Assert.Equal("https://www.twitch.tv/known_login/", await result.LivestreamModel.GetStreamUrl());
        Assert.Equal("known_login", result.ChannelIdentifier.TwitchLogin);
        await client.DidNotReceive().GetUsers(Arg.Any<GetUsersQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetainTheLoginsOfImportedChannels()
    {
        client.GetUserByUsername("importer", Arg.Any<CancellationToken>())
            .Returns(new User { Id = "importer-id", Login = "importer", DisplayName = "Importer" });
        List<FollowedChannel> follows = [new() { BroadcasterId = ChannelId, BroadcasterLogin = "known_login", BroadcasterName = "한동숙" }];
        client.GetFollowedChannels("importer-id", Arg.Any<CancellationToken>()).Returns(follows);

        var result = Assert.Single(await sut.GetFollowedChannels("importer"));

        Assert.Equal("https://www.twitch.tv/known_login/", await result.LivestreamModel.GetStreamUrl());
        Assert.Equal("known_login", result.ChannelIdentifier.TwitchLogin);
        await client.DidNotReceive().GetUsers(Arg.Any<GetUsersQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateTheKnownLoginFromLivePollingAfterARename()
    {
        var stream = CreateStream("OldName");
        stream.ChannelIdentifier.TwitchLogin = "oldname";
        sut.AddChannelWithoutQuerying(stream.ChannelIdentifier);
        client.GetStreams(Arg.Any<GetStreamsQuery>(), Arg.Any<CancellationToken>()).Returns(CreateLiveResponse());

        await sut.QueryChannels(CancellationToken.None);

        Assert.Equal("https://www.twitch.tv/known_login/", await stream.GetStreamUrl());
        Assert.Equal("한동숙", stream.ChannelIdentifier.DisplayName);
        await client.DidNotReceive().GetUsers(Arg.Any<GetUsersQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseTheLoginReturnedInTopStreamsWithoutAUserLookup()
    {
        client.GetStreams(Arg.Any<GetStreamsQuery>(), Arg.Any<CancellationToken>()).Returns(CreateLiveResponse());

        var response = await sut.GetTopStreams(new TopStreamQuery { Take = 1 });
        var stream = Assert.Single(response.LivestreamModels);

        Assert.Equal("https://www.twitch.tv/known_login/", await stream.GetStreamUrl());
        await client.DidNotReceive().GetUsers(Arg.Any<GetUsersQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseTheNewestTopStreamsCursorWhenTheSamePageLoadsOutOfOrder()
    {
        const string CurrentCursor = "current";
        var oldResponse = new TaskCompletionSource<StreamsRoot>();
        var currentResponse = new StreamsRoot { Streams = [], Pagination = new() { Cursor = CurrentCursor } };
        client.GetStreams(Arg.Any<GetStreamsQuery>(), Arg.Any<CancellationToken>())
            .Returns(oldResponse.Task, Task.FromResult(currentResponse));

        var oldRequest = sut.GetTopStreams(new TopStreamQuery { Take = 1 });
        await sut.GetTopStreams(new TopStreamQuery { Take = 1 });
        oldResponse.SetResult(new StreamsRoot { Streams = [], Pagination = new() { Cursor = "old" } });
        await oldRequest;

        await sut.GetTopStreams(new TopStreamQuery { Skip = 1, Take = 1 });

        await client.Received(1).GetStreams(
            Arg.Is<GetStreamsQuery>(query => query.Pagination.After == CurrentCursor),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseTheNewestVodCursorWhenTheSamePageLoadsOutOfOrder()
    {
        const string Channel = "channel";
        const string CurrentCursor = "current";
        client.GetUserByUsername(Channel, Arg.Any<CancellationToken>()).Returns(new User { Id = ChannelId });
        var oldResponse = new TaskCompletionSource<VideosRoot>();
        var currentResponse = new VideosRoot { Videos = [], Pagination = new() { Cursor = CurrentCursor } };
        client.GetVideos(Arg.Any<GetVideosQuery>(), Arg.Any<CancellationToken>())
            .Returns(oldResponse.Task, Task.FromResult(currentResponse));

        var oldRequest = sut.GetVods(new VodQuery { StreamDisplayName = Channel, Take = 1 });
        await sut.GetVods(new VodQuery { StreamDisplayName = Channel, Take = 1 });
        oldResponse.SetResult(new VideosRoot { Videos = [], Pagination = new() });
        await oldRequest;

        await sut.GetVods(new VodQuery { StreamDisplayName = Channel, Skip = 1, Take = 1 });

        await client.Received(1).GetVideos(
            Arg.Is<GetVideosQuery>(query => query.CursorPagination.After == CurrentCursor),
            Arg.Any<CancellationToken>());
    }

    private static StreamsRoot CreateLiveResponse() => new()
    {
        Streams =
        [
            new Stream
            {
                UserId = ChannelId,
                UserLogin = "known_login",
                UserName = "한동숙",
                ThumbnailTemplateUrl = "https://example.test/{width}x{height}.jpg",
                Type = "live",
            },
        ],
    };

    private LivestreamModel CreateStream(string displayName)
    {
        var channel = new ChannelIdentifier(sut, ChannelId) { DisplayName = displayName };
        return new LivestreamModel(ChannelId, channel);
    }
}
