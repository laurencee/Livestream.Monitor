using System.Threading.Tasks;
using ExternalAPIs.Hitbox;
using ExternalAPIs.Hitbox.Query;
using Xunit;

namespace ExternalAPIs.Tests
{
    public class HitboxClientShould
    {
        private const string KnownChannelName = "rewardsgg";
        private const int KnownChannelId = 859185;

        //private const string KnownChannelName = "Heroesofcards";
        //private const int KnownChannelId = 217859;

        private readonly HitboxReadonlyClient sut = new HitboxReadonlyClient();

        [Fact]
        public async Task GetTopStreams()
        {
            var topStreamsQuery = new TopStreamsQuery();
            var livestreams = await sut.GetTopStreams(topStreamsQuery);
            Assert.NotNull(livestreams);
            Assert.NotEmpty(livestreams);
        }

        [Fact]
        public async Task GetLivestreamDetails()
        {
            var mediainfo = await sut.GetStreamDetails(KnownChannelId.ToString());
            Assert.NotNull(mediainfo);
        }

        [Fact]
        public async Task GetChannelDetails()
        {
            var livestream = await sut.GetChannelDetails(KnownChannelName);
            Assert.NotNull(livestream);
            Assert.NotNull(livestream.Channel);
        }

        [Fact]
        public async Task GetChannelVideos()
        {
            var channelVideosQuery = new ChannelVideosQuery("ECTVLoL");
            var videos = await sut.GetChannelVideos(channelVideosQuery);
            Assert.NotNull(videos);
            Assert.NotEmpty(videos);
            Assert.NotNull(videos[0].Channel);
        }

        [Fact]
        public async Task GetUserFollows()
        {
            var followings = await sut.GetUserFollows("fxfighter");
            Assert.NotNull(followings);
            Assert.NotEmpty(followings);
        }

        [InlineData(null)]
        [InlineData("Minecraft")]
        [Theory]
        public async Task GetTopGames(string gameName)
        {
            var topGames = await sut.GetTopGames(gameName);
            Assert.NotNull(topGames);
            Assert.NotEmpty(topGames);
        }
    }
}
