using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto;

namespace ExternalAPIs.Youtube
{
    public interface IYoutubeReadonlyClient
    {
        Task<VideoRoot> GetLivestreamDetails(string videoId);
    }
}