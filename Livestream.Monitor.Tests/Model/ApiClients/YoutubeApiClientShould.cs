using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube;
using ExternalAPIs.Youtube.Dto;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Model.ApiClients;

public sealed class YoutubeApiClientShould : IDisposable
{
    private const string ChannelId = "UCchannel";
    private const int QuickCheckEntries = 5;
    private const int CompleteCheckEntries = 50;

    private readonly IYoutubeReadonlyClient youtubeClient = Substitute.For<IYoutubeReadonlyClient>();
    private readonly string cachePath = Path.Combine(Path.GetTempPath(), $"Livestream.Monitor.Cache-{Guid.NewGuid():N}.bin");
    private readonly YoutubeApiClient sut;

    public YoutubeApiClientShould()
    {
        sut = new YoutubeApiClient(youtubeClient, new CacheHandler(cachePath));
        sut.AddChannelWithoutQuerying(new ChannelIdentifier(sut, ChannelId) { DisplayName = "@channel" });
    }

    public void Dispose() => File.Delete(cachePath);

    [Fact]
    public async Task QuicklyCheckOnlyTheLastCompleteCheckEntriesInLaterSessions()
    {
        RespondWith(liveVideos: [], playlistVideoIds: ["new", "old"]);
        await sut.QueryChannelsQuickly(CancellationToken.None);
        await sut.QueryChannels(CancellationToken.None);

        await youtubeClient.Received(1).GetLivestreamPlaylistVideoIds(ChannelId, QuickCheckEntries, Arg.Any<CancellationToken>());
        await youtubeClient.Received(1).GetLivestreamPlaylistVideoIds(ChannelId, CompleteCheckEntries, Arg.Any<CancellationToken>());
        youtubeClient.ClearReceivedCalls();

        await StartNextSession(ChannelId).QueryChannelsQuickly(CancellationToken.None);

        await youtubeClient.DidNotReceive().GetLivestreamPlaylistVideoIds(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await youtubeClient.Received(1).GetLiveVideos(
            Arg.Is<IReadOnlyCollection<string>>(x => x.SequenceEqual(new[] { "new", "old" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepTheEarlierEntriesOfAChannelWhoseCompleteCheckFailed()
    {
        RespondWith(liveVideos: [], playlistVideoIds: ["new", "old"]);
        await sut.QueryChannels(CancellationToken.None);

        youtubeClient.GetLivestreamPlaylistVideoIds(ChannelId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<List<string>>(new TimeoutException("Timed out.")));
        var failedCheck = await sut.QueryChannels(CancellationToken.None);
        youtubeClient.ClearReceivedCalls();

        await StartNextSession(ChannelId).QueryChannelsQuickly(CancellationToken.None);

        Assert.NotNull(Assert.Single(failedCheck).FailedQueryException);
        await youtubeClient.Received(1).GetLiveVideos(
            Arg.Is<IReadOnlyCollection<string>>(x => x.SequenceEqual(new[] { "new", "old" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepTheEntriesOfAChannelAddedDuringACompleteCheck()
    {
        const string AddedChannelId = "UCadded";
        RespondWith(liveVideos: [], playlistVideoIds: ["new"]);
        var playlist = new TaskCompletionSource<List<string>>();
        youtubeClient.GetLivestreamPlaylistVideoIds(ChannelId, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(playlist.Task);
        var addedChannel = new ChannelsRoot { Items = [new Channel { Id = AddedChannelId }] };
        youtubeClient.GetChannelDetailsFromHandle("added", Arg.Any<CancellationToken>()).Returns(addedChannel);

        var completeCheck = sut.QueryChannels(CancellationToken.None);
        await sut.AddChannel(new ChannelIdentifier(sut, "added"));
        playlist.SetResult(["old"]);
        await completeCheck;
        youtubeClient.ClearReceivedCalls();

        await StartNextSession(AddedChannelId).QueryChannelsQuickly(CancellationToken.None);

        await youtubeClient.DidNotReceive().GetLivestreamPlaylistVideoIds(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShareTheKnownVideoLookupOfConcurrentChecks()
    {
        RespondWith(liveVideos: [], playlistVideoIds: ["new", "old"]);
        await sut.QueryChannels(CancellationToken.None);

        var lookup = new TaskCompletionSource<List<Video>>();
        youtubeClient.GetLiveVideos(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(lookup.Task);
        youtubeClient.ClearReceivedCalls();
        var quickCheck = sut.QueryChannelsQuickly(CancellationToken.None);
        var completeCheck = sut.QueryChannels(CancellationToken.None);
        lookup.SetResult([]);
        await Task.WhenAll(quickCheck, completeCheck);

        await youtubeClient.Received(1).GetLiveVideos(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LookUpTheKnownVideosAgainInALaterCheck()
    {
        RespondWith(liveVideos: [], playlistVideoIds: ["new", "old"]);
        await sut.QueryChannels(CancellationToken.None);
        youtubeClient.ClearReceivedCalls();

        await sut.QueryChannelsQuickly(CancellationToken.None);
        await sut.QueryChannelsQuickly(CancellationToken.None);

        await youtubeClient.Received(2).GetLiveVideos(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForgetTheEntriesOfAChannelNoLongerFollowed()
    {
        RespondWith(liveVideos: [], playlistVideoIds: ["new"]);
        await sut.QueryChannels(CancellationToken.None);
        await sut.RemoveChannel(new ChannelIdentifier(sut, ChannelId));
        await sut.QueryChannels(CancellationToken.None);
        youtubeClient.ClearReceivedCalls();

        await StartNextSession(ChannelId).QueryChannelsQuickly(CancellationToken.None);

        await youtubeClient.Received(1).GetLivestreamPlaylistVideoIds(ChannelId, QuickCheckEntries, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportAKnownLiveVideoBeyondThePlaylistEntriesOnlyFromAQuickCheck()
    {
        RespondWith(liveVideos: [CreateLiveVideo("deep")], playlistVideoIds: ["deep"]);
        await sut.QueryChannels(CancellationToken.None);
        RespondWith(liveVideos: [CreateLiveVideo("deep")], playlistVideoIds: ["new"]);

        var quick = await sut.QueryChannelsQuickly(CancellationToken.None);
        var complete = await sut.QueryChannels(CancellationToken.None);

        var quickResult = Assert.Single(quick);
        Assert.Null(quickResult.FailedQueryException);
        Assert.True(quickResult.LivestreamModel.Live);
        Assert.Equal("deep", quickResult.LivestreamModel.Id);
        var completeResult = Assert.Single(complete);
        Assert.Null(completeResult.FailedQueryException);
        Assert.False(completeResult.LivestreamModel.Live);
    }

    private void RespondWith(List<Video> liveVideos, List<string> playlistVideoIds)
    {
        youtubeClient.GetLivestreamPlaylistVideoIds(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => playlistVideoIds.ToList());
        youtubeClient.GetLiveVideos(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => liveVideos.Where(x => call.Arg<IReadOnlyCollection<string>>().Contains(x.Id)).ToList());
    }

    private YoutubeApiClient StartNextSession(string channelId)
    {
        var nextSession = new YoutubeApiClient(youtubeClient, new CacheHandler(cachePath));
        nextSession.AddChannelWithoutQuerying(new ChannelIdentifier(nextSession, channelId));
        return nextSession;
    }

    private static Video CreateLiveVideo(string id) => new()
    {
        Id = id,
        Snippet = new VideoSnippet { LiveBroadcastContent = "live", ChannelTitle = "channel", Title = "stream" },
        LiveStreamingDetails = new LiveStreamingDetails { ActualStartTime = DateTimeOffset.UtcNow },
    };
}
