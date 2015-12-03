using System.Threading.Tasks;
using Google.API.Dto;

namespace Google.API
{
    public interface IGoogleVideoReadonlyClient
    {
        Task<VideoRoot> GetLivestreamDetails(string videoId);
    }
}