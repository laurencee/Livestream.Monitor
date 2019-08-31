using System;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model
{
    public class LivestreamModel : PropertyChangedBase
    {
        private DateTimeOffset? startTime;
        private long viewers;
        private string game;
        private string description;
        private string displayName;
        private bool live;
        private bool isPartner;
        private ThumbnailUrls thumbnailUrls;
        private bool dontNotify;
        private DateTimeOffset? lastLiveTime;
        private string broadcasterLanguage;
        private string language;

        public LivestreamModel()
        {
            if (!Execute.InDesignMode) throw new InvalidOperationException("Design time only constructor");
            Id = "DesignTimeId";
            ChannelIdentifier = new ChannelIdentifier();
            UniqueStreamKey = new UniqueStreamKey();
        }

        /// <summary> A livestream object from an Api/Channel </summary>
        /// <param name="id">A unique id for this livestream for its <see cref="ApiClient"/></param>
        /// <param name="channelIdentifier">The <see cref="ApiClient"/> and unique channel identifier for that api client where this stream came from</param>
        public LivestreamModel(string id, ChannelIdentifier channelIdentifier)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("Argument is null or whitespace", nameof(id));

            Id = id;
            ChannelIdentifier = channelIdentifier ?? throw new ArgumentNullException(nameof(channelIdentifier));
            UniqueStreamKey = new UniqueStreamKey(ApiClient.ApiName, Id);
        }

        /// <summary> The unique identifier for the livestream </summary>
        public string Id { get; }

        public ChannelIdentifier ChannelIdentifier { get; }

        public IApiClient ApiClient => ChannelIdentifier.ApiClient;

        /// <summary> This key is unique between all api client, it has a string representation and equality members. </summary>
        public UniqueStreamKey UniqueStreamKey { get; }

        public bool Live
        {
            get => live;
            set
            {
                if (value == live) return;
                live = value;
                NotifyOfPropertyChange(() => Live);
                NotifyOfPropertyChange(() => Uptime);

                // must update after notifying property change to give time to inspect LastLiveTime property before live state changed
                if (live)
                    LastLiveTime = DateTimeOffset.Now;
            }
        }

        public string DisplayName
        {
            get => displayName;
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
            get => description;
            set
            {
                if (value == description) return;
                description = value;
                NotifyOfPropertyChange(() => Description);
            }
        }

        public string Game
        {
            get => game;
            set
            {
                if (value == game) return;
                game = value;
                NotifyOfPropertyChange(() => Game);
            }
        }

        public long Viewers
        {
            get => viewers;
            set
            {
                if (value == viewers) return;
                viewers = value;
                NotifyOfPropertyChange(() => Viewers);
            }
        }

        public DateTimeOffset? StartTime
        {
            get => startTime;
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
            get => isPartner;
            set
            {
                if (value == isPartner) return;
                isPartner = value;
                NotifyOfPropertyChange();
            }
        }

        public ThumbnailUrls ThumbnailUrls
        {
            get => thumbnailUrls;
            set
            {
                if (Equals(value, thumbnailUrls)) return;
                thumbnailUrls = value;
                NotifyOfPropertyChange(() => ThumbnailUrls);
            }
        }

        public string BroadcasterLanguage
        {
            get => broadcasterLanguage;
            set
            {
                if (value == broadcasterLanguage) return;
                broadcasterLanguage = value;
                NotifyOfPropertyChange(() => BroadcasterLanguage);
            }
        }

        public string Language
        {
            get => language;
            set
            {
                if (value == language) return;
                language = value;
                NotifyOfPropertyChange(() => Language);
            }
        }

        public Task<string> GetStreamUrl => ApiClient?.GetStreamUrl(ChannelIdentifier.ChannelId);

        public Task<string> GetChatUrl => ApiClient?.GetChatUrl(ChannelIdentifier.ChannelId);

        /// <summary> The username this livestream came from via importing (twitch allows importing followed streams) </summary>
        public string ImportedBy { get; set; }

        public TimeSpan Uptime => Live && StartTime.HasValue ? DateTimeOffset.Now - StartTime.Value : TimeSpan.Zero;

        public DateTimeOffset? LastLiveTime
        {
            get => lastLiveTime;
            private set
            {
                if (value.Equals(lastLiveTime)) return;
                lastLiveTime = value;
                NotifyOfPropertyChange(() => LastLiveTime);
            }
        }

        /// <summary> Exclude this livestream from raising popup notifications </summary>
        public bool DontNotify
        {
            get => dontNotify;
            set
            {
                if (value == dontNotify) return;
                dontNotify = value;
                NotifyOfPropertyChange(() => DontNotify);
            }
        }

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
            return Equals(UniqueStreamKey, other.UniqueStreamKey);
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
            return UniqueStreamKey?.GetHashCode() ?? 0;
        }

        #endregion
    }
}
