using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExternalAPIs.Hitbox;
using ExternalAPIs.Hitbox.Query;
using Xunit;

namespace ExternalAPIs.Tests
{
    public class HitboxClientShould
    {
        private const string KnownChannelName = "rewardsgg";
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
            var mediainfo = await sut.GetStreamDetails(859185.ToString());
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
    }
}
