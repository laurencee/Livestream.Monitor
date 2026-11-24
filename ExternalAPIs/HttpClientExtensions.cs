using System;
using System.IO;
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

        private const int MaxConnectionsPerServer = 8;

        // Shared so the connection pool stays alive across short-lived request clients.
        private static readonly HttpClientHandler SharedHandler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = MaxConnectionsPerServer,
            UseCookies = false,
        };

        private static readonly string[] ProviderOrigins = ["https://api.twitch.tv/", "https://www.googleapis.com/"];

        /// <summary> Creates a request client sharing the application's connection pool. </summary>
        public static HttpClient CreateCompressionHttpClient()
        {
            // Use HTTP/2 so parallel requests can share a connection.
            return new HttpClient(SharedHandler, disposeHandler: false)
            {
                Timeout = QueryTimeout,
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
        }

        /// <summary> Opens provider connections in the background to reduce the first query's connection delay. </summary>
        public static void PreconnectProviderApis()
        {
            foreach (var origin in ProviderOrigins)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Use the API root to warm up the connection without making a data request.
                        using var httpClient = CreateCompressionHttpClient();
                        using var request = new HttpRequestMessage(HttpMethod.Head, origin);

                        // SendAsync uses the message's HTTP version, not the client's default.
                        request.Version = httpClient.DefaultRequestVersion;
                        request.VersionPolicy = httpClient.DefaultVersionPolicy;

                        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    }
                    catch (Exception)
                    {
                        // The first query can connect normally if preconnect fails.
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

            // With ResponseHeadersRead the client's own timeout ends once the headers arrive, so this deadline also covers reading the body.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(httpClient.Timeout);

            try
            {
                var responseTask = httpClient.GetAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

                using var httpResponseMessage = await responseTask.ConfigureAwait(false);
                await httpResponseMessage.EnsureSuccessStatusCodeAsync(deadline.Token).ConfigureAwait(false);

                await using var body = await httpResponseMessage.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<T>(body, JsonDefaults.Options, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out executing web request", e);
            }
            catch (IOException e)
            {
                // A connection lost while the body is read fails the stream read; callers handle it as a failed request.
                throw new HttpRequestException(e.Message, e);
            }
        }

        private static async Task EnsureSuccessStatusCodeAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode) return;

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            response.Content?.Dispose();

            var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
            var message = string.IsNullOrWhiteSpace(content) ? status : $"{status}: {content}";
            throw new HttpRequestWithStatusException(response.StatusCode, message);
        }
    }
}
