using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Kick.Dto;
using ExternalAPIs.Kick.Query;

namespace ExternalAPIs.Kick;

public class KickReadonlyClient : IKickReadonlyClient
{
    // ReSharper disable once InconsistentNaming
    private string _accessToken;

    public async Task<LivestreamsRoot> GetLivestreams(GetLivestreamsQuery query, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder(RequestConstants.LivestreamsEndpoint);

        var queryParams = new List<string>();
        if (query.BroadcasterUserIds.Any()) 
            queryParams.Add($"broadcaster_user_id={string.Join("&broadcaster_user_id=", query.BroadcasterUserIds)}");

        if (query.Limit.HasValue) 
            queryParams.Add($"limit={query.Limit.Value}");

        if (query.CategoryId.HasValue)
            queryParams.Add($"category_id={query.CategoryId}");

        if (query.Language != null)
            queryParams.Add($"language={query.Language}");

        if (query.Page > 1)
            queryParams.Add($"page={query.Page}");

        switch (query.Sort)
        {
            case LivestreamsSort.ViewerCount:
                queryParams.Add("sort=viewer_count");
                break;
            case LivestreamsSort.StartedAt:
                queryParams.Add("sort=started_at");
                break;
        }

        if (queryParams.Count > 0) // append query params
        {
            urlBuilder.Append("?");
            for (int i = 0; i < queryParams.Count; i++)
            {
                urlBuilder.Append(queryParams[i]);
                if (i + 1 != queryParams.Count) urlBuilder.Append('&');
            }
        }

        var requestUrl = urlBuilder.ToString();
        var response = await ExecuteRequest<LivestreamsRoot>(requestUrl, cancellationToken);
        return response;
    }

    public async Task<ChannelsRoot> GetChannels(GetChannelsQuery query, CancellationToken cancellationToken = default)
    {
        // TODO - split requests on MaxRequestLimit
        string requestUrl;
        if (query.Slugs.Count > 0)
            requestUrl = $"{RequestConstants.ChannelsEndpoint}?slug={string.Join("&slug=", query.Slugs)}";
        else if (query.BroadcasterUserIds.Count > 0)
            requestUrl = $"{RequestConstants.ChannelsEndpoint}?broadcaster_user_id={string.Join("&broadcaster_user_id=", query.BroadcasterUserIds)}";
        else
            throw new ArgumentException($"Either {nameof(query.Slugs)} or {nameof(query.BroadcasterUserIds)} must be set");

        var response = await ExecuteRequest<ChannelsRoot>(requestUrl, cancellationToken);
        return response;
    }

    public void SetAccessToken(string accessToken)
    {
        _accessToken = accessToken;
    }

    private async Task<T> ExecuteRequest<T>(string request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_accessToken)) throw new InvalidOperationException("Access token is not set.");

        using var httpClient = HttpClientExtensions.CreateCompressionHttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        return await httpClient.ExecuteRequest<T>(request, cancellationToken);
    }
}