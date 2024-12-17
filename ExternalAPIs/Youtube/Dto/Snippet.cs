using System;
using System.Collections.Generic;

namespace ExternalAPIs.Youtube.Dto
{
    public class Snippet
    {
        public DateTimeOffset? PublishedAt { get; set; }

        public string ChannelId { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public string CustomUrl { get; set; }

        public string Country { get; set; }

        public Thumbnails Thumbnails { get; set; }

        public string ChannelTitle { get; set; }

        public List<string> Tags { get; set; }

        public string CategoryId { get; set; }

        public string LiveBroadcastContent { get; set; }

        public Localized Localized { get; set; }

        public string DefaultAudioLanguage { get; set; }
    }
}