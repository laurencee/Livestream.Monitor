using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.WPF.Core;
using Livestream.Monitor.WPF.Core.UI;
using Livestream.Monitor.WPF.Core.Utility;
using Livestream.Monitor.WPF.Model;
using Livestream.Monitor.WPF.Views;
using MahApps.Metro.Controls.Dialogs;
using INavigationService = Livestream.Monitor.WPF.Core.INavigationService;

namespace Livestream.Monitor.WPF.ViewModels
{
    public class LivestreamListViewModel : Screen, IHandle<ExceptionDispatchInfo>
    {
        private readonly StreamLauncher streamLauncher;
        private readonly INavigationService navigationService;
        private readonly ISettingsHandler settingsHandler;
        private readonly DispatcherTimer refreshTimer;

        private bool pendingViewRefresh;
        private bool firstRefreshBatched;
        private int refreshErrorCount, refreshCount;
        private int suspendViewRefreshCount;

        // Caliburn.Micro 4 sets IsActive only after OnActivateAsync completes; the refresh it runs is still on screen.
        private bool activating;

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
            get;
            set => Set(ref field, value);
        }

        public IMonitorStreamsModel StreamsModel { get; }

        /// <summary>True while the list polls for updates, from activation until deactivation.</summary>
        internal bool IsAutoRefreshing => refreshTimer.IsEnabled;

        public FilterModel FilterModel { get; }

        public LivestreamsLayoutMode LayoutModeMode
        {
            get;
            set => Set(ref field, value);
        } = LivestreamsLayoutMode.Grid;

        private bool IsOnScreen => IsActive || activating;

        public async Task RefreshLivestreams()
        {
            refreshTimer.Stop();
            BeginViewRefreshBatch();
            try
            {
                await StreamsModel.RefreshLivestreams();
                refreshErrorCount = 0;

                // only reactive the timer if we're still on this screen
                if (IsOnScreen) refreshTimer.Start();
            }
            catch (AggregateException aggregateException)
            {
                if (!IsOnScreen) return;
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
                if (!IsOnScreen) return;
                refreshErrorCount++;

                await this.ShowMessageAsync("Error refreshing livestreams", ex.ExtractErrorMessage());
                refreshTimer.Start();
            }
            finally
            {
                EndViewRefreshBatch();
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

            var streamUrl = await StreamsModel.SelectedLivestream.GetStreamUrl();
            Process.Start(new ProcessStartInfo(streamUrl) { UseShellExecute = true });
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
                vm.StreamDisplayName = stream.ChannelIdentifier.DisplayName ?? stream.ChannelIdentifier.ChannelId;
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
            var stream = StreamsModel.SelectedLivestream;
            if (stream == null) return;

            var dialogs = new WpfDialogs(this);
            if (await dialogs.ConfirmRemoval(stream)) await StreamsModel.RemoveOrReport(stream, dialogs);

            // return focus to the datagrid after showing the remove livestream dialog
            this.SetFocus("LivestreamListDataGrid");
        }

        public async Task CopyLivestreamUrl()
        {
            if (StreamsModel.SelectedLivestream?.ApiClient == null) return;

            // clipboard.SetDataObject can sometimes fail due to a WPF bug, at least don't crash the app if this happens
            try
            {
                var streamUrl = await StreamsModel.SelectedLivestream.GetStreamUrl();
                Clipboard.SetDataObject(streamUrl);
            }
            catch (Exception e)
            {
                await this.ShowMessageAsync("Error copying url", $"An error occurred attempting to copy the url: {e.Message}");
            }
        }

        protected override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            activating = true;
            Loading = true;
            try
            {
                refreshErrorCount = 0;
                refreshTimer.Tick += RefreshTimerOnTick;
                StreamsModel.LivestreamsRefreshComplete += OnLivestreamsRefreshComplete;
                StreamsModel.PropertyChanged += StreamsModelOnPropertyChanged;
                FilterModel.PropertyChanged += OnFilterModelOnPropertyChanged;
                ViewSource.Source = StreamsModel.Livestreams;

                // load user sort preference
                var customSort = settingsHandler.Settings.LivestreamListSortState;
                if (customSort != null)
                {
                    // online streams can have 0 viewers, we want to treat these the same as other streams with viewers
                    if (customSort.Column.IsEqualTo(nameof(LivestreamModel.Viewers)))
                        ViewSource.SortDescriptions.Add(new SortDescription(nameof(LivestreamModel.Live), ListSortDirection.Descending));

                    ViewSource.SortDescriptions.Add(new SortDescription(customSort.Column, customSort.SortDirection));
                }
                else
                {
                    ViewSource.SortDescriptions.Add(new SortDescription(nameof(LivestreamModel.Live), ListSortDirection.Descending));
                    ViewSource.SortDescriptions.Add(new SortDescription(nameof(LivestreamModel.Viewers), ListSortDirection.Descending));
                }

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

            activating = false;
            Loading = false;
            await base.OnActivateAsync(cancellationToken);
        }

        protected override void OnViewLoaded(object view)
        {
            // fix for DataGridColumn not showing sort icon on initial load
            // CollectionViewSource bindings don't directly touch DataGridColumn.SortDirection for whatever reason
            var livestreamListView = (LivestreamListView)GetView();
            var layoutControl = livestreamListView.FindName("LayoutControl") as ContentControl;
            var dataGrid = FindVisualChild<DataGrid>(layoutControl);

            // if we sorted by viewers then we have 2 sort descriptions as we first sort by live
            var sortDescription = ViewSource.SortDescriptions.Last();
            var column = dataGrid.Columns.First(x => x.SortMemberPath.IsEqualTo(sortDescription.PropertyName));
            column.SortDirection = sortDescription.Direction;

            base.OnViewLoaded(view);
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element)
                    return element;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private async void StreamsModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StreamsModel.Initialised) && StreamsModel.Initialised)
            {
                await RefreshLivestreams();

                // The rows are now shown sorted, but the model's first refresh can still be applying complete results, which it
                // ends by raising LivestreamsRefreshComplete; their view refreshes wait for that.
                if (!StreamsModel.CanRefreshLivestreams && StreamsModel.Livestreams.Any())
                {
                    BeginViewRefreshBatch();
                    firstRefreshBatched = true;
                }
            }
        }

        protected override async Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
        {
            refreshTimer.Tick -= RefreshTimerOnTick;
            refreshTimer.Stop();
            StreamsModel.LivestreamsRefreshComplete -= OnLivestreamsRefreshComplete;
            StreamsModel.PropertyChanged -= StreamsModelOnPropertyChanged;
            FilterModel.PropertyChanged -= OnFilterModelOnPropertyChanged;
            EndFirstRefreshBatch();
            ViewSource.Filter -= ViewSourceOnFilter;
            ViewSource.SortDescriptions.Clear();

            StreamsModel.Livestreams.CollectionChanged -= LivestreamsOnCollectionChanged;
            foreach (LivestreamModel livestream in StreamsModel.Livestreams)
            {
                UnhookLiveStreamEvents(livestream);
            }

            await base.OnDeactivateAsync(close, cancellationToken);
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

            RequestViewRefresh();
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
                RequestViewRefresh();
            }
        }

        public CollectionViewSource ViewSource { get; set; } = new();

        private void ViewSourceOnFilter(object sender, FilterEventArgs e)
        {
            e.Accepted = !FilterModel.IsFiltering || FilterModel.Matches((LivestreamModel)e.Item);
        }

        private void OnLivestreamsRefreshComplete(object sender, EventArgs eventArgs)
        {
            // We only really care about sorting online livestreams so this causes the sort descriptions to be applied immediately
            RequestViewRefresh();
            EndFirstRefreshBatch();
        }

        private void EndFirstRefreshBatch()
        {
            if (!firstRefreshBatched) return;

            firstRefreshBatched = false;
            EndViewRefreshBatch();
        }

        private void OnFilterModelOnPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            RequestViewRefresh();
        }

        private void BeginViewRefreshBatch()
        {
            suspendViewRefreshCount++;
        }

        private void EndViewRefreshBatch()
        {
            if (suspendViewRefreshCount == 0) return;

            suspendViewRefreshCount--;
            if (suspendViewRefreshCount != 0 || !pendingViewRefresh) return;

            pendingViewRefresh = false;
            ViewSource.View?.Refresh();
        }

        private void RequestViewRefresh()
        {
            // Initialised triggers the existing refresh batch after startup rows have been populated.
            if (!StreamsModel.Initialised || suspendViewRefreshCount > 0)
            {
                pendingViewRefresh = true;
                return;
            }

            ViewSource.View?.Refresh();
        }

        public async Task HandleAsync(ExceptionDispatchInfo message, CancellationToken cancellationToken)
        {
            await this.ShowMessageAsync(
                "Error", $"{message.SourceException.ExtractErrorMessage()}{Environment.NewLine}{Environment.NewLine}" +
                         "(TIP: Remove the stream causing the error if it will never resolve itself, e.g. banned channels)");
        }

        public void OnDataGridSorting(DataGridSortingEventArgs e)
        {
            var sortDirection = e.Column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            var bindingPath = e.Column.SortMemberPath;

            var sortState = new DataGridSortState()
            {
                Column = bindingPath,
                SortDirection = sortDirection,
            };
            settingsHandler.Settings.LivestreamListSortState = sortState;
            settingsHandler.SaveSettings();
        }
    }
}
