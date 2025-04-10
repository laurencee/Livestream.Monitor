using System;

namespace ExternalAPIs.Youtube.Dto
{
    public class SearchResult
    {
        public string Kind { get; set; }
        public string Etag { get; set; }
        public Id Id { get; set; }
        public SearchSnippet Snippet { get; set; }
    }

    public class Id
    {
        public string Kind { get; set; }
        public string VideoId { get; set; }
        public string ChannelId { get; set; }
        public string PlaylistId { get; set; }
    }

    public class SearchSnippet
    {
        public DateTimeOffset? PublishedAt { get; set; }
        public string ChannelId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public Thumbnails Thumbnails { get; set; }
        public string ChannelTitle { get; set; }
        public string LiveBroadcastContent { get; set; }
    }
}
