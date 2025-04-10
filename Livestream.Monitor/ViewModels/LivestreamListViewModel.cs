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
using INavigationService = Livestream.Monitor.Core.INavigationService;
using System.Diagnostics;

namespace Livestream.Monitor.ViewModels
{
    public class LivestreamListViewModel : Screen, IHandleWithTask<ExceptionDispatchInfo>
    {
        private readonly StreamLauncher streamLauncher;
        private readonly INavigationService navigationService;
        private readonly ISettingsHandler settingsHandler;
        private readonly DispatcherTimer refreshTimer;

        private bool loading;
        private int refreshErrorCount, refreshCount;
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
            INavigationService navigationService,
            ISettingsHandler settingsHandler)
        {
            this.streamLauncher = streamLauncher ?? throw new ArgumentNullException(nameof(streamLauncher));
            this.navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            this.StreamsModel = monitorStreamsModel ?? throw new ArgumentNullException(nameof(monitorStreamsModel));
            FilterModel = filterModel;
            refreshTimer = new DispatcherTimer { Interval = Constants.RefreshPollingTime };
        }

        public bool Loading
        {
            get => loading;
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
            catch (AggregateException aggregateException)
            {
                if (!IsActive) return;
                refreshErrorCount++;

                // keep trying to refresh until we hit too many consecutive errors unless it's our first query
                if (refreshCount == 0 || refreshErrorCount >= 3)
                {
                    if (!settingsHandler.Settings.DisableRefreshErrorDialogs)
                    {
                        foreach (var ex in aggregateException.InnerExceptions)
                        {
                            var messageDialogResult = await this.ShowMessageAsync(
                                "Error refreshing livestreams", ex.ExtractErrorMessage(),
                                MessageDialogStyle.AffirmativeAndNegative,
                                new MetroDialogSettings()
                                {
                                    NegativeButtonText = "Ignore"
                                });

                            if (messageDialogResult == MessageDialogResult.Negative)
                                StreamsModel.IgnoreQueryFailure(ex.ExtractErrorMessage());
                        }
                    }

                    refreshTimer.Start();
                }
                else
                {
                    refreshTimer.Start();
                }
            }
            catch (Exception ex)
            {
                if (!IsActive) return;
                refreshErrorCount++;

                await this.ShowMessageAsync("Error refreshing livestreams", ex.ExtractErrorMessage());
            }

            refreshCount++;
        }

        /// <summary>
        /// Loads the selected stream through livestreamer/streamlink
        /// and displays a messagebox with the loading process details
        /// </summary>
        public async Task OpenStream()
        {
            if (Loading) return;

            await streamLauncher.OpenStream(StreamsModel.SelectedLivestream, this);
        }

        public async Task OpenStreamInBrowser()
        {
            if (Loading || StreamsModel.SelectedLivestream == null) return;

            var streamUrl = await StreamsModel.SelectedLivestream.GetStreamUrl;
            Process.Start(streamUrl);
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
                vm.StreamDisplayName = stream.ChannelIdentifier.DisplayName;
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
                    await StreamsModel.RemoveLivestream(StreamsModel.SelectedLivestream.ChannelIdentifier, this);
                }
                catch (Exception ex)
                {
                    await this.ShowMessageAsync("Error Removing Stream",
                        $"Error removing '{StreamsModel.SelectedLivestream.DisplayName}': {ex.ExtractErrorMessage()}" +
                        Environment.NewLine + Environment.NewLine +
                        $"TIP: You may have to remove the livestream directly from the livestreams.json file... :(");
                }
            }

            // return focus to the datagrid after showing the remove livestream dialog
            this.SetFocus("LivestreamListDataGrid");
        }

        public async Task CopyLivestreamUrl()
        {
            if (StreamsModel.SelectedLivestream?.ApiClient == null) return;

            // clipboard.SetDataObject can sometimes fail due to a WPF bug, at least don't crash the app if this happens
            try
            {
                var streamUrl = await StreamsModel.SelectedLivestream.GetStreamUrl;
                Clipboard.SetDataObject(streamUrl);
            }
            catch (Exception e)
            {
                await this.ShowMessageAsync("Error copying url",
                    $"An error occurred attempting to copy the url, please try again: {e.Message}");
            }
        }

        protected override async void OnActivate()
        {
            Loading = true;
            try
            {
                refreshErrorCount = 0;
                refreshTimer.Tick += RefreshTimerOnTick;
                StreamsModel.LivestreamsRefreshComplete += OnLivestreamsRefreshComplete;
                StreamsModel.PropertyChanged += StreamsModelOnPropertyChanged;
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
                await this.ShowMessageAsync("Error loading livestream list", ex.ExtractErrorMessage());
            }

            Loading = false;
            base.OnActivate();
        }

        private async void StreamsModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StreamsModel.Initialised) && StreamsModel.Initialised)
            {
                await RefreshLivestreams();
            }
        }

        protected override void OnDeactivate(bool close)
        {
            refreshTimer.Tick -= RefreshTimerOnTick;
            refreshTimer.Stop();
            StreamsModel.LivestreamsRefreshComplete -= OnLivestreamsRefreshComplete;
            StreamsModel.PropertyChanged -= StreamsModelOnPropertyChanged;
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
                // quick exits for least expensive paths
                if (FilterModel.ShowOnlineOnly && !livestreamModel.Live)
                {
                    e.Accepted = false;
                    return;
                }

                var apiClientNameFilter = FilterModel.SelectedApiClientName;
                if (apiClientNameFilter != FilterModel.AllApiClientsFilterName &&
                    livestreamModel.ApiClient.ApiName != apiClientNameFilter)
                {
                    e.Accepted = false;
                    return;
                }

                var nameFilter = FilterModel.LivestreamNameFilter;
                bool filterNameMatch = string.IsNullOrWhiteSpace(nameFilter) ||
                                       livestreamModel.DisplayName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                                       livestreamModel.ChannelIdentifier.ChannelId.Contains(nameFilter, StringComparison.OrdinalIgnoreCase);

                e.Accepted = filterNameMatch;
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
                "Error", $"{message.SourceException.ExtractErrorMessage()}{Environment.NewLine}{Environment.NewLine}" +
                         "(TIP: Remove the stream causing the error if it will never resolve itself, e.g. banned channels)");
        }
    }
}