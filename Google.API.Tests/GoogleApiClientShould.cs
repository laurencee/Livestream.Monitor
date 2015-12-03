using System;
using System.Threading.Tasks;
using Xunit;

namespace Google.API.Tests
{
    public class GoogleApiClientShould
    {
        private const string SKY_NEWS_VIDEO_ID = "y60wDzZt8yg";
        private readonly GoogleVideoReadonlyClient sut = new GoogleVideoReadonlyClient();

        [Fact, Trait("Category", "LocalOnly")]
        public async Task GetLivestreamDetails()
        {
            var videoRoot = await sut.GetLivestreamDetails(SKY_NEWS_VIDEO_ID);

            Assert.NotNull(videoRoot?.Items);
            Assert.NotNull(videoRoot?.Items[0].Snippet);
            Assert.NotNull(videoRoot?.Items[0].LiveStreamingDetails);
        }
    }
}
