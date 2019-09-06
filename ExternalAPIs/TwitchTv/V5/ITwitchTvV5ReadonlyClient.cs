using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.V5.Dto;

namespace ExternalAPIs.TwitchTv.V5
{
    public interface ITwitchTvV5ReadonlyClient
    {
        Task<List<TopGame>> GetTopGames(CancellationToken cancellationToken = default(CancellationToken));

        Task<List<Game>> SearchGames(string gameName, CancellationToken cancellationToken = default(CancellationToken));

        void SetAccessToken(string accessToken);
    }
}