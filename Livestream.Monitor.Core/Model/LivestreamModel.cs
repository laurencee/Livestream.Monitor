using Livestream.Monitor.Core;
using System;
using System.Threading.Tasks;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model
{
    public class LivestreamModel : ObservableModel
    {
        private (string Id, string Name) lastKnownCategory;

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

        /// <summary> Unique across all api clients. Has a string representation and equality members. </summary>
        public UniqueStreamKey UniqueStreamKey { get; }

        public bool Live
        {
            get;
            set => Set(ref field, value);
        }

        public string DisplayName
        {
            get;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(DisplayName));

                Set(ref field, value);
            }
        }

        public string Description
        {
            get;
            set => Set(ref field, value);
        }

        public string Game
        {
            get;
            set => Set(ref field, value);
        }

        internal string GameId { get; set; }

        public long Viewers
        {
            get;
            set => Set(ref field, value);
        }

        public DateTimeOffset? StartTime
        {
            get;
            set => Set(ref field, value);
        }

        public ThumbnailUrls ThumbnailUrls
        {
            get;
            set => Set(ref field, value);
        }

        public string BroadcasterLanguage
        {
            get;
            set => Set(ref field, value);
        }

        public Task<string> GetStreamUrl() => ApiClient?.GetStreamUrl(this);

        public Task<string> GetChatUrl() => ApiClient?.GetChatUrl(this);

        public TimeSpan Uptime => Live && StartTime.HasValue ? DateTimeOffset.Now - StartTime.Value : TimeSpan.Zero;

        public bool SupportsCategoryChangeNotifications => ApiClient?.ApiName is TwitchApiClient.API_NAME or KickApiClient.API_NAME;

        public bool IsChatDisabled
        {
            get;
            set => Set(ref field, value);
        }

        /// <summary> Sets the livestream to the offline state </summary>
        public void Offline()
        {
            Live = false;
            Viewers = 0;
            StartTime = DateTimeOffset.MinValue;
        }

        /// <summary>Applies a provider snapshot and returns the new category if it changed during the same live session.</summary>
        internal string ApplyUpdate(LivestreamModel update)
        {
            var sameSession = Live && update.Live && StartTime == update.StartTime;
            var previous = lastKnownCategory;
            if (previous.Name == null && !string.IsNullOrWhiteSpace(Game)) previous = (GameId, Game);

            BroadcasterLanguage = update.BroadcasterLanguage;
            Description = update.Description?.Trim();
            DisplayName = update.DisplayName;
            Game = update.Game;
            GameId = update.GameId;
            ThumbnailUrls = update.ThumbnailUrls;

            Viewers = update.Viewers;
            StartTime = update.StartTime;
            Live = update.Live;

            if (!sameSession) lastKnownCategory = default;
            if (!Live || string.IsNullOrWhiteSpace(Game)) return null;

            lastKnownCategory = (GameId, Game);
            if (!sameSession || previous.Name == null) return null;

            var sameCategory = !string.IsNullOrEmpty(previous.Id) && !string.IsNullOrEmpty(GameId)
                ? previous.Id == GameId
                : previous.Name.Equals(Game, StringComparison.OrdinalIgnoreCase);

            if (sameCategory && string.IsNullOrEmpty(GameId)) lastKnownCategory.Id = previous.Id;

            return sameCategory ? null : Game;
        }

        public override string ToString() =>
            $"{DisplayName}, Viewers={Viewers}, Uptime={Uptime.ToString("hh'h 'mm'm 'ss's'")}";

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
            return Equals((LivestreamModel)obj);
        }

        public override int GetHashCode()
        {
            return UniqueStreamKey?.GetHashCode() ?? 0;
        }

        #endregion
    }
}
