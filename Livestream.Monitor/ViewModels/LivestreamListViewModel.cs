using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ExceptionServices;
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
    public class LivestreamListViewModel : Screen, IHandleWithTask<ExceptionDispatchInfo>
    {
        private readonly StreamLauncher streamLauncher;
        private readonly INavigationService navigationService;
        private readonly DispatcherTimer refreshTimer;

        private bool loading;
        private int refreshErrorCount = 0;
        private LivestreamsLayoutMode layoutModeMode = LivestreamsLayoutMode.Grid;

        public LivestreamListViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            StreamsModel = new FakeMonitorStreamsModel();
            FilterModel = new FilterModel();
        }

        public LivestreamListViewModel(
            IMonitorStreamsModel monitorStreamsModel,
            FilterModel filterModel,
            StreamLauncher streamLauncher,
            INavigationService navigationService)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (navigationService == null) throw new ArgumentNullException(nameof(navigationService));

            this.streamLauncher = streamLauncher;
            this.navigationService = navigationService;
            this.StreamsModel = monitorStreamsModel;
            FilterModel = filterModel;
            refreshTimer = new DispatcherTimer { Interval = Constants.RefreshPollingTime };
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

        public LivestreamsLayoutMode LayoutModeMode
        {
            get { return layoutModeMode; }
            set
            {
                if (value == layoutModeMode) return;
                layoutModeMode = value;
                NotifyOfPropertyChange(() => LayoutModeMode);
            }
        }

        public async Task RefreshLivestreams()
        {
            refreshTimer.Stop();
            try
            {
                await StreamsModel.RefreshLivestreams();
                refreshErrorCount = 0;

                // only reactive the timer if we're still on this screen
                if (IsActive) refreshTimer.Start();
            }
            catch (Exception ex)
            {
                if (!IsActive) return;
                refreshErrorCount++;

                // keep trying to refresh until we hit too many consecutive errors
                if (refreshErrorCount >= 3)
                {
                    Execute.OnUIThread(async () =>
                    {
                        await this.ShowMessageAsync("Error refreshing livestreams", ex.ExtractErrorMessage());
                        refreshTimer.Start();
                    });
                }
                else
                {
                    refreshTimer.Start();
                }
            }
        }

        /// <summary> Loads the selected stream through livestreamer and displays a messagebox with the loading process details </summary>
        public void OpenStream()
        {
            if (Loading) return;

            streamLauncher.OpenStream(StreamsModel.SelectedLivestream, StreamsModel.SelectedStreamQuality);
        }

        public void ToggleNotify()
        {
            var stream = StreamsModel.SelectedLivestream;
            if (stream == null) return;

            stream.DontNotify = !stream.DontNotify;
        }

        public void GotoVodViewer()
        {
            var stream = StreamsModel.SelectedLivestream;
            if (stream == null || !stream.ApiClient.HasVodViewerSupport) return;

            navigationService.NavigateTo<VodListViewModel>(vm =>
            {
                vm.SelectedApiClient = stream.ApiClient;
                vm.StreamId = stream.Id;                
            });
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
                try
                {
                    await StreamsModel.RemoveLivestream(StreamsModel.SelectedLivestream.ChannelIdentifier);
                }
                catch (Exception ex)
                {
                    await this.ShowMessageAsync("Error Removing Stream",
                        $"Error removing '{StreamsModel.SelectedLivestream.DisplayName}': {ex.Message}" +
                        Environment.NewLine + Environment.NewLine +
                        $"TIP: You may have to remove the livestream directly from the livestreams.json file... :(");
                }
            }

            // return focus to the datagrid after showing the remove livestream dialog
            this.SetFocus("LivestreamListDataGrid");
        }

        public void CopyLivestreamUrl()
        {
            if (StreamsModel.SelectedLivestream?.ApiClient == null) return;

            Clipboard.SetText(StreamsModel.SelectedLivestream.StreamUrl);
        }

        protected override async void OnActivate()
        {
            Loading = true;
            try
            {
                refreshErrorCount = 0;
                refreshTimer.Tick += RefreshTimerOnTick;
                StreamsModel.LivestreamsRefreshComplete += OnLivestreamsRefreshComplete;
                FilterModel.PropertyChanged += OnFilterModelOnPropertyChanged;
                ViewSource.Source = StreamsModel.Livestreams;
                ViewSource.SortDescriptions.Add(new SortDescription(nameof(LivestreamModel.Live), ListSortDirection.Descending));
                ViewSource.SortDescriptions.Add(new SortDescription(nameof(LivestreamModel.Viewers), ListSortDirection.Descending));
                ViewSource.Filter += ViewSourceOnFilter;

                foreach (LivestreamModel livestream in StreamsModel.Livestreams)
                {
                    HookLiveStreamEvents(livestream);
                }

                if (DateTimeOffset.Now - StreamsModel.LastRefreshTime > Constants.HalfRefreshPollingTime)
                    await RefreshLivestreams(); // will start the refresh timer after refreshing the livestreams
                else
                    refreshTimer.Start(); // need to always make sure the refresh timer is running

                // hook up followed livestreams after our initial call so we can refresh immediately as needed
                StreamsModel.Livestreams.CollectionChanged += LivestreamsOnCollectionChanged;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error loading livestream list", ex.Message);
            }

            Loading = false;
            base.OnActivate();
        }

        protected override void OnDeactivate(bool close)
        {
            refreshTimer.Tick -= RefreshTimerOnTick;
            refreshTimer.Stop();
            StreamsModel.LivestreamsRefreshComplete -= OnLivestreamsRefreshComplete;
            FilterModel.PropertyChanged -= OnFilterModelOnPropertyChanged;
            ViewSource.Filter -= ViewSourceOnFilter;
            ViewSource.SortDescriptions.Clear();

            StreamsModel.Livestreams.CollectionChanged -= LivestreamsOnCollectionChanged;
            foreach (LivestreamModel livestream in StreamsModel.Livestreams)
            {
                UnhookLiveStreamEvents(livestream);
            }

            base.OnDeactivate(close);
        }

        private async void RefreshTimerOnTick(object sender, EventArgs args)
        {
            await RefreshLivestreams();
        }

        private void LivestreamsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (LivestreamModel livestream in e.NewItems)
                {
                    HookLiveStreamEvents(livestream);
                }
            }

            if (e.OldItems != null)
            {
                foreach (LivestreamModel livestream in e.OldItems)
                {
                    UnhookLiveStreamEvents(livestream);
                }
            }

            ViewSource.View.Refresh();
        }

        private void HookLiveStreamEvents(LivestreamModel livestream)
        {
            livestream.PropertyChanged += LivestreamOnPropertyChanged;
        }

        private void UnhookLiveStreamEvents(LivestreamModel livestream)
        {
            livestream.PropertyChanged -= LivestreamOnPropertyChanged;
        }

        private void LivestreamOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (ViewSource.SortDescriptions.Any(x => x.PropertyName == e.PropertyName))
            {
                // make sure streams going online/offline cause the view sort descriptions to be applied immediately
                ViewSource.View.Refresh();
            }
        }

        public CollectionViewSource ViewSource { get; set; } = new CollectionViewSource();

        private void ViewSourceOnFilter(object sender, FilterEventArgs e)
        {
            var livestreamModel = (LivestreamModel)e.Item;
            if (!FilterModel.IsFiltering)
            {
                e.Accepted = true;
            }
            else
            {
                var apiClientNameFilter = FilterModel.SelectedApiClientName;
                var nameFilter = FilterModel.LivestreamNameFilter;

                bool filterNameMatch = string.IsNullOrWhiteSpace(nameFilter) ||
                                       livestreamModel.DisplayName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                                       livestreamModel.ChannelIdentifier.ChannelId.Contains(nameFilter, StringComparison.OrdinalIgnoreCase);

                bool apiClientMatch = apiClientNameFilter == FilterModel.AllApiClientsFilterName ||
                                      livestreamModel.ApiClient.ApiName == apiClientNameFilter;

                e.Accepted = filterNameMatch && apiClientMatch;
            }
        }

        private void OnLivestreamsRefreshComplete(object sender, EventArgs eventArgs)
        {
            // We only really care about sorting online livestreams so this causes the sort descriptions to be applied immediately 
            ViewSource.View.Refresh();
        }

        private void OnFilterModelOnPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            ViewSource.View.Refresh();
        }

        public async Task Handle(ExceptionDispatchInfo message)
        {
            await this.ShowMessageAsync(
                "Error", $"{message.SourceException.Message}{Environment.NewLine}{Environment.NewLine}" +
                         "(TIP: Remove the stream causing the error if it will never resolve itself, e.g. banned channels)");
        }
    }
}