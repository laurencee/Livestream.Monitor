using System;
using ExternalAPIs.TwitchTv.Helix;
using System.IO;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Helix.Query;
using Xunit;

namespace ExternalAPIs.Tests
{
    [Trait("Category", "LiveApi")]
    public class TwitchTvHelixClientShould
    {
        private const string JustChattingGameId = "509658";
        private const string UserIdThijs = "57025612";

        private readonly TwitchTvHelixHelixReadonlyClient sut = new();

        public TwitchTvHelixClientShould()
        {
            // create this file locally, it's already marked to be copied to the output in this test project
            var twitchAccessToken = File.ReadAllText("twitchaccesstoken.local");
            sut.SetAccessToken(twitchAccessToken);
        }

        [Fact]
        public async Task GetUser()
        {
            string expectedUser = "thijs";
            var query = new GetUsersQuery();
            query.UserNames.Add(expectedUser);
            var users = await sut.GetUsers(query);

            Assert.NotNull(users);
            Assert.NotEmpty(users);
            Assert.Equal(expectedUser, users[0].DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetTopGamesList()
        {
            var topGames = await sut.GetTopGames();
            Assert.NotNull(topGames);
            Assert.NotEmpty(topGames);

            topGames.ForEach(x =>
            {
                Assert.NotNull(x.Id);
                Assert.NotNull(x.Name);
            });
        }

        [Fact]
        public async Task FindGameWoW()
        {
            const string ExpectedGameName = "World of Warcraft";
            var query = new GetGamesQuery { GameNames = [ExpectedGameName] };
            var gamesResult = await sut.GetGames(query);
            Assert.NotNull(gamesResult);
            Assert.NotEmpty(gamesResult);
            Assert.All(gamesResult, game => Assert.True(game.Name.Contains(ExpectedGameName), "game.Name.Contains(ExpectedGameName)"));
        }

        [Fact]
        public async Task GetTopGameStreams()
        {
            var topStreamsQuery = new GetStreamsQuery()
            {
                GameIds = [JustChattingGameId],
            };
            var topStreams = await sut.GetStreams(topStreamsQuery);
            Assert.NotNull(topStreams);
            Assert.NotEmpty(topStreams.Streams);

            Assert.All(topStreams.Streams, stream => Assert.Equal(JustChattingGameId, stream.GameId));
        }

        [Fact]
        public async Task GetChannelVideos()
        {
            var channelVideosQuery = new GetVideosQuery()
            {
                UserId = UserIdThijs,
            };
            var channelVideos = await sut.GetVideos(channelVideosQuery);
            Assert.NotNull(channelVideos);
            Assert.NotEmpty(channelVideos.Videos);
        }
        
        [Fact]
        public async Task SearchCategories()
        {
            var twitchCategories = await sut.SearchCategories("Just Chatting");
            Assert.NotNull(twitchCategories);
            Assert.NotEmpty(twitchCategories);
        }
    }
}
