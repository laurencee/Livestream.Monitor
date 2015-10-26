using Caliburn.Micro;

namespace Livestream.Monitor.Model
{
    public class ChannelNotification : PropertyChangedBase
    {
        private int id;
        private string imageUrl;
        private string message;
        private string title;

        public string Message
        {
            get { return message; }
            set
            {
                if (message == value) return;
                message = value;
                NotifyOfPropertyChange();
            }
        }

        public int Id
        {
            get { return id; }
            set
            {
                if (id == value) return;
                id = value;
                NotifyOfPropertyChange();
            }
        }

        public string ImageUrl
        {
            get { return imageUrl; }
            set
            {
                if (imageUrl == value) return;
                imageUrl = value;
                NotifyOfPropertyChange();
            }
        }

        public string Title
        {
            get { return title; }
            set
            {
                if (title == value) return;
                title = value;
                NotifyOfPropertyChange();
            }
        }

        public ChannelData ChannelData { get; set; }
    }
}