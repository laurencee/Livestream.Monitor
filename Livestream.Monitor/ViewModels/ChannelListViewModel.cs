using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Caliburn.Micro;
using Livestream.Monitor.Model;
using Livestream.Monitor.Core;
using MahApps.Metro.Controls.Dialogs;

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
            FilterModel filterModel,
            StreamLauncher streamLauncher)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));

            this.streamLauncher = streamLauncher;
            this.StreamsModel = monitorStreamsModel;
            FilterModel = filterModel;
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

        public FilterModel FilterModel { get; }
        
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

        public async Task DataGridKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Delete && StreamsModel.SelectedChannel != null)
            {
                await RemoveChannel();
            }
        }

        public async Task RemoveChannel()
        {
            if (StreamsModel.SelectedChannel == null) return;

            var dialogResult = await this.ShowMessageAsync("Remove channel",
                $"Are you sure you want to remove channel '{StreamsModel.SelectedChannel.ChannelName}'?",
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings() { AffirmativeButtonText = "Remove" });

            if (dialogResult == MessageDialogResult.Affirmative)
            {
                StreamsModel.RemoveChannel(StreamsModel.SelectedChannel);
            }

            // return focus to the datagrid after showing the remove channel dialog
            this.SetFocus("ChannelListDataGrid");
        }

        public void CopyChannelUrl()
        {
            if (StreamsModel.SelectedChannel == null) return;

            Clipboard.SetText($"http://www.twitch.tv/{StreamsModel.SelectedChannel.ChannelName}");
        }

        protected override async void OnActivate()
        {
            Loading = true;
            try
            {
                StreamsModel.OnlineChannelsRefreshComplete += OnOnlineChannelsRefreshComplete;
                FilterModel.PropertyChanged += (sender, args) => ViewSource.View.Refresh();
                ViewSource.Source = StreamsModel.Channels;
                ViewSource.SortDescriptions.Add(new SortDescription("Viewers", ListSortDirection.Descending));
                ViewSource.SortDescriptions.Add(new SortDescription("Live", ListSortDirection.Descending));
                ViewSource.Filter += ViewSourceOnFilter;

                await RefreshChannels();
                // hook up followed channels after our initial call so we can refresh immediately as needed
                StreamsModel.Channels.CollectionChanged += ChannelsOnCollectionChanged;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error loading channel list", ex.Message);
                // TODO - log the error
            }

            Loading = false;
            base.OnActivate();
        }

        private async void ChannelsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    ViewSource.View.Refresh();
                    break;
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Reset:
                    await RefreshChannels();
                    break;
            }
        }

        public CollectionViewSource ViewSource { get; set; } = new CollectionViewSource();

        private void ViewSourceOnFilter(object sender, FilterEventArgs e)
        {
            var f = FilterModel.Filter;
            if (string.IsNullOrWhiteSpace(f))
            {
                e.Accepted = true;
                return;
            }

            var item = e.Item as ChannelData;
            if (item != null && item.ChannelName.Contains(f, StringComparison.OrdinalIgnoreCase))
            {
                e.Accepted = true;
                return;
            }

            e.Accepted = false;
        }

        private void OnOnlineChannelsRefreshComplete(object sender, EventArgs eventArgs)
        {
            // We only really care about sorting online channels so this causes the sort descriptions to be applied immediately 
            ViewSource.View.Refresh();
        }
    }
}