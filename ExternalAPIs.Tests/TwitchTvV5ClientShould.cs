using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.V5;
using Xunit;

namespace ExternalAPIs.Tests
{
    public class TwitchTvV5ClientShould
    {
        private readonly TwitchTvV5ReadonlyClient sut = new TwitchTvV5ReadonlyClient();
        
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
