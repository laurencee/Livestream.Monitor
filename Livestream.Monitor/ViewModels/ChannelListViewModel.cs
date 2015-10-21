using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using Caliburn.Micro;
using Livestream.Monitor.Model;

namespace Livestream.Monitor.ViewModels
{
    public class ChannelListViewModel : Screen
    {
        private readonly StreamLauncher streamLauncher;
        private readonly DispatcherTimer refreshTimer;

        private bool loading;

        public ChannelListViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            StreamsModel = new MonitorStreamsModel();
        }

        public ChannelListViewModel(
            IMonitorStreamsModel monitorStreamsModel,
            StreamLauncher streamLauncher)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));

            this.streamLauncher = streamLauncher;
            this.StreamsModel = monitorStreamsModel;
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            refreshTimer.Tick += async (sender, args) => await RefreshChannels();
        }

        public bool Loading
        {
            get { return loading; }
            set
            {
                if (value == loading) return;
                loading = value;
                NotifyOfPropertyChange(() => Loading);
            }
        }

        public IMonitorStreamsModel StreamsModel { get; }

        public CollectionViewSource ViewSource { get; set; } = new CollectionViewSource();

        public async Task RefreshChannels()
        {
            refreshTimer.Stop();
            await StreamsModel.RefreshChannels();
            refreshTimer.Start();
        }

        /// <summary> Loads the selected stream through livestreamer and displays a messagebox with the loading process details </summary>
        public void StartStream()
        {
            if (Loading) return;

            streamLauncher.StartStream();
        }

        public void RemoveChannel()
        {
            if (StreamsModel.SelectedChannel == null) return;

            StreamsModel.RemoveChannel(StreamsModel.SelectedChannel);
        }

        protected override async void OnActivate()
        {
            Loading = true;
            try
            {
                StreamsModel.OnlineChannelsRefreshComplete += OnOnlineChannelsRefreshComplete;
                ViewSource.Source = StreamsModel.Channels;
                ViewSource.SortDescriptions.Add(new SortDescription("Viewers", ListSortDirection.Descending));
                ViewSource.SortDescriptions.Add(new SortDescription("Live", ListSortDirection.Descending));

                await RefreshChannels();
                // hook up followed channels after our initial call so we can refresh immediately as needed
                StreamsModel.Channels.CollectionChanged += FollowedChannelsOnCollectionChanged;
            }
            catch (Exception)
            {
                // TODO - show the error to the user and log it
            }

            Loading = false;
            base.OnActivate();
        }

        private void OnOnlineChannelsRefreshComplete(object sender, EventArgs eventArgs)
        {
            // We only really care about sorting online channels so this causes the sort descriptions to be applied immediately 
            ViewSource.View.Refresh();
        }

        private async void FollowedChannelsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    ViewSource.View.Refresh();
                    break;
                case NotifyCollectionChangedAction.Remove:
                    break;
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Reset:
                    await RefreshChannels();
                    break;
            }
        }
    }
}