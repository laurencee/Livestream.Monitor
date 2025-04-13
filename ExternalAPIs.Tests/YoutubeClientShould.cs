using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ExternalAPIs.Youtube;
using ExternalAPIs.Youtube.Query;
using Xunit;

namespace ExternalAPIs.Tests
{
    public class YoutubeClientShould
    {
        private const string LOFIGIRL_LIVE_VIDEO_ID = "jfKfPfyJRdk"; // needs to be manually updated to a live video id
        private const string LOFIGIRL_CHANNEL_ID = "UCSJ4gkVC6NrvII8umztf0Ow";
        private const string YOUTUBE_HANDLE = "@LofiGirl";
        private readonly YoutubeReadonlyClient sut = new YoutubeReadonlyClient();

        [Fact]
        public async Task GetLivestreamDetails()
        {
            var videoRoot = await sut.GetVideosDetails([LOFIGIRL_LIVE_VIDEO_ID]);

            Assert.NotNull(videoRoot?.Items);
            Assert.NotNull(videoRoot.Items[0].Snippet);
            Assert.NotNull(videoRoot.Items[0].LiveStreamingDetails);
        }

        [Fact]
        public async Task ThrowNotFoudExceptionForInvalidVideoId()
        {
            try
            {
                // clearly a well thought out random channel id that will fail and not just mashing the keyboard
                await sut.GetVideosDetails(new [] {"sdlfh94uhf43ouhf3l4uhf3493[]"});
            }
            catch (HttpRequestWithStatusException exception)
            {
                Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
            }
        }

        [Fact]
        public async Task GetChannelIdFromHandle()
        {
            var channelsRoot = await sut.GetChannelDetailsFromHandle(YOUTUBE_HANDLE);

            Assert.NotNull(channelsRoot?.Items);
            Assert.NotEmpty(channelsRoot.Items);
        }

        [Fact]
        public async Task GetVodsFromHandle()
        {
            var channelsRoot = await sut.GetChannelDetailsFromHandle(YOUTUBE_HANDLE);
            Assert.NotEmpty(channelsRoot.Items);

            var uploadsPlaylistId = channelsRoot.Items[0].ContentDetails.RelatedPlaylists.Uploads;

            var query= new PlaylistItemsQuery(uploadsPlaylistId);
            var playlistItemsRoot = await sut.GetPlaylistItems(query);
            Assert.NotEmpty(playlistItemsRoot.Items);

            var videoIds = playlistItemsRoot.Items.Select(x => x.ContentDetails.VideoId).ToArray();
            var videoDetails = await sut.GetVideosDetails(videoIds);
            Assert.NotEmpty(videoDetails.Items);
        }

        [Fact]
        public async Task GetLiveVideos()
        {
            var onlineVideos = await sut.GetLivestreamVideos(LOFIGIRL_CHANNEL_ID);

            Assert.NotNull(onlineVideos);
            Assert.NotNull(onlineVideos.Items);
            Assert.NotEmpty(onlineVideos.Items);
        }
    }
}
