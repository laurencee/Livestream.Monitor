using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using ExternalAPIs;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.WPF.Core;
using Livestream.Monitor.WPF.Core.UI;
using Livestream.Monitor.WPF.Model;

namespace Livestream.Monitor.WPF.ViewModels
{
    public class VodListViewModel : PagingConductor<VodDetails>
    {
        private readonly StreamLauncher streamLauncher;
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly IApiClientFactory apiClientFactory;

        private IApiClient selectedApiClient;
        private int queryGeneration;

        #region Design time constructor

        public VodListViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");
        }

        #endregion

        public VodListViewModel(
            StreamLauncher streamLauncher,
            IMonitorStreamsModel monitorStreamsModel,
            IApiClientFactory apiClientFactory)
        {
            this.streamLauncher = streamLauncher ?? throw new ArgumentNullException(nameof(streamLauncher));
            this.monitorStreamsModel = monitorStreamsModel ?? throw new ArgumentNullException(nameof(monitorStreamsModel));
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));
        }

        public override bool CanPrevious => Page > 1 && !LoadingItems;

        public override bool CanNext => !LoadingItems && Items.Count == ItemsPerPage;

        public string StreamDisplayName
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => StreamDisplayName);
                if (!string.IsNullOrWhiteSpace(field))
                {
                    MovePage();
                }
                else
                {
                    queryGeneration++;
                    Items.Clear();
                    LoadingItems = false;
                }
            }
        }

        /// <summary> Known stream names to use for auto-completion </summary>
        public BindableCollection<LivestreamModel> KnownStreams
        {
            get;
            set => Set(ref field, value);
        } = new();

        public BindableCollection<IApiClient> ApiClients { get; set; } = new();

        /// <summary> Should be set before setting the StreamId since the stream id is unique to each api </summary>
        public IApiClient SelectedApiClient
        {
            get { return selectedApiClient; }
            set
            {
                if (Equals(value, selectedApiClient)) return;
                selectedApiClient = value;
                NotifyOfPropertyChange(() => SelectedApiClient);

                // filter livestreams for the newly selected api client to populate known streams
                var orderedLiveStreams = monitorStreamsModel.Livestreams
                                                            .Where(x => x.ApiClient == selectedApiClient)
                                                            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

                // as streamids are unique to each api client, we should clear the stream id when changing api's
                // unless we know it exists in the newly selected api client to avoid immediate query failures
                if (IsActive && StreamDisplayName != null && orderedLiveStreams.All(x => x.Id != StreamDisplayName))
                {
                    StreamDisplayName = null;
                }

                KnownStreams = new BindableCollection<LivestreamModel>(orderedLiveStreams);
                NotifyOfPropertyChange(() => VodTypes);
                SelectedVodType = VodTypes.FirstOrDefault();
            }
        }

        public BindableCollection<string> VodTypes
        {
            get
            {
                var vodTypes = SelectedApiClient?.VodTypes;
                return vodTypes == null ? new BindableCollection<string>() : new BindableCollection<string>(vodTypes);
            }
        }

        public string SelectedVodType
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => SelectedVodType);
                MovePage();
            }
        }

        public bool LoadingItems
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => LoadingItems);
                NotifyOfPropertyChange(() => CanPrevious);
                NotifyOfPropertyChange(() => CanNext);
            }
        }

        public Task OpenVod(VodDetails vodDetails) => streamLauncher.OpenVod(vodDetails, this);

        public async Task CopyVodUrl(VodDetails vodDetails)
        {
            try
            {
                Clipboard.SetDataObject(vodDetails.Url);
            }
            catch (Exception exception)
            {
                var message = "An error occurred attempting to copy the URL: " + exception.ExtractErrorMessage();
                await this.ShowMessageAsync("Error copying URL", message);
            }
        }

        public async Task OpenVodInBrowser(VodDetails vodDetails)
        {
            try
            {
                var uri = vodDetails.GetBrowserUri();
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                await this.ShowMessageAsync("Error opening browser", exception.ExtractErrorMessage());
            }
        }

        protected override async Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            ApiClients.AddRange(apiClientFactory.GetAll().Where(x => x.HasVodViewerSupport));
            await base.OnInitializeAsync(cancellationToken);
        }

        protected override async void OnViewLoaded(object view)
        {
            if (Execute.InDesignMode) return;

            await EnsureItems();
            base.OnViewLoaded(view);
        }

        protected override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            // set twitch as the default platform
            if (SelectedApiClient == null)
                SelectedApiClient = apiClientFactory.Get<TwitchApiClient>();

            await base.OnActivateAsync(cancellationToken);
        }

        protected override async void MovePage()
        {
            await EnsureItems();
            base.MovePage();
        }

        private async Task EnsureItems()
        {
            var queryStreamDisplayName = StreamDisplayName;
            if (!IsActive || string.IsNullOrWhiteSpace(queryStreamDisplayName)) return;

            var generation = ++queryGeneration;
            var provider = selectedApiClient;

            LoadingItems = true;

            try
            {
                Items.Clear();

                var vodQuery = new VodQuery()
                {
                    StreamDisplayName = queryStreamDisplayName,
                    Skip = (Page - 1) * ItemsPerPage,
                    Take = ItemsPerPage,
                };
                if (SelectedVodType != null) vodQuery.VodTypes.Add(SelectedVodType);

                var vods = await provider.GetVods(vodQuery);

                if (IsCurrent()) Items.AddRange(vods);
            }
            catch (HttpRequestWithStatusException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                if (IsCurrent()) await this.ShowMessageAsync("Error", $"Unknown channel '{queryStreamDisplayName}'.");
            }
            catch (Exception ex)
            {
                if (!IsCurrent()) return;

                var message =
                    $"Could not load VODs from '{PlatformNames.GetDisplayName(provider.ApiName)}' for channel '{queryStreamDisplayName}'." +
                    $"{Environment.NewLine}{Environment.NewLine}{ex}";

                await this.ShowMessageAsync("Could not load VODs", message);
            }
            finally
            {
                if (generation == queryGeneration) LoadingItems = false;
            }

            bool IsCurrent() => IsActive && generation == queryGeneration;
        }
    }
}
