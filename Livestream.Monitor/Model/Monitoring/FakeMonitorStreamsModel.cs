using System;
using System.Threading.Tasks;
using Caliburn.Micro;

namespace Livestream.Monitor.Model.Monitoring
{
    public class FakeMonitorStreamsModel : PropertyChangedBase, IMonitorStreamsModel
    {
        private static readonly Random Random = new Random();
        private LivestreamModel selectedLivestream;

        public FakeMonitorStreamsModel()
        {
            Livestreams = new BindableCollection<LivestreamModel>();

            for (int i = 0; i < 10; i++)
            {
                var livestream = new LivestreamModel()
                {
                    Id = "Livestream " + i,
                    DisplayName = "Livestream " + i,
                };

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

        public event EventHandler OnlineLivestreamsRefreshComplete;

        public Task AddLivestream(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));
            Livestreams.Add(livestreamModel);

            return Task.CompletedTask;
        }

        public Task ImportFollows(string username)
        {
            return Task.CompletedTask;
        }

        public Task RefreshLivestreams()
        {
            OnOnlineLivestreamsRefreshComplete();
            return Task.CompletedTask;
        }

        public void RemoveLivestream(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));
            Livestreams.Remove(livestreamModel);
        }

        protected virtual void OnOnlineLivestreamsRefreshComplete()
        {
            OnlineLivestreamsRefreshComplete?.Invoke(this, EventArgs.Empty);
        }
    }
}