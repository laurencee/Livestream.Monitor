using System;
using System.IO;
using System.Text.RegularExpressions;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model
{
    public sealed class VodDetails
    {
        private static readonly Regex ExcessBlankLines = new(@"\n(?:[^\S\n]*\n){2,}");

        internal static string NormalizeDescription(string description) =>
            ExcessBlankLines.Replace(description.ReplaceLineEndings("\n").Trim(), "\n\n");

        public string Url { get; init; }

        /// <summary>Returns the VOD's HTTP or HTTPS URI for opening in a browser.</summary>
        public Uri GetBrowserUri()
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException($"Invalid VOD URL: '{Url}'. Only HTTP and HTTPS URLs can be opened in a browser.");
            }

            return uri;
        }

        public TimeSpan Length { get; init; }

        public DateTimeOffset RecordedAt { get; init; }

        public string Title { get; init; }

        public string Description { get; init; }

        public string PreviewImage { get; init; }

        public bool IsUpcoming { get; init; }

        public IApiClient ApiClient { get; init; }
    }
}
