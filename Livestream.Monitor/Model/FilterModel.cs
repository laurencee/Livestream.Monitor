using Caliburn.Micro;

namespace Livestream.Monitor.Model
{
    public class FilterModel : PropertyChangedBase
    {
        /// <summary> A provider name to allow just filtering on the livestream name regardless of stream provider</summary>
        public const string AllStreamProviderFilterName = "All";

        private string livestreamNameFilter;
        private string selectedStreamProviderName;
        private BindableCollection<string> streamProviderNames;

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

        public string SelectedStreamProviderName
        {
            get { return selectedStreamProviderName; }
            set
            {
                if (Equals(value, selectedStreamProviderName)) return;
                selectedStreamProviderName = value;
                NotifyOfPropertyChange(() => SelectedStreamProviderName);
            }
        }

        public BindableCollection<string> StreamProviderNames
        {
            get { return streamProviderNames; }
            set
            {
                if (Equals(value, streamProviderNames)) return;
                streamProviderNames = value;
                NotifyOfPropertyChange(() => StreamProviderNames);
            }
        }
    }
}
