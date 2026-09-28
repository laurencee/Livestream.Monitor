using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ExternalAPIs
{
    public static class HttpClientExtensions
    {
        public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

        // Keep the Framework connection pool alive across short-lived request clients.
        private const int MaxConnectionsPerServer = 8;
        private static readonly HttpClientHandler SharedHandler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = MaxConnectionsPerServer,
            UseCookies = false,
        };

        /// <summary> Creates a request client sharing the application's connection pool. </summary>
        public static HttpClient CreateCompressionHttpClient()
        {
            return new HttpClient(SharedHandler, disposeHandler: false) { Timeout = QueryTimeout };
        }

        public static async Task<T> ExecuteRequest<T>(string request, CancellationToken cancellationToken = default)
        {
            return await ExecuteRequest<T>(CreateCompressionHttpClient(), request, cancellationToken);
        }

        public static async Task<T> ExecuteRequest<T>(this HttpClient httpClient, string request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request)) throw new ArgumentNullException(nameof(request));

            using (httpClient)
            {
                try
                {
                    var responseTask = httpClient.GetAsync(request, cancellationToken);

                    using var httpResponseMessage = await responseTask.ConfigureAwait(false);
                    await httpResponseMessage.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
                    var response = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<T>(response);
                }
                catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Timed out executing web request", e);
                }
            }
        }

        private static async Task EnsureSuccessStatusCodeAsync(this HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            // preserve the original content as the error message
            var content = await response.Content.ReadAsStringAsync();

            response.Content?.Dispose();
            throw new HttpRequestWithStatusException(response.StatusCode, content);
        }
    }
}
