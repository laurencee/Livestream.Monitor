using System.Threading.Tasks;
using Google.API.Dto;

namespace Google.API
{
    public interface IYoutubeReadonlyClient
    {
        Task<VideoRoot> GetLivestreamDetails(string videoId);
    }
}