using System;

namespace ExternalAPIs.Youtube.Query;

public class PlaylistItemsQuery(string playlistId)
{
    public string PlaylistId { get; } = playlistId ?? throw new ArgumentNullException(nameof(playlistId));
    public string PageToken { get; set; }

    /// <summary> Defaults to 50 </summary>
    public int ItemsPerPage
    {
        get;
        set
        {
            if (value > 50)
                throw new ArgumentOutOfRangeException(nameof(ItemsPerPage),
                    $"ItemsPerPage {value} above allowed max 50");

            field = value;
        }
    } = 50;
}