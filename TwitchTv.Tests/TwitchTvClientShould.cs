using System.Threading.Tasks;
using Xunit;

namespace TwitchTv.Tests
{
    public class TwitchTvClientShould
    {
        private readonly TwitchTvReadonlyClient sut = new TwitchTvReadonlyClient();

        [Fact]
        public async Task GetFollowsFromUser()
        {
            var followedStreams = await sut.GetUserFollows("fxfighter");
            Assert.NotNull(followedStreams);
            Assert.NotEmpty(followedStreams.Follows);
        }

        [Fact]
        public async Task GetChannelDetails()
        {
            var channelDetails = await sut.GetChannelDetails("fxfighter");
            Assert.NotNull(channelDetails);
        }

        [Fact]
        public async Task GetStreamDetails()
        {
            var streamDetails = await sut.GetStreamDetails("etup");
            Assert.NotNull(streamDetails);
        }

        [Fact]
        public async Task GetTopGamesList()
        {
            var topGames = await sut.GetTopGames();
            Assert.NotNull(topGames);
            Assert.NotEmpty(topGames);
        }

        [Fact]
        public async Task FindStreamEtup()
        {
            var streamsResult = await sut.SearchStreams("Etup");
            Assert.NotNull(streamsResult);
            Assert.NotEmpty(streamsResult);
        }

        [Fact]
        public async Task FindStreamsGivenPartialNames()
        {
            var streamsResult = await sut.SearchStreams("C9");
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
    }
}
