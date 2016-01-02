using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using TwitchTv;
using TwitchTv.Query;

namespace Livestream.Monitor.ViewModels
{
    public class VodListViewModel : PagingConductor<VodDetails>
    {
        private const int VOD_TILES_PER_PAGE = 15;

        private readonly StreamLauncher streamLauncher;
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ITwitchTvReadonlyClient twitchTvClient;

        private string vodFilter;
        private string streamName;
        private string vodUrl;
        private VodDetails selectedItem;
        private BindableCollection<string> knownStreamNames = new BindableCollection<string>();
        private bool loadingItems;

        public VodListViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            
        }

        public VodListViewModel(
            StreamLauncher streamLauncher,
            IMonitorStreamsModel monitorStreamsModel,
            ITwitchTvReadonlyClient twitchTvClient)
        {
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));

            this.streamLauncher = streamLauncher;
            this.monitorStreamsModel = monitorStreamsModel;
            this.twitchTvClient = twitchTvClient;

            ItemsPerPage = VOD_TILES_PER_PAGE;
        }

        public string StreamName
        {
            get { return streamName; }
            set
            {
                if (value == streamName) return;
                streamName = value;
                NotifyOfPropertyChange(() => StreamName);
                UpdateItems();
            }
        }

        /// <summary> Known stream names to use for auto-completion </summary>
        public BindableCollection<string> KnownStreamNames
        {
            get { return knownStreamNames; }
            set
            {
                if (Equals(value, knownStreamNames)) return;
                knownStreamNames = value;
                NotifyOfPropertyChange(() => KnownStreamNames);
            }
        }

        public string VodFilter
        {
            get { return vodFilter; }
            set
            {
                if (value == vodFilter) return;
                vodFilter = value;
                NotifyOfPropertyChange(() => VodFilter);
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

        protected override async void OnViewLoaded(object view)
        {
            if (Execute.InDesignMode) return;

            await EnsureItems();
            base.OnViewLoaded(view);
        }

        protected override void OnActivate()
        {
            var orderedStreamNames = monitorStreamsModel.Livestreams.Select(x => x.DisplayName).OrderBy(x => x);
            KnownStreamNames = new BindableCollection<string>(orderedStreamNames);

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
            if (string.IsNullOrWhiteSpace(StreamName)) return;

            LoadingItems = true;

            try
            {
                Items.Clear();

                var channelVideosQuery = new ChannelVideosQuery()
                {
                    ChannelName = StreamName,
                    BroadcastsOnly = true,
                    HLSVodsOnly = true,
                    Skip = (Page - 1) * ItemsPerPage,
                    Take = ItemsPerPage,
                };
                var channelVideos = await twitchTvClient.GetChannelVideos(channelVideosQuery);

                var vods = channelVideos.Select(channelVideo => new VodDetails
                {
                    Url = channelVideo.Url,
                    Length = TimeSpan.FromSeconds(channelVideo.Length),
                    RecordedAt = channelVideo.RecordedAt,
                    Views = channelVideo.Views,
                    Game = channelVideo.Game,
                    Description = channelVideo.Description,
                    Title = channelVideo.Title
                });

                Items.AddRange(vods);
            }
            catch (HttpRequestException ex) when (ex.Message == "Response status code does not indicate success: 404 (Not Found).")
            {
                await this.ShowMessageAsync("Error", $"Unknown stream name '{StreamName}'.");
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