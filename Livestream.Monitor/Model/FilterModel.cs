using Caliburn.Micro;

namespace Livestream.Monitor.Model
{
    public class FilterModel : PropertyChangedBase
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

        public BindableCollection<string> ApiClientNames
        {
            get;
            set => Set(ref field, value);
        }
    }
}
