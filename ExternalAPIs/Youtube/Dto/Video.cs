using System;
using System.Collections.Generic;

namespace ExternalAPIs.Youtube.Dto
{
    public class Video
    {
        public string Kind { get; set; }

        public string Etag { get; set; }

        public string Id { get; set; }

        public LiveStreamingDetails LiveStreamingDetails { get; set; }

        public VideoSnippet Snippet { get; set; }

        public VideoContentDetails ContentDetails { get; set; }

        public Statistics Statistics { get; set; }

        public RecordingDetails RecordingDetails { get; set; }

        // fileDetails are only available to the video owner so we can't use that
        // it would have been nice as it has the duration in ms instead of a string format along with other data
    }

    public class LiveStreamingDetails
    {
        public DateTimeOffset? ActualStartTime { get; set; }

        public DateTimeOffset? ScheduledStartTime { get; set; }

        public int ConcurrentViewers { get; set; }

        /// <summary> Only set when livestream has enabled chat </summary>
        public string ActiveLiveChatId { get; set; }
    }

    public class VideoSnippet
    {
        public DateTimeOffset? PublishedAt { get; set; }
        public string ChannelId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public Thumbnails Thumbnails { get; set; }
        public string ChannelTitle { get; set; }
        public List<string> Tags { get; set; }
        public string CategoryId { get; set; }
        public string LiveBroadcastContent { get; set; }
        public string DefaultLanguage { get; set; }
        public Localized Localized { get; set; }
        public string DefaultAudioLanguage { get; set; }
    }

    public class VideoContentDetails
    {
        /// <summary> ISO8601 duration PnYnMnDTnHnMnS </summary>
        public string Duration { get; set; }
        public string Dimension { get; set; }
        public string Definition { get; set; }
        public string Caption { get; set; }
        public bool LicensedContent { get; set; }
        public string Projection { get; set; }
        public bool HasCustomThumbnail { get; set; }
    }

    public class Statistics
    {
        // the api description of these are ulongs but the json example on the api docs has them as strings
        // they are actually numeric
        public long ViewCount { get; set; }
        public long LikeCount { get; set; }
        public long DislikeCount { get; set; }
        public long FavoriteCount { get; set; }
        public long CommentCount { get; set; }
    }

    public class RecordingDetails
    {
        public DateTimeOffset? RecordingDate { get; set; }
    }
}