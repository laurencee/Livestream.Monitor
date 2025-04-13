namespace ExternalAPIs.Kick.Query;

public class GetLivestreamsQuery
{
    /// <summary> Optional to filter by a single channel, usually you won't want to do this as <see cref="GetChannelsQuery"/> can the same data in batch requests </summary>
    public string BroadcasterUserId { get; set; }

    public int? CategoryId { get; set; }

    public string Language { get; set; }

    /// <summary> Leaving this null defaults to 25, max limit is 100 from the API </summary>
    public int? Limit { get; set; }

    /// <summary> Setting this to <see cref="LivestreamsSort.ViewerCount"/> gives the top streams </summary>
    public LivestreamsSort Sort { get; set; }

    public int Page { get; set; } = 1;
}

public enum LivestreamsSort
{
    None,
    ViewerCount,
    StartedAt,
}