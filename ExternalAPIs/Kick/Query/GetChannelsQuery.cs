using System.Collections.Generic;

namespace ExternalAPIs.Kick.Query;

/// <summary> Allows either <see cref="Slugs"/> or <see cref="BroadcasterUserIds"/> per query. </summary>
public class GetChannelsQuery
{
    /// <summary> Channel names on the end of the url </summary>
    public List<string> Slugs { get; set; } = [];

    public List<int> BroadcasterUserIds { get; set; } = [];
}