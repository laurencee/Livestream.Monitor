using System;
using System.Collections.Generic;

namespace ExternalAPIs.Kick.Query;

public class GetLivestreamsQuery
{
    public const int MaxBroadcasterUserIds = 50;
    private int? limit;

    /// <summary> Optional filter to specific channels. </summary>
    /// <remarks> Max supported user ids per request is <see cref="MaxBroadcasterUserIds"/> </remarks>
    public List<int> BroadcasterUserIds { get; set; } = new List<int>();

    public int? CategoryId { get; set; }

    public string Language { get; set; }

    /// <summary> Max number of responses per request </summary>
    /// <remarks> Null defaults to 25 from kick api, max limit is <see cref="RequestConstants.MaxRequestLimit"/> </remarks>
    public int? Limit
    {
        get => limit;
        set
        {
            if (limit > RequestConstants.MaxRequestLimit || limit < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(value), $"Limit must be between 1 and {RequestConstants.MaxRequestLimit}. Value was {value}.");
            
            limit = value;
        }
    }

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