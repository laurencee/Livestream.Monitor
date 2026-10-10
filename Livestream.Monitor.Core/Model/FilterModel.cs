using System;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.Model
{
    public class FilterModel : ObservableModel
    {
        /// <summary> A provider name to allow just filtering on the livestream name regardless of stream provider</summary>
        public const string AllApiClientsFilterName = "All";

        /// <summary> Simple check to know if the model is doing any filtering </summary>
        public bool IsFiltering => LivestreamNameFilter != null ||
                                   SelectedApiClientName != AllApiClientsFilterName ||
                                   ShowOnlineOnly;

        public bool ShowOnlineOnly
        {
            get;
            set => Set(ref field, value);
        }

        public string LivestreamNameFilter
        {
            get;
            set => Set(ref field, value);
        }

        public string SelectedApiClientName
        {
            get;
            set => Set(ref field, value);
        }

        public ObservableList<string> ApiClientNames
        {
            get;
            set => Set(ref field, value);
        }

        public bool Matches(LivestreamModel livestream) =>
            Matches(livestream, LivestreamNameFilter, SelectedApiClientName, ShowOnlineOnly);

        /// <summary>
        /// Whether a stream is listed: live when only online streams are shown, from the selected provider unless all
        /// are shown, and with a display name or channel id containing the name filter, if any.
        /// </summary>
        public static bool Matches(
            LivestreamModel livestream,
            string nameFilter,
            string apiClientName,
            bool onlineOnly)
        {
            if (onlineOnly && !livestream.Live) return false;
            if (apiClientName != AllApiClientsFilterName && livestream.ApiClient.ApiName != apiClientName) return false;

            return string.IsNullOrWhiteSpace(nameFilter) ||
                   livestream.DisplayName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                   livestream.ChannelIdentifier.ChannelId.Contains(nameFilter, StringComparison.OrdinalIgnoreCase);
        }
    }
}
