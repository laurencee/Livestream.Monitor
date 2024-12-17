using System.Net;
using System.Threading.Tasks;
using ExternalAPIs.Youtube;
using Xunit;

namespace ExternalAPIs.Tests
{
    public class YoutubeClientShould
    {
        private const string SKY_NEWS_VIDEO_ID = "siyW0GOBtbo";
        private const string SKY_NEWS_CHANNEL_ID = "UCoMdktPbSTixAyNGwb-UYkQ";
        private readonly YoutubeReadonlyClient sut = new YoutubeReadonlyClient();

        [Fact, Trait("Category", "LocalOnly")]
        public async Task GetLivestreamDetails()
        {
            var videoRoot = await sut.GetLivestreamDetails(SKY_NEWS_VIDEO_ID);

            Assert.NotNull(videoRoot?.Items);
            Assert.NotNull(videoRoot?.Items[0].Snippet);
            Assert.NotNull(videoRoot?.Items[0].LiveStreamingDetails);
        }

        [Fact, Trait("Category", "LocalOnly")]
        public async Task ThrowNotFoudExceptionForInvalidVideoId()
        {
            try
            {
                // clearly a well thought out random channel id that will fail and not just mashing the keyboard
                await sut.GetLivestreamDetails("sdlfh94uhf43ouhf3l4uhf3493[]");
            }
            catch (HttpRequestWithStatusException exception)
            {
                Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
            }
        }

        [Fact, Trait("Category", "LocalOnly")]
        public async Task GetChannelIdFromUsername()
        {
            const string channelName = "skynews";
            var channelId = await sut.GetChannelDetailsFromHandle(channelName);

            Assert.NotNull(channelId);
        }

        [Fact, Trait("Category", "LocalOnly")]
        public async Task GetLiveVideos()
        {
            var onlineVideos = await sut.GetLivestreamVideos(SKY_NEWS_CHANNEL_ID);

            Assert.NotNull(onlineVideos);
            Assert.NotNull(onlineVideos.Items);
            Assert.NotEmpty(onlineVideos.Items);
        }
    }
}
