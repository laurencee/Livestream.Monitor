using System;
using System.Linq;
using Caliburn.Micro;

namespace Livestream.Monitor.Model.Monitoring
{
    public class LivestreamNotification : PropertyChangedBase
    {
        public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60);

        public static readonly Action<IMonitorStreamsModel, LivestreamNotification> DefaultClickAction = (model, notification) =>
        {
            var livestream = model.Livestreams.FirstOrDefault(x => Equals(x, notification.LivestreamModel));
            if (livestream != null)
            {
                model.SelectedLivestream = livestream;
            }
        };

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

        public Action<IMonitorStreamsModel, LivestreamNotification> ClickAction { get; set; } = DefaultClickAction;

        public LivestreamModel LivestreamModel { get; set; }
    }
}