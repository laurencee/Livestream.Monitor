using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.V3;
using ExternalAPIs.TwitchTv.V3.Query;
using Xunit;
using Assert = Xunit.Assert;

namespace ExternalAPIs.Tests
{
    public class TwitchTvClientShould
    {
        private const string StreamName = "etup";
        private readonly TwitchTvReadonlyClient sut = new TwitchTvReadonlyClient();

        [Fact]
        public async Task GetFollowsFromUser()
        {
            var followedStreams = await sut.GetUserFollows(StreamName);
            Assert.NotNull(followedStreams);
            Assert.NotEmpty(followedStreams.Follows);
        }

        [Fact]
        public async Task GetChannelDetails()
        {
            var channelDetails = await sut.GetChannelDetails(StreamName);
            Assert.NotNull(channelDetails);
        }

        [Fact, Trait("Category", "LocalOnly")]
        public async Task GetStreamDetailsForEtup()
        {
            var streamDetails = await sut.GetStreamDetails(StreamName);
            Assert.NotNull(streamDetails);
        }

        [Fact]
        public async Task GetStreamsDetails()
        {
            var streamNames = new List<string>(new []
            {
                "massansc",
                "esl_csgo",
                "saintvicious"
            });
            var streamsDetails = await sut.GetStreamsDetails(streamNames);
            Assert.NotNull(streamsDetails);
            Assert.NotEmpty(streamsDetails);
        }

        [Fact]
        public async Task GetTopGamesList()
        {
            var topGames = await sut.GetTopGames();
            Assert.NotNull(topGames);
            Assert.NotEmpty(topGames);

            topGames.ForEach(x =>
            {
                Assert.NotNull(x.Game);
                Assert.NotNull(x.Game.Name);
            });
        }

        [Fact, Trait("Category", "LocalOnly")]
        public async Task FindStreamEtup()
        {
            var streamsResult = await sut.SearchStreams(StreamName);
            Assert.NotNull(streamsResult);
            Assert.NotEmpty(streamsResult);
        }

        [Fact]
        public async Task FindStreamsGivenPartialNames()
        {
            var streamsResult = await sut.SearchStreams("the");
            Assert.NotNull(streamsResult);
            Assert.NotEmpty(streamsResult);
        }

        [Fact]
        public async Task FindGameMinecraft()
        {
            var gamesResult = await sut.SearchGames("Minecraft");
            Assert.NotNull(gamesResult);
            Assert.NotEmpty(gamesResult);
        }

        [Fact]
        public async Task FindGameWoW()
        {
            var gamesResult = await sut.SearchGames("World of Warcraft");
            Assert.NotNull(gamesResult);
            Assert.NotEmpty(gamesResult);
        }

        [InlineData((string)null)]
        [InlineData("World of Warcraft")]
        [InlineData("Minecraft")]
        [InlineData("League of Legends")]
        [Theory]
        public async Task GetTopStreams(string gameName)
        {
            var topStreamsQuery = new TopStreamQuery() { Skip = 0, Take = 100, GameName = gameName};
            var topStreams = await sut.GetTopStreams(topStreamsQuery);
            Assert.NotNull(topStreams);
            Assert.NotEmpty(topStreams);

            if (topStreamsQuery.GameName != null)
            {
                Assert.All(topStreams, stream => Assert.Contains(topStreamsQuery.GameName, stream.Game, StringComparison.OrdinalIgnoreCase));
            }
        }

        [InlineData(StreamName, true, true)]
        [InlineData(StreamName, true, false)]
        [InlineData(StreamName, false, false)]
        [Theory]
        public async Task GetChannelVideos(string channelName, bool broadcastsOnly, bool hlsVodsOnly)
        {
            var channelVideosQuery = new ChannelVideosQuery()
            {
                ChannelName = channelName,
                BroadcastsOnly = broadcastsOnly,
                HLSVodsOnly = hlsVodsOnly,
            };
            var channelVideos = await sut.GetChannelVideos(channelVideosQuery);
            Assert.NotNull(channelVideos);
            Assert.NotEmpty(channelVideos);
        }
    }
}
