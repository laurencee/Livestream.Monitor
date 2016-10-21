using System;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model.Monitoring
{
    public class FakeMonitorStreamsModel : PropertyChangedBase, IMonitorStreamsModel
    {
        private static readonly Random Random = new Random();
        private LivestreamModel selectedLivestream;
        private DateTimeOffset lastRefreshTime;

        public FakeMonitorStreamsModel()
        {
            Livestreams = new BindableCollection<LivestreamModel>();

            for (int i = 0; i < 10; i++)
            {
                var livestream = new LivestreamModel("Livestream " + i, null);
                livestream.DisplayName = "Livestream " + i;

                if (i < 3)
                    SetStreamOnline(livestream);
                else
                    SetStreamOffline(livestream);

                Livestreams.Add(livestream);
            }
        }

        public static void SetStreamOnline(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));

            livestreamModel.Live = true;
            livestreamModel.Viewers = GetRandomViewerCount();
            livestreamModel.StartTime = GetRandomStreamStartTime();
            livestreamModel.Game = "Online Game";
            livestreamModel.Description = "Doing something online right now!";
        }

        public static void SetStreamOffline(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));

            livestreamModel.Live = false;
            livestreamModel.Viewers = 0;
            livestreamModel.StartTime = DateTimeOffset.MinValue;
            livestreamModel.Game = "Offline Game";
            livestreamModel.Description = "Time for bed";
        }

        private static int GetRandomViewerCount()
        {
            return Random.Next(minValue: 100, maxValue: 20000);
        }

        private static DateTimeOffset GetRandomStreamStartTime()
        {
            return DateTimeOffset.Now.AddSeconds(-Random.Next(minValue: 5, maxValue: 50000));
        }

        public BindableCollection<LivestreamModel> Livestreams { get; }

        public LivestreamModel SelectedLivestream
        {
            get { return selectedLivestream; }
            set
            {
                if (Equals(value, selectedLivestream)) return;
                selectedLivestream = value;
                NotifyOfPropertyChange(() => SelectedLivestream);
            }
        }

        public bool CanRefreshLivestreams => true;

        public DateTimeOffset LastRefreshTime
        {
            get { return lastRefreshTime; }
            private set
            {
                if (value.Equals(lastRefreshTime)) return;
                lastRefreshTime = value;
                NotifyOfPropertyChange(() => LastRefreshTime);
            }
        }

        public string SelectedStreamQuality { get; set; }

        public bool CanOpenStream { get; }

        public event EventHandler LivestreamsRefreshComplete;

        public Task AddLivestream(ChannelIdentifier channelIdentifier, IViewAware viewAware)
        {
            if (channelIdentifier == null) throw new ArgumentNullException(nameof(channelIdentifier));
            Livestreams.Add(new LivestreamModel(channelIdentifier.ChannelId, channelIdentifier));

            return Task.CompletedTask;
        }

        public Task ImportFollows(string username, IApiClient apiClient)
        {
            return Task.CompletedTask;
        }

        public Task RefreshLivestreams()
        {
            OnOnlineLivestreamsRefreshComplete();
            LastRefreshTime = DateTimeOffset.Now;
            return Task.CompletedTask;
        }

        public Task RemoveLivestream(ChannelIdentifier channelIdentifier)
        {
            if (channelIdentifier == null) throw new ArgumentNullException(nameof(channelIdentifier));
            var matchingLivestreams = Livestreams.Where(x => Equals(channelIdentifier, x.ChannelIdentifier)).ToList();
            Livestreams.RemoveRange(matchingLivestreams);
            return Task.CompletedTask;
        }

        protected virtual void OnOnlineLivestreamsRefreshComplete()
        {
            LivestreamsRefreshComplete?.Invoke(this, EventArgs.Empty);
        }
    }
}