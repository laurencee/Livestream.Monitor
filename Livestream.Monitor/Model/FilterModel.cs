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

        /// <summary> Simple check to know if the model is doing any filtering </summary>
        public bool IsFiltering => LivestreamNameFilter != null || SelectedApiClientName != AllApiClientsFilterName;

        public string LivestreamNameFilter
        {
            get { return livestreamNameFilter; }
            set
            {
                if (value == livestreamNameFilter) return;
                livestreamNameFilter = value;
                NotifyOfPropertyChange(() => LivestreamNameFilter);
            }
        }

        public string SelectedApiClientName
        {
            get { return selectedApiClientName; }
            set
            {
                if (Equals(value, selectedApiClientName)) return;
                selectedApiClientName = value;
                NotifyOfPropertyChange(() => SelectedApiClientName);
            }
        }

        public BindableCollection<string> ApiClientNames
        {
            get { return apiClientNames; }
            set
            {
                if (Equals(value, apiClientNames)) return;
                apiClientNames = value;
                NotifyOfPropertyChange(() => ApiClientNames);
            }
        }
    }
}
