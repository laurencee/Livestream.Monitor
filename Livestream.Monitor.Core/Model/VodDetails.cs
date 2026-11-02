using Livestream.Monitor.Core;
using System;
using System.IO;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model
{
    public class VodDetails : ObservableModel
    {
        public string Url
        {
            get;
            set => Set(ref field, value);
        }

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

        public TimeSpan Length
        {
            get;
            set => Set(ref field, value);
        }

        public long Views
        {
            get;
            set => Set(ref field, value);
        }

        public DateTimeOffset RecordedAt
        {
            get;
            set => Set(ref field, value);
        }

        public string Title
        {
            get;
            set => Set(ref field, value);
        }

        public string Description
        {
            get;
            set => Set(ref field, value);
        }

        public string Game
        {
            get;
            set => Set(ref field, value);
        }

        public string PreviewImage
        {
            get;
            set => Set(ref field, value);
        }

        public bool IsUpcoming
        {
            get;
            set => Set(ref field, value);
        }

        public IApiClient ApiClient
        {
            get;
            set => Set(ref field, value);
        }
    }
}
