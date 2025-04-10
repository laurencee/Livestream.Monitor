using System;

namespace ExternalAPIs.Youtube.Dto
{
    public class PlaylistItem
    {
        public string Kind { get; set; }
        public string Etag { get; set; }
        public string Id { get; set; }
        public PlaylistSnippet Snippet { get; set; }
        public PlaylistItemContentDetails ContentDetails { get; set; }
        public Status Status { get; set; }
    }

    public class PlaylistSnippet
    {
        public DateTimeOffset? PublishedAt { get; set; }
        public string ChannelId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public Thumbnails Thumbnails { get; set; }
        public string ChannelTitle { get; set; }
        public string VideoOwnerChannelTitle { get; set; }
        public string VideoOwnerChannelId { get; set; }
        public string PlaylistId { get; set; }
        public uint Position { get; set; }
        public ResourceId ResourceId { get; set; }
    }

    public class ResourceId
    {
        public string Kind { get; set; }
        public string VideoId { get; set; }
    }

    public class PlaylistItemContentDetails
    {
        public string VideoId { get; set; }
        public string StartAt { get; set; }
        public string EndAt { get; set; }
        public string Note { get; set; }
        public DateTimeOffset? VideoPublishedAt { get; set; }
    }

    public class Status
    {
        public string PrivacyStatus { get; set; }
    }
}