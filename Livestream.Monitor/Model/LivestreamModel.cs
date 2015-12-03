using System;
using Caliburn.Micro;
using TwitchTv.Dto;

namespace Livestream.Monitor.Model
{
    public class LivestreamModel : PropertyChangedBase
    {
        private DateTimeOffset startTime;
        private long viewers;
        private string game;
        private string description;
        private string displayName;
        private bool live;
        private bool isPartner;
        private PreviewImage previewImage;

        /// <summary> The unique identifier for the livestream </summary>
        public string Id { get; set; }

        /// <summary> The name of the service who hosts this livestream (twitchtv, youtube etc.) </summary>
        public string StreamProvider { get; set; }

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

        public string DisplayName
        {
            get { return displayName; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(DisplayName));
                if (value == displayName) return;
                displayName = value;
                NotifyOfPropertyChange(() => DisplayName);
            }
        }

        public string Description
        {
            get { return description; }
            set
            {
                if (value == description) return;
                description = value;
                NotifyOfPropertyChange(() => Description);
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

        public PreviewImage PreviewImage
        {
            get { return previewImage; }
            set
            {
                if (Equals(value, previewImage)) return;
                previewImage = value;
                NotifyOfPropertyChange(() => PreviewImage);
            }
        }

        /// <summary> The username this livestream came from via importing (twitch allows importing followed streams) </summary>
        public string ImportedBy { get; set; }

        public TimeSpan Uptime => Live ? DateTimeOffset.Now - StartTime : TimeSpan.Zero;

        /// <summary> Sets the livestream to the offline state </summary>
        public void Offline()
        {
            Live = false;
            Viewers = 0;
            StartTime = DateTimeOffset.MinValue;
        }

        public override string ToString() => 
            $"{displayName}, Viewers={viewers}, Uptime={Uptime.ToString("hh'h 'mm'm 'ss's'")}";

        #region Equality members

        protected bool Equals(LivestreamModel other)
        {
            return string.Equals(displayName, other.displayName);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((LivestreamModel) obj);
        }

        public override int GetHashCode()
        {
            return displayName?.GetHashCode() ?? 0;
        }

        #endregion
    }
}
