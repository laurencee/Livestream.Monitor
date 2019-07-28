namespace ExternalAPIs.TwitchTv.V3.Dto.QueryRoot
{
    /// <summary>
    /// JSON root for the stream information, contains additional data we dont care about
    /// </summary>
    public class StreamRoot
    {
        public Stream Stream { get; set; }
    }
}