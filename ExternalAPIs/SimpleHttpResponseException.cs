using System.Net;
using System.Net.Http;

namespace ExternalAPIs
{
    /// <summary>  A <see cref="HttpRequestException"/> with the <see cref="HttpStatusCode"/> preserved </summary>
    public class HttpRequestWithStatusException(HttpStatusCode statusCode, string message)
        : HttpRequestException(message)
    {
        public HttpStatusCode StatusCode { get; private set; } = statusCode;
    }
}