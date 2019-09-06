using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.V5.Dto;
using ExternalAPIs.TwitchTv.V5.Dto.QueryRoot;

namespace ExternalAPIs.TwitchTv.V5
{
    public class TwitchTvV5ReadonlyClient : ITwitchTvV5ReadonlyClient
    {
        private string _accessToken;

        public async Task<List<TopGame>> GetTopGames(CancellationToken cancellationToken = default)
        {
            var request = RequestConstants.TopGames;
            var gamesRoot = await ExecuteRequest<TopGamesRoot>(request, cancellationToken);
            return gamesRoot.TopGames;
        }

        public async Task<List<Game>> SearchGames(string gameName, CancellationToken cancellationToken = default)
        {
            if (String.IsNullOrWhiteSpace(gameName)) throw new ArgumentNullException(nameof(gameName));

            var request = RequestConstants.SearchGames.Replace("{0}", gameName);
            var gamesRoot = await ExecuteRequest<GamesRoot>(request, cancellationToken);
            return gamesRoot.Games;
        }

        public void SetAccessToken(string accessToken)
        {
            _accessToken = accessToken;
        }

        private Task<T> ExecuteRequest<T>(string request, CancellationToken cancellationToken = default(CancellationToken))
        {
            HttpClient httpClient = HttpClientExtensions.CreateCompressionHttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(RequestConstants.AcceptHeader));
            httpClient.DefaultRequestHeaders.Add(RequestConstants.ClientIdHeaderKey, RequestConstants.ClientIdHeaderValue);

            if (_accessToken != null)
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }

            return httpClient.ExecuteRequest<T>(request, cancellationToken);
        }
    }
}