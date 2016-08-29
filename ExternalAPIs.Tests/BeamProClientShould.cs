using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Beam.Pro;
using ExternalAPIs.Beam.Pro.Query;
using Xunit;

namespace ExternalAPIs.Tests
{
    public class BeamProClientShould
    {
        private readonly IBeamProReadonlyClient sut = new BeamProReadonlyClient();
        
        [Fact]
        public async Task GetTopStreams(string gameName)
        {
            var topStreams = await sut.GetTopStreams(new BeamProPagedQuery());
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
            // to find a user to query for recordings, look for hasVod=true in the response for channels "https://beam.pro/api/v1/channels?order=hasVod:DESC"
            // you must use the "id" of the channel rather than the channel name/token
            var videos = await sut.GetChannelVideos(1903, new BeamProPagedQuery() { Take = 10});
            Assert.NotEmpty(videos);
            Assert.NotNull(videos[0].duration);
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
