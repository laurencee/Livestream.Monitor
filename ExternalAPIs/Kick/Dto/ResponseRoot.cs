using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ExternalAPIs.Kick.Dto;

public class ResponseRoot<T>
{
    public T Data { get; set; }

    public string Message { get; set; }
}

public class LivestreamsRoot : ResponseRoot<List<Livestream>>
{
    public LivestreamsRoot() => Data = [];
}

public class ChannelsRoot : ResponseRoot<List<Channel>>
{
    public ChannelsRoot() => Data = [];
}

public class User
{
    [JsonPropertyName("profile_picture")]
    public string ProfilePicture { get; set; }
}

public class Livestream
{
    [JsonPropertyName("broadcaster_user_id")]
    public int BroadcasterUserId { get; set; }

    public Category Category { get; set; }

    [JsonPropertyName("channel_id")]
    public int ChannelId { get; set; }

    [JsonPropertyName("has_mature_content")]
    public bool HasMatureContent { get; set; }

    public string Language { get; set; }
    public string Slug { get; set; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonPropertyName("stream_title")]
    public string StreamTitle { get; set; }

    public string Thumbnail { get; set; }

    [JsonPropertyName("viewer_count")]
    public int ViewerCount { get; set; }
}

public class Channel
{
    [JsonPropertyName("banner_picture")]
    public string BannerPicture { get; set; }

    [JsonPropertyName("broadcaster_user_id")]
    public int BroadcasterUserId { get; set; }

    public Category Category { get; set; }

    [JsonPropertyName("channel_description")]
    public string ChannelDescription { get; set; }

    public string Slug { get; set; }
    public StreamDetails Stream { get; set; }

    [JsonPropertyName("stream_title")]
    public string StreamTitle { get; set; }
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Thumbnail { get; set; }
}

public class StreamDetails
{
    [JsonPropertyName("is_live")]
    public bool IsLive { get; set; }

    [JsonPropertyName("is_mature")]
    public bool IsMature { get; set; }

    public string Key { get; set; }
    public string Language { get; set; }

    [JsonPropertyName("start_time")]
    public DateTimeOffset? StartTime { get; set; }

    public string Thumbnail { get; set; }
    public string Url { get; set; }

    [JsonPropertyName("viewer_count")]
    public int ViewerCount { get; set; }
}
