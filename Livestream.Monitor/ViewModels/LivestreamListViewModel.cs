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
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model.Monitoring;
using MahApps.Metro.Controls.Dialogs;

namespace Livestream.Monitor.ViewModels
{
    public class LivestreamListViewModel : Screen
    {
        private readonly StreamLauncher streamLauncher;
        private readonly DispatcherTimer refreshTimer;

        private bool loading;

        public LivestreamListViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            StreamsModel = new MonitorStreamsModel();
        }

        public LivestreamListViewModel(
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
            refreshTimer.Tick += async (sender, args) => await RefreshLivestreams();
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
        
        public async Task RefreshLivestreams()
        {
            refreshTimer.Stop();
            await StreamsModel.RefreshLivestreams();
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
            if (e.Key == Key.Delete && StreamsModel.SelectedLivestream != null)
            {
                await RemoveLivestream();
            }
        }

        public async Task RemoveLivestream()
        {
            if (StreamsModel.SelectedLivestream == null) return;

            var dialogResult = await this.ShowMessageAsync("Remove livestream",
                $"Are you sure you want to remove livestream '{StreamsModel.SelectedLivestream.DisplayName}'?",
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings() { AffirmativeButtonText = "Remove" });

            if (dialogResult == MessageDialogResult.Affirmative)
            {
                StreamsModel.RemoveLivestream(StreamsModel.SelectedLivestream);
            }

            // return focus to the datagrid after showing the remove livestream dialog
            this.SetFocus("LivestreamListDataGrid");
        }

        public void CopyLivestreamUrl()
        {
            if (StreamsModel.SelectedLivestream == null) return;

            Clipboard.SetText($"http://www.twitch.tv/{StreamsModel.SelectedLivestream.DisplayName}");
        }

        protected override async void OnActivate()
        {
            Loading = true;
            try
            {
                StreamsModel.OnlineLivestreamsRefreshComplete += OnOnlineLivestreamsRefreshComplete;
                FilterModel.PropertyChanged += OnFilterModelOnPropertyChanged;
                ViewSource.Source = StreamsModel.Livestreams;
                ViewSource.SortDescriptions.Add(new SortDescription("Viewers", ListSortDirection.Descending));
                ViewSource.SortDescriptions.Add(new SortDescription("Live", ListSortDirection.Descending));
                ViewSource.Filter += ViewSourceOnFilter;

                await RefreshLivestreams();
                // hook up followed livestreams after our initial call so we can refresh immediately as needed
                StreamsModel.Livestreams.CollectionChanged += LivestreamsOnCollectionChanged;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error loading livestream list", ex.Message);
                // TODO - log the error
            }

            Loading = false;
            base.OnActivate();
        }

        

        protected override void OnDeactivate(bool close)
        {
            StreamsModel.OnlineLivestreamsRefreshComplete -= OnOnlineLivestreamsRefreshComplete;
            FilterModel.PropertyChanged -= OnFilterModelOnPropertyChanged;
            ViewSource.Filter -= ViewSourceOnFilter;
            ViewSource.SortDescriptions.Clear();

            StreamsModel.Livestreams.CollectionChanged -= LivestreamsOnCollectionChanged;

            base.OnDeactivate(close);
        }

        private async void LivestreamsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    ViewSource.View.Refresh();
                    break;
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Reset:
                    await RefreshLivestreams();
                    break;
            }
        }

        public CollectionViewSource ViewSource { get; set; } = new CollectionViewSource();

        private void ViewSourceOnFilter(object sender, FilterEventArgs e)
        {
            var f = FilterModel.LivestreamNameFilter;
            if (string.IsNullOrWhiteSpace(f))
            {
                e.Accepted = true;
                return;
            }

            var item = e.Item as LivestreamModel;
            if (item != null && item.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase))
            {
                e.Accepted = true;
                return;
            }

            e.Accepted = false;
        }

        private void OnOnlineLivestreamsRefreshComplete(object sender, EventArgs eventArgs)
        {
            // We only really care about sorting online livestreams so this causes the sort descriptions to be applied immediately 
            ViewSource.View.Refresh();
        }

        private void OnFilterModelOnPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            ViewSource.View.Refresh();
        }
    }
}