using Caliburn.Micro;

namespace Livestream.Monitor.ViewModels
{
    public class HeaderViewModel : Screen
    {
        private string streamName;

        public bool CanAddStream => !string.IsNullOrWhiteSpace(StreamName);

        public string StreamName
        {
            get { return streamName; }
            set
            {
                if (value == streamName) return;
                streamName = value;
                NotifyOfPropertyChange(() => StreamName);
                NotifyOfPropertyChange(() => CanAddStream);
            }
        }

        public void AddStream()
        {
        }
    }
}