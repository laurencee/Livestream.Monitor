using Caliburn.Micro;

namespace Livestream.Monitor.Model
{
    public class FilterModel : PropertyChangedBase
    {
        /// <summary> A provider name to allow just filtering on the livestream name regardless of stream provider</summary>
        public const string AllApiClientsFilterName = "All";

        private string livestreamNameFilter;
        private string selectedApiClientName;
        private BindableCollection<string> apiClientNames;
        private bool showOnlineOnly;

        /// <summary> Simple check to know if the model is doing any filtering </summary>
        public bool IsFiltering => LivestreamNameFilter != null ||
                                   SelectedApiClientName != AllApiClientsFilterName ||
                                   ShowOnlineOnly;

        public bool ShowOnlineOnly
        {
            get { return showOnlineOnly; }
            set => Set(ref showOnlineOnly, value);
        }

        public string LivestreamNameFilter
        {
            get { return livestreamNameFilter; }
            set => Set(ref livestreamNameFilter, value);
        }

        public string SelectedApiClientName
        {
            get { return selectedApiClientName; }
            set => Set(ref selectedApiClientName, value);
        }

        public BindableCollection<string> ApiClientNames
        {
            get { return apiClientNames; }
            set => Set(ref apiClientNames, value);
        }
    }
}
