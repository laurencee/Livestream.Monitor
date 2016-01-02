using System;
using Caliburn.Micro;
using Action = System.Action;

namespace Livestream.Monitor.Model.Monitoring
{
    public class LivestreamNotification : PropertyChangedBase
    {
        public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60);

        private int id;
        private string imageUrl;
        private string message;
        private string title;
        private TimeSpan duration = DefaultDuration; // set a good default value

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

        /// <summary> For internal usage by the <see cref="NotificationHandler"/>, will be overwritten if set </summary>
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

        /// <summary> Set to <see cref="DefaultDuration"/> by default </summary>
        public TimeSpan Duration
        {
            get { return duration; }
            set
            {
                if (value.Equals(duration)) return;
                if (value.TotalSeconds < 0) value = TimeSpan.Zero;
                if (value > MaxDuration) value = MaxDuration;

                duration = value;
                NotifyOfPropertyChange(() => Duration);
            }
        }

        public Action ClickAction { get; set; }

        public LivestreamModel LivestreamModel { get; set; }
    }
}