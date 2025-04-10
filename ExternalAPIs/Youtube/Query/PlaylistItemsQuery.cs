using System;

namespace ExternalAPIs.Youtube.Query;

public class PlaylistItemsQuery(string playlistId)
{
    private int itemsPerPage = 50;

    public string PlaylistId { get; } = playlistId ?? throw new ArgumentNullException(nameof(playlistId));
    public string PageToken { get; set; }

    /// <summary> Defaults to 50 </summary>
    public int ItemsPerPage
    {
        get => itemsPerPage;
        set
        {
            if (value > 50)
                throw new ArgumentOutOfRangeException(nameof(ItemsPerPage), $"ItemsPerPage {value} above allowed max 50");

            itemsPerPage = value;
        }
    }
}