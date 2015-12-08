using Caliburn.Micro;

namespace Livestream.Monitor.Model
{
    public class FilterModel : PropertyChangedBase
    {
        private string livestreamNameFilter;

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
    }
}
