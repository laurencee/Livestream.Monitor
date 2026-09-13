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

        public string Message
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                NotifyOfPropertyChange();
            }
        }

        public string ImageUrl
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                NotifyOfPropertyChange();
            }
        }

        public string Title
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                NotifyOfPropertyChange();
            }
        }

        /// <summary> Set to <see cref="DefaultDuration"/> by default </summary>
        public TimeSpan Duration
        {
            get;
            set
            {
                if (value.Equals(field)) return;
                if (value.TotalSeconds < 0) value = TimeSpan.Zero;
                if (value > MaxDuration) value = MaxDuration;

                field = value;
                NotifyOfPropertyChange(() => Duration);
            }
        } = DefaultDuration;

        public Action<IMonitorStreamsModel, LivestreamNotification> ClickAction { get; set; } = DefaultClickAction;

        public LivestreamModel LivestreamModel { get; set; }
    }
}