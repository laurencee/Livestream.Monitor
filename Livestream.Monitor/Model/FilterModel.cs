using Caliburn.Micro;

namespace Livestream.Monitor.Model
{
    public class FilterModel : PropertyChangedBase
    {
        private string filter;

        public string Filter
        {
            get { return filter; }
            set
            {
                if (value == filter) return;
                filter = value;
                NotifyOfPropertyChange(() => Filter);
            }
        }
    }
}
