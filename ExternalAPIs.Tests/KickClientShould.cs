using System;
using System.IO;
using System.Threading.Tasks;
using ExternalAPIs.Kick;
using ExternalAPIs.Kick.Query;
using Xunit;

namespace ExternalAPIs.Tests;

public class KickClientShould
{
    private static class CategoryIds
    {
        public const int IRL = 8549;
        public const int JustChatting = 15;
        public const int SlotsAndCasino = 28;
        public const int Dota2 = 14;
    }

    private const string TestSlug = "gmhikaru";
    private const string TestSlug2 = "xqc";
    private const int TestBroadcastUserId = 1820952; // gmhikaru
    private const int TestBroadcastUserId2 = 676; // xqc

    private readonly KickReadonlyClient sut = new();

    public KickClientShould()
    {
        // create this file locally, it's already marked to be copied to the output in this test project
        var twitchAccessToken = File.ReadAllText("kickaccesstoken.local");
        sut.SetAccessToken(twitchAccessToken);
    }

    [Fact]
    public async Task GetChannelBySlug()
    {
        var query = new GetChannelsQuery
        {
            Slugs = [TestSlug, TestSlug2],
        };
        var channelsRoot = await sut.GetChannels(query);

        Assert.NotNull(channelsRoot);
        Assert.Equal(query.Slugs.Count, channelsRoot.Data.Count);
        Assert.Equal(TestSlug, channelsRoot.Data[0].Slug, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(TestSlug2, channelsRoot.Data[1].Slug, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetChannelById()
    {
        var query = new GetChannelsQuery
        {
            BroadcasterUserIds = [TestBroadcastUserId, TestBroadcastUserId2],
        };
        var channelsRoot = await sut.GetChannels(query);

        Assert.NotNull(channelsRoot);
        Assert.Equal(query.BroadcasterUserIds.Count, channelsRoot.Data.Count);
        Assert.Equal(TestBroadcastUserId, channelsRoot.Data[0].BroadcasterUserId);
        Assert.Equal(TestBroadcastUserId2, channelsRoot.Data[1].BroadcasterUserId);
    }

    [InlineData(CategoryIds.JustChatting)] // broken api (internal server error)
    [InlineData(CategoryIds.Dota2)]
    [InlineData(CategoryIds.SlotsAndCasino)]
    [InlineData(CategoryIds.IRL)] // broken api (returns nothing)
    [InlineData(null)]
    [Theory]
    public async Task GetTopStreams(int? categoryId)
    {
        var channelVideosQuery = new GetLivestreamsQuery()
        {
            CategoryId = categoryId,
            Sort = LivestreamsSort.ViewerCount,
        };
        var livestreamsRoot = await sut.GetLivestreams(channelVideosQuery);

        Assert.NotNull(livestreamsRoot);
        // only returns data for online streams and unfortunately an OK response doesn't actually mean the request was correct
        Assert.Equal("OK", livestreamsRoot.Message, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(livestreamsRoot.Data);
    }

    [Fact]
    public async Task GetTopStreamsPage2()
    {
        var channelVideosQuery = new GetLivestreamsQuery()
        {
            Sort = LivestreamsSort.ViewerCount,
            Page = 2,
        };

        var livestreamsRoot = await sut.GetLivestreams(channelVideosQuery);

        var channelVideosQuery2 = new GetLivestreamsQuery()
        {
            Sort = LivestreamsSort.ViewerCount,
            Page = 3,
        };
        var livestreamsRoot2 = await sut.GetLivestreams(channelVideosQuery2);

        Assert.NotNull(livestreamsRoot);
        // only returns data for online streams and unfortunately an OK response doesn't actually mean the request was correct
        Assert.Equal("OK", livestreamsRoot.Message, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(livestreamsRoot.Data);
    }
}