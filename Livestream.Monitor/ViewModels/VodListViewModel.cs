using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.Model.StreamProviders;

namespace Livestream.Monitor.ViewModels
{
    public class VodListViewModel : PagingConductor<VodDetails>
    {
        private const int VOD_TILES_PER_PAGE = 15;

        private readonly StreamLauncher streamLauncher;
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly IStreamProviderFactory streamProviderFactory;

        private string streamId;
        private string vodUrl;
        private VodDetails selectedItem;
        private BindableCollection<LivestreamModel> knownStreams = new BindableCollection<LivestreamModel>();
        private bool loadingItems;
        private IStreamProvider selectedStreamProvider;
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
            IStreamProviderFactory streamProviderFactory)
        {
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (streamProviderFactory == null) throw new ArgumentNullException(nameof(streamProviderFactory));

            this.streamLauncher = streamLauncher;
            this.monitorStreamsModel = monitorStreamsModel;
            this.streamProviderFactory = streamProviderFactory;

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
                UpdateItems();
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

        public BindableCollection<IStreamProvider> StreamProviders { get; set; } = new BindableCollection<IStreamProvider>();

        public IStreamProvider SelectedStreamProvider
        {
            get { return selectedStreamProvider; }
            set
            {
                if (Equals(value, selectedStreamProvider)) return;
                selectedStreamProvider = value;
                NotifyOfPropertyChange(() => SelectedStreamProvider);
                NotifyOfPropertyChange(() => VodTypes);
                SelectedVodType = VodTypes.FirstOrDefault();
            }
        }

        public BindableCollection<string> VodTypes
        {
            get
            {
                var vodTypes = SelectedStreamProvider?.VodTypes;
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
                UpdateItems();
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
            StreamProviders.AddRange(streamProviderFactory.GetAll().Where(x => x.HasVodViewerSupport));
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
            var orderedStream = monitorStreamsModel.Livestreams.OrderBy(x => x.Id);
            KnownStreams = new BindableCollection<LivestreamModel>(orderedStream);
            // set twitch as the default stream provider
            SelectedStreamProvider = streamProviderFactory.Get<TwitchStreamProvider>();

            base.OnActivate();
        }

        protected override async void MovePage()
        {
            await EnsureItems();
            base.MovePage();
        }

        // Just a hack to allow the property to call EnsureItems since properties can't do async calls inside getters/setters natively
        private async void UpdateItems()
        {
            await EnsureItems();
        }

        private async Task EnsureItems()
        {
            if (string.IsNullOrWhiteSpace(StreamId)) return;

            LoadingItems = true;

            try
            {
                Items.Clear();

                var vodQuery = new VodQuery()
                {
                    StreamId = StreamId,
                    Skip = (Page - 1) * ItemsPerPage,
                    Take = ItemsPerPage,
                };
                vodQuery.VodTypes.Add(SelectedVodType);

                var vods = await selectedStreamProvider.GetVods(vodQuery);

                Items.AddRange(vods);
            }
            catch (HttpRequestWithStatusException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await this.ShowMessageAsync("Error", $"Unknown stream name '{StreamId}'.");
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error",
                    $"An error occured attempting to get top twitch streams.{Environment.NewLine}{Environment.NewLine}{ex}");
            }

            LoadingItems = false;
        }
    }
}