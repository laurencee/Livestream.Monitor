using System;
using Caliburn.Micro;
using TwitchTv.Dto;

namespace Livestream.Monitor.Model
{
    public class ChannelData : PropertyChangedBase
    {
        private DateTimeOffset startTime;
        private long viewers;
        private string game;
        private string channelDescription;
        private string channelName;
        private bool live;
        private bool isPartner;
        private Preview preview;

        public bool Live
        {
            get { return live; }
            set
            {
                if (value == live) return;
                live = value;
                NotifyOfPropertyChange(() => Live);
                NotifyOfPropertyChange(() => Uptime);
            }
        }

        public string ChannelName
        {
            get { return channelName; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ChannelName));
                if (value == channelName) return;
                channelName = value;
                NotifyOfPropertyChange(() => ChannelName);
            }
        }

        public string ChannelDescription
        {
            get { return channelDescription; }
            set
            {
                if (value == channelDescription) return;
                channelDescription = value;
                NotifyOfPropertyChange(() => ChannelDescription);
            }
        }

        public string Game
        {
            get { return game; }
            set
            {
                if (value == game) return;
                game = value;
                NotifyOfPropertyChange(() => Game);
            }
        }

        public long Viewers
        {
            get { return viewers; }
            set
            {
                if (value == viewers) return;
                viewers = value;
                NotifyOfPropertyChange(() => Viewers);
            }
        }

        public DateTimeOffset StartTime
        {
            get { return startTime; }
            set
            {
                if (value.Equals(startTime)) return;
                startTime = value;
                NotifyOfPropertyChange(() => StartTime);
                NotifyOfPropertyChange(() => Uptime);
            }
        }

        public bool IsPartner
        {
            get { return isPartner; }
            set
            {
                if (value == isPartner) return;
                isPartner = value;
                NotifyOfPropertyChange();
            }
        }

        public Preview Preview
        {
            get { return preview; }
            set
            {
                if (Equals(value, preview)) return;
                preview = value;
                NotifyOfPropertyChange(() => Preview);
            }
        }

        /// <summary> The username this Channel came from by importing their follow list </summary>
        public string ImportedBy { get; set; }

        public TimeSpan Uptime => Live ? DateTimeOffset.Now - StartTime : TimeSpan.Zero;

        /// <summary> Sets the channel to the offline state </summary>
        public void Offline()
        {
            Live = false;
            Viewers = 0;
            StartTime = DateTimeOffset.MinValue;
        }

        public override string ToString() => 
            $"{channelName}, Viewers={viewers}, Uptime={Uptime.ToString("hh'h 'mm'm 'ss's'")}";

        #region Equality members

        protected bool Equals(ChannelData other)
        {
            return string.Equals(channelName, other.channelName);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ChannelData) obj);
        }

        public override int GetHashCode()
        {
            return channelName?.GetHashCode() ?? 0;
        }

        #endregion
    }
}
