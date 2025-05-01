using System;
using System.Collections.Generic;
using Newtonsoft.Json;

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

public class Livestream
{
    [JsonProperty("broadcaster_user_id")]
    public int BroadcasterUserId { get; set; }

    public Category Category { get; set; }

    [JsonProperty("channel_id")]
    public int ChannelId { get; set; }

    [JsonProperty("has_mature_content")]
    public bool HasMatureContent { get; set; }

    public string Language { get; set; }
    public string Slug { get; set; }

    [JsonProperty("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonProperty("stream_title")]
    public string StreamTitle { get; set; }

    public string Thumbnail { get; set; }

    [JsonProperty("viewer_count")]
    public int ViewerCount { get; set; }
}

public class Channel
{
    [JsonProperty("banner_picture")]
    public string BannerPicture { get; set; }

    [JsonProperty("broadcaster_user_id")]
    public int BroadcasterUserId { get; set; }

    public Category Category { get; set; }

    [JsonProperty("channel_description")]
    public string ChannelDescription { get; set; }

    public string Slug { get; set; }
    public StreamDetails Stream { get; set; }

    [JsonProperty("stream_title")]
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
    [JsonProperty("is_live")]
    public bool IsLive { get; set; }

    [JsonProperty("is_mature")]
    public bool IsMature { get; set; }

    public string Key { get; set; }
    public string Language { get; set; }

    [JsonProperty("start_time")]
    public DateTimeOffset? StartTime { get; set; }

    public string Thumbnail { get; set; }
    public string Url { get; set; }

    [JsonProperty("viewer_count")]
    public int ViewerCount { get; set; }
}