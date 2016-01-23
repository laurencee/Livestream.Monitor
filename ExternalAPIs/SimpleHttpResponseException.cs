using System.Net;
using System.Net.Http;

namespace ExternalAPIs
{
    /// <summary>  A <see cref="HttpRequestException"/> with the <see cref="HttpStatusCode"/> preserved </summary>
    public class HttpRequestWithStatusException : HttpRequestException
    {
        public HttpRequestWithStatusException(HttpStatusCode statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; private set; }
    }
}