using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using Caliburn.Micro;
using Livestream.Monitor.Model;
using TwitchTv;
using static System.String;

namespace Livestream.Monitor.ViewModels
{
    public class ChannelListViewModel : Screen
    {
        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private bool loading;
        private readonly DispatcherTimer refreshTimer;
        private ChannelData selectedChannelData;

        public ChannelListViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            PopulateDesignData();
        }

        public ChannelListViewModel(ITwitchTvReadonlyClient twitchTvClient)
        {
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            this.twitchTvClient = twitchTvClient;
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

        public ChannelData SelectedChannelData
        {
            get { return selectedChannelData; }
            set
            {
                if (Equals(value, selectedChannelData)) return;
                selectedChannelData = value;
                NotifyOfPropertyChange(() => SelectedChannelData);
            }
        }


        public CollectionViewSource ViewSource { get; set; } = new CollectionViewSource();

        public BindableCollection<ChannelData> ChannelData { get; set; } = new BindableCollection<ChannelData>();

        protected override async void OnActivate()
        {
            Loading = true;
            try
            {
                ViewSource.Source = ChannelData;
                ViewSource.SortDescriptions.Add(new SortDescription("Viewers", ListSortDirection.Descending));
                ViewSource.SortDescriptions.Add(new SortDescription("Live", ListSortDirection.Descending));

                var userFollows = await twitchTvClient.GetUserFollows("fxfighter");
                var channelDatas = userFollows.Follows.Select(x => x.Channel.ToChannelData());
                ChannelData.AddRange(channelDatas);

                await RefreshChannels();
            }
            catch (Exception ex) 
            {
                // TODO - show the error to the user and log it
            }
            
            Loading = false;
            base.OnActivate();
        }

        public async Task RefreshChannels()
        {
            refreshTimer.Stop();
            var tasks = ChannelData.Where(x => !IsNullOrWhiteSpace(x.ChannelName))
                                   .Select(x => new { ChannelData = x, Stream = twitchTvClient.GetStreamDetails(x.ChannelName) })
                                   .ToList();

            try
            {
                await Task.WhenAll(tasks.Select(x => x.Stream));
                foreach (var task in tasks)
                {
                    var streamDetails = task.Stream.Result;
                    if (streamDetails == null) continue;

                    task.ChannelData.PopulateWithChannel(streamDetails.Channel);
                    task.ChannelData.Live = streamDetails.Viewers.HasValue;
                    task.ChannelData.Viewers = streamDetails.Viewers ?? 0;
                    if (streamDetails.CreatedAt != null)
                    {
                        task.ChannelData.StartTime = DateTimeOffset.Parse(streamDetails.CreatedAt);
                        task.ChannelData.NotifyOfPropertyChange(nameof(task.ChannelData.Uptime));
                    }
                }

                ViewSource.View.Refresh();
            }
            catch (Exception)
            {
                // TODO - do something with errors, log/report etc
            }

            refreshTimer.Start();
        }

        public void StartStream()
        {
            var selectedChannel = SelectedChannelData;
            if (selectedChannel == null || !selectedChannel.Live) return;
            // TODO - do a smarter find for the livestreamer exe and prompt on startup if it can not be found
            const string livestreamPath = @"C:\Program Files (x86)\Livestreamer\livestreamer.exe";
            const string livestreamerArgs = @"http://www.twitch.tv/{0}/ source";

            Process.Start(livestreamPath, livestreamerArgs.Replace("{0}", selectedChannel.ChannelName));
        }

        private void PopulateDesignData()
        {
            var rnd = new Random();

            for (int i = 0; i < 100; i++)
            {
                ChannelData.Add(new ChannelData()
                {
                    Live = i < 14,
                    ChannelName = $"Channel Name {i + 1}",
                    ChannelDescription = $"Channel Description {i + 1}",
                    Game = i < 50 ? "Game A" : "Game B",
                    StartTime = i < 14 ? DateTimeOffset.Now.AddSeconds(-(rnd.Next(10000))) : DateTimeOffset.MinValue,
                    Viewers = i < 14 ? rnd.Next(50000) : 0
                });
            }
        }
    }
}