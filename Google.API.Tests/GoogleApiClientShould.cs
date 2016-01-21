using System;
using System.Net;
using System.Threading.Tasks;
using HttpCommon;
using Xunit;

namespace Google.API.Tests
{
    public class GoogleApiClientShould
    {
        private const string SKY_NEWS_VIDEO_ID = "y60wDzZt8yg";
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
                Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
            }
        }
    }
}
