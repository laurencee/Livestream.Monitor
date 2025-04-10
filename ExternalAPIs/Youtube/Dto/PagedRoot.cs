using System.Collections.Generic;

namespace ExternalAPIs.Youtube.Dto
{
    public class PagedRoot<T>
    {
        public string Kind { get; set; }
        public string Etag { get; set; }
        public string NextPageToken { get; set; }
        public string PrevPageToken { get; set; }
        public PageInfo PageInfo { get; set; }
        public List<T> Items { get; set; }
    }

    public class VideosRoot : PagedRoot<Video> { }

    public class ChannelsRoot : PagedRoot<Channel> { }

    public class PlaylistItemsRoot : PagedRoot<PlaylistItem> { }

    public class SearchLiveVideosRoot : PagedRoot<SearchResult> { }
}