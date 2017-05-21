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

        /// <summary> Creates a new HttpClient with compression enabled </summary>
        public static HttpClient CreateCompressionHttpClient()
        {
            return new HttpClient(new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            }) { Timeout = QueryTimeout };
        }

        public static async Task<T> ExecuteRequest<T>(string request, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await ExecuteRequest<T>(CreateCompressionHttpClient(), request, cancellationToken);
        }

        public static async Task<T> ExecuteRequest<T>(this HttpClient httpClient, string request, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(request)) throw new ArgumentNullException(nameof(request));
            
            using (httpClient)
            {
                try
                {
                    var httpResponseMessage = await httpClient.GetAsync(request, cancellationToken);
                    await httpResponseMessage.EnsureSuccessStatusCodeAsync();
                    var response = await httpResponseMessage.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<T>(response);
                }
                catch (TaskCanceledException e)
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
