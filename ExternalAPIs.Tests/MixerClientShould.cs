using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Mixer;
using ExternalAPIs.Mixer.Query;
using Xunit;

namespace ExternalAPIs.Tests
{
    public class MixerClientShould
    {
        private readonly IMixerReadonlyClient sut = new MixerReadonlyClient();

        [InlineData((string)null)]
        [InlineData("World of Warcraft")]
        [InlineData("Minecraft")]
        [InlineData("League of Legends")]
        [Theory]
        public async Task GetTopStreams(string gameName)
        {
            var topStreams = await sut.GetTopStreams(new MixerPagedQuery());
            Assert.NotEmpty(topStreams);
            Assert.NotNull(topStreams[0]);
        }

        [Fact]
        public async Task GetStreamDetails()
        {
            var streamDetails = await sut.GetStreamDetails("monstercat");
            Assert.NotNull(streamDetails);
            Assert.NotNull(streamDetails.description);
        }

        [Fact]
        public async Task GetChannelVideos()
        {
            // to find a user to query for recordings, look for hasVod=true in the response for channels "https://mixer.com/api/v1/channels?order=hasVod:DESC"
            // you must use the "id" of the channel rather than the channel name/token
            var videos = await sut.GetChannelVideos(1903, new MixerPagedQuery() { Take = 10});
            Assert.NotEmpty(videos);
        }

        [InlineData(null)]
        [InlineData("Final")]
        [InlineData("World of Warcraft")]
        [Theory]
        public async Task GetKnownGames(string nameFilter)
        {
            var knownGames = await sut.GetKnownGames(new KnownGamesPagedQuery() { GameName = nameFilter });
            Assert.NotEmpty(knownGames);
            Assert.NotNull(knownGames[0].name);
        }
    }
}
