using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ExternalAPIs.GitHub
{
    public class GitHubClient
    {
        private const string BaseUrl = "https://api.github.com";
        private const string Repository = "Livestream.Monitor";
        private const string Username = "laurencee";
        private const string ApiVersion = "2022-11-28";

        public Task<Release> GetLatestRelease(CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/repos/{Username}/{Repository}/releases/latest";
            return ExecuteRequest<Release>(url, cancellationToken);
        }

        private static async Task<T> ExecuteRequest<T>(string request, CancellationToken cancellationToken)
        {
            using var httpClient = HttpClientExtensions.CreateCompressionHttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Livestream.Monitor");
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
            return await httpClient.ExecuteRequest<T>(request, cancellationToken);
        }
    }

    public class Release
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }

        [JsonProperty("html_url")]
        public string HtmlUrl { get; set; }
    }
} 