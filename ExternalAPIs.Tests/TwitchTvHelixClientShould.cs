using System;
using ExternalAPIs.TwitchTv.Helix;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Helix.Query;
using Xunit;

namespace ExternalAPIs.Tests
{
    public class TwitchTvHelixClientShould
    {
        private static class GameIds
        {
            public const string LeagueOfLegends = "21779";
            public const string WorldOfWarcraft = "18122";
            public const string Fortnite = "33214";
            public const string JustChatting = "509658";
        }

        public const string UserIdMethod = "121649330";
        public const string UserIdThijs = "57025612";

        private readonly TwitchTvHelixHelixReadonlyClient sut = new TwitchTvHelixHelixReadonlyClient();

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

        [Fact(Skip = "As of 2020 must use user_id of oauth access token, used to be able to lookup anyone's follow list")]
        public async Task GetFollowsFromUser()
        {
            // previously used thijs as he follows more than 100 channels which means the query must page
            var followedStreams = await sut.GetFollowedChannels("INSERT USER ID MATCHING OAUTH ACCESS TOKEN");
            Assert.NotNull(followedStreams);
            Assert.NotEmpty(followedStreams);
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
            const string expectedGameName = "World of Warcraft";
            var query = new GetGamesQuery { GameNames = new List<string>() { expectedGameName } };
            var gamesResult = await sut.GetGames(query);
            Assert.NotNull(gamesResult);
            Assert.NotEmpty(gamesResult);
            Assert.All(gamesResult, game => Assert.True(game.Name.Contains(expectedGameName), "game.Name.Contains(expectedGameName)"));
        }

        [InlineData(GameIds.WorldOfWarcraft)]
        [InlineData(GameIds.LeagueOfLegends)]
        [InlineData(GameIds.Fortnite)]
        [InlineData(GameIds.JustChatting)]
        [Theory]
        public async Task GetTopGameStreams(string gameId)
        {
            var topStreamsQuery = new GetStreamsQuery()
            {
                GameIds = new List<string>() { gameId }
            };
            var topStreams = await sut.GetStreams(topStreamsQuery);
            Assert.NotNull(topStreams);
            Assert.NotEmpty(topStreams.Streams);

            Assert.All(topStreams.Streams, stream => Assert.Equal(gameId, stream.GameId));
        }

        [InlineData(UserIdMethod)]
        [InlineData(UserIdThijs)]
        [Theory]
        public async Task GetChannelVideos(string userId)
        {
            var channelVideosQuery = new GetVideosQuery()
            {
                UserId = userId,
            };
            var channelVideos = await sut.GetVideos(channelVideosQuery);
            Assert.NotNull(channelVideos);
            Assert.NotEmpty(channelVideos.Videos);
        }
        
        [InlineData("League of Legends")]
        [InlineData("Just Chatting")]
        [Theory]
        public async Task SearchCategories(string category)
        {
            var twitchCategories = await sut.SearchCategories(category);
            Assert.NotNull(twitchCategories);
            Assert.NotEmpty(twitchCategories);
        }
    }
}
