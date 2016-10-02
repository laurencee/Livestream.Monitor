using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.ViewModels
{
    public class VodListViewModel : PagingConductor<VodDetails>
    {
        private const int VOD_TILES_PER_PAGE = 15;

        private readonly StreamLauncher streamLauncher;
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly IApiClientFactory apiClientFactory;

        private string streamId;
        private string vodUrl;
        private VodDetails selectedItem;
        private BindableCollection<LivestreamModel> knownStreams = new BindableCollection<LivestreamModel>();
        private bool loadingItems;
        private IApiClient selectedApiClient;
        private string selectedVodType;

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
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (apiClientFactory == null) throw new ArgumentNullException(nameof(apiClientFactory));

            this.streamLauncher = streamLauncher;
            this.monitorStreamsModel = monitorStreamsModel;
            this.apiClientFactory = apiClientFactory;

            ItemsPerPage = VOD_TILES_PER_PAGE;
        }

        public override bool CanPrevious => Page > 1 && !LoadingItems;

        public override bool CanNext => !LoadingItems && Items.Count == VOD_TILES_PER_PAGE;

        public string StreamId
        {
            get { return streamId; }
            set
            {
                if (value == streamId) return;
                streamId = value;
                NotifyOfPropertyChange(() => StreamId);
                if (!string.IsNullOrWhiteSpace(streamId))                
                    MovePage();                
                else                
                    Items.Clear();                
            }
        }

        /// <summary> Known stream names to use for auto-completion </summary>
        public BindableCollection<LivestreamModel> KnownStreams
        {
            get { return knownStreams; }
            set
            {
                if (Equals(value, knownStreams)) return;
                knownStreams = value;
                NotifyOfPropertyChange(() => KnownStreams);
            }
        }

        public BindableCollection<IApiClient> ApiClients { get; set; } = new BindableCollection<IApiClient>();

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
                                                            .OrderBy(x => x.Id).ToList();

                // as streamids are unique to each api client, we should clear the stream id when changing api's 
                // unless we know it exists in the newly selected api client to avoid immediate query failures
                if (IsActive && StreamId != null && !orderedLiveStreams.Any(x => x.Id == StreamId))
                {
                    StreamId = null;
                }

                KnownStreams = new BindableCollection<LivestreamModel>(orderedLiveStreams);
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
            get { return selectedVodType; }
            set
            {
                if (value == selectedVodType) return;
                selectedVodType = value;
                NotifyOfPropertyChange(() => SelectedVodType);
                MovePage();
            }
        }

        public string VodUrl
        {
            get { return vodUrl; }
            set
            {
                if (value == vodUrl) return;
                vodUrl = value;
                NotifyOfPropertyChange(() => VodUrl);
                NotifyOfPropertyChange(() => CanOpenVod);
            }
        }

        public bool LoadingItems
        {
            get { return loadingItems; }
            set
            {
                if (value == loadingItems) return;
                loadingItems = value;
                NotifyOfPropertyChange(() => LoadingItems);
                NotifyOfPropertyChange(() => CanPrevious);
                NotifyOfPropertyChange(() => CanNext);
            }
        }

        public VodDetails SelectedItem
        {
            get { return selectedItem; }
            set
            {
                if (Equals(value, selectedItem)) return;
                selectedItem = value;
                NotifyOfPropertyChange(() => SelectedItem);
                VodUrl = selectedItem.Url;
            }
        }

        public bool CanOpenVod => !string.IsNullOrWhiteSpace(VodUrl) &&
                                  Uri.IsWellFormedUriString(VodUrl, UriKind.Absolute);

        public void VodClicked(VodDetails vodDetails)
        {
            SelectedItem = vodDetails;
        }

        public void OpenVod()
        {
            if (!CanOpenVod) return;

            VodDetails vodDetails;
            if (SelectedItem != null && selectedItem.Url == VodUrl)
                vodDetails = SelectedItem;
            else
                vodDetails = new VodDetails() { Url = vodUrl };

            streamLauncher.OpenVod(vodDetails);
        }

        protected override void OnInitialize()
        {
            ApiClients.AddRange(apiClientFactory.GetAll().Where(x => x.HasVodViewerSupport));
            base.OnInitialize();
        }

        protected override async void OnViewLoaded(object view)
        {
            if (Execute.InDesignMode) return;

            await EnsureItems();
            base.OnViewLoaded(view);
        }

        protected override void OnActivate()
        {
            // set twitch as the default stream provider
            if (SelectedApiClient == null)
                SelectedApiClient = apiClientFactory.Get<TwitchApiClient>();

            base.OnActivate();
        }

        protected override async void MovePage()
        {
            await EnsureItems();
            base.MovePage();
        }

        private async Task EnsureItems()
        {
            var newStreamId = StreamId; // avoid possible case of the stream id changes mid query
            if (!IsActive || string.IsNullOrWhiteSpace(newStreamId)) return;

            LoadingItems = true;

            try
            {
                Items.Clear();

                var vodQuery = new VodQuery()
                {
                    StreamId = newStreamId,
                    Skip = (Page - 1) * ItemsPerPage,
                    Take = ItemsPerPage,
                };
                vodQuery.VodTypes.Add(SelectedVodType);

                var vods = await selectedApiClient.GetVods(vodQuery);

                Items.AddRange(vods);
            }
            catch (HttpRequestWithStatusException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await this.ShowMessageAsync("Error", $"Unknown stream name '{newStreamId}'.");
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error Getting Vods",
                    $"Error occured getting vods from api '{SelectedApiClient.ApiName}' for channel '{newStreamId}'." +
                    $"{Environment.NewLine}{Environment.NewLine}{ex}");
            }

            LoadingItems = false;
        }
    }
}