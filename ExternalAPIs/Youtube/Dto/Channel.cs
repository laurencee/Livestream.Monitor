using System;

namespace ExternalAPIs.Youtube.Dto
{
    public class Channel
    {
        public string Kind { get; set; }

        public string Etag { get; set; }

        public string Id { get; set; }

        public ChannelSnippet Snippet { get; set; }

        public ChannelContentDetails ContentDetails { get; set; }
    }

    public class ChannelSnippet
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string CustomUrl { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public Thumbnails Thumbnails { get; set; }
        public string DefaultLanguage { get; set; }
        public Localized Localized { get; set; }
        public string Country { get; set; }
    }

    public class ChannelContentDetails
    {
        public RelatedPlaylists RelatedPlaylists { get; set; }
    }

    public class RelatedPlaylists
    {
        public string Likes { get; set; }
        public string Favorites { get; set; }
        public string Uploads { get; set; }
    }
}