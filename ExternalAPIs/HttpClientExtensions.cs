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

        // Stream provider API origins, connected before their first query.
        private static readonly string[] ProviderOrigins = ["https://api.twitch.tv/", "https://www.googleapis.com/"];

        /// <summary> Creates a request client sharing the application's connection pool. </summary>
        public static HttpClient CreateCompressionHttpClient()
        {
            // HTTP/2 lets a provider's parallel requests share one connection instead of each paying its own TLS handshake.
            return new HttpClient(SharedHandler, disposeHandler: false)
            {
                Timeout = QueryTimeout,
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
        }

        /// <summary>
        /// Opens pooled connections to the stream provider APIs in the background, so the first queries do not wait for
        /// connection setup.
        /// </summary>
        public static void PreconnectProviderApis()
        {
            foreach (var origin in ProviderOrigins)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // The API root needs no key and counts against no quota; only the connection is wanted.
                        using var httpClient = CreateCompressionHttpClient();
                        using var request = new HttpRequestMessage(HttpMethod.Head, origin);

                        // A sent message keeps its own HTTP/1.1 default, whose connection the HTTP/2 queries would not share.
                        request.Version = httpClient.DefaultRequestVersion;
                        request.VersionPolicy = httpClient.DefaultVersionPolicy;

                        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    }
                    catch (Exception)
                    {
                        // A failed preconnect only means the first query connects as before, and reports any failure itself.
                    }
                });
            }
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
