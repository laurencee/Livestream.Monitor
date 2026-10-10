using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

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
            using var httpClient = CreateCompressionHttpClient();
            return await httpClient.ExecuteRequest<T>(request, cancellationToken);
        }

        /// <summary> Gets and deserializes a JSON response; the caller owns and disposes the client. </summary>
        public static async Task<T> ExecuteRequest<T>(this HttpClient httpClient, string request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request)) throw new ArgumentNullException(nameof(request));

            try
            {
                var responseTask = httpClient.GetAsync(request, cancellationToken);

                using var httpResponseMessage = await responseTask.ConfigureAwait(false);
                await httpResponseMessage.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
                var content = httpResponseMessage.Content;
                var response = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<T>(response, JsonDefaults.Options);
            }
            catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out executing web request", e);
            }
        }

        private static async Task EnsureSuccessStatusCodeAsync(this HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            // preserve the original content as the error message, after the status so an empty body still shows a cause
            var content = await response.Content.ReadAsStringAsync();

            response.Content?.Dispose();

            var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
            var message = string.IsNullOrWhiteSpace(content) ? status : $"{status}: {content}";
            throw new HttpRequestWithStatusException(response.StatusCode, message);
        }
    }
}
