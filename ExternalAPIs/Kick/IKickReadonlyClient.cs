using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Kick.Dto;
using ExternalAPIs.Kick.Query;

namespace ExternalAPIs.Kick;

public interface IKickReadonlyClient
{
    Task<LivestreamsRoot> GetLivestreams(GetLivestreamsQuery query, CancellationToken cancellationToken = default);

    Task<ChannelsRoot> GetChannels(GetChannelsQuery query, CancellationToken cancellationToken = default);

    void SetAccessToken(string accessToken);
}