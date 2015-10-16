using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model;
using Newtonsoft.Json;
using TwitchTv;
using static System.String;

namespace Livestream.Monitor.ViewModels
{
    public class ChannelListViewModel : Screen
    {
        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly IWindowManager windowManager;
        private bool loading;
        private readonly DispatcherTimer refreshTimer;
        private ChannelData selectedChannelData;

        public ChannelListViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            PopulateDesignData();
        }

        public ChannelListViewModel(
            ITwitchTvReadonlyClient twitchTvClient,
            IWindowManager windowManager)
        {
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));

            this.twitchTvClient = twitchTvClient;
            this.windowManager = windowManager;
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
                
                LoadChannels();

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
                var missingChannelData = new List<ChannelData>();

                await Task.WhenAll(tasks.Select(x => x.Stream));
                foreach (var task in tasks)
                {
                    var streamDetails = task.Stream.Result;
                    if (streamDetails == null)
                    {
                        missingChannelData.Add(task.ChannelData);
                        continue;
                    }

                    task.ChannelData.PopulateWithChannel(streamDetails.Channel);
                    task.ChannelData.PopulateWithStreamDetails(streamDetails);
                }

                // this causes the sort descriptions to be re-applied on the new data
                ViewSource.View.Refresh();

                // these dont need to be sorted because these streams were offline
                var offlineTasks = missingChannelData.Select(x => new
                                                            {
                                                                ChannelData = x,
                                                                OfflineData = twitchTvClient.GetChannelDetails(x.ChannelName)
                                                            })
                                                    .ToList();

                await Task.WhenAll(offlineTasks.Select(x => x.OfflineData));
                foreach (var offlineTask in offlineTasks)
                {
                    var offlineData = offlineTask.OfflineData.Result;
                    if (offlineData == null) continue;

                    offlineTask.ChannelData.PopulateWithChannel(offlineData);
                }
            }
            catch (Exception)
            {
                // TODO - do something with errors, log/report etc
            }

            refreshTimer.Start();
        }

        private MessageBoxViewModel messageBoxViewModel = new MessageBoxViewModel();

        public void StartStream()
        {
            var selectedChannel = SelectedChannelData;
            if (selectedChannel == null || !selectedChannel.Live) return;
            // TODO - do a smarter find for the livestreamer exe and prompt on startup if it can not be found
            const string livestreamPath = @"C:\Program Files (x86)\Livestreamer\livestreamer.exe";
            const string livestreamerArgs = @"http://www.twitch.tv/{0}/ source";

            messageBoxViewModel.DisplayName = $"Stream '{selectedChannel.ChannelName}'";
            messageBoxViewModel.MessageText = "Launching livestreamer...";
            var settings = new WindowSettingsBuilder().SizeToContent()
                                                      .WithWindowStyle(WindowStyle.ToolWindow)
                                                      .WithResizeMode(ResizeMode.NoResize)
                                                      .Create();

            windowManager.ShowWindow(messageBoxViewModel, null, settings);

            // the process needs to be launched from its own thread so it doesn't lockup the UI
            Task.Run(() =>
            {
                var proc = new Process
                {
                    StartInfo =
                    {
                        FileName = livestreamPath,
                        Arguments = livestreamerArgs.Replace("{0}", selectedChannel.ChannelName),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    },
                    EnableRaisingEvents = true
                };

                // see below for output handler
                proc.ErrorDataReceived += ProcOnErrorDataReceived;
                proc.OutputDataReceived += ProcOnOutputDataReceived;

                proc.Start();

                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();

                proc.WaitForExit();
                messageBoxViewModel.TryClose();
            });
        }

        private void ProcOnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                messageBoxViewModel.MessageText += Environment.NewLine + e.Data;
            }
        }

        private void ProcOnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                messageBoxViewModel.MessageText += Environment.NewLine + e.Data;
            }
        }

        // TODO - extract all saving/loading of channel information into its own model
        public void AddChannel(string channelName)
        {
            if (IsNullOrWhiteSpace(channelName)) return;

            // TODO - load stream and channel details
            //SaveChannelsToDisk();
        }

        // TODO - extract all saving/loading of channel information into its own model
        public async Task ImportFollowList(string username)
        {
            if (IsNullOrWhiteSpace(username)) return;

            // TODO - pass the username in here somehow
            var userFollows = await twitchTvClient.GetUserFollows(username);
            var channelData = userFollows.Follows.Select(x => x.Channel.ToChannelData());
            ChannelData.AddRange(channelData);
            SaveChannelsToDisk();
        }

        // TODO - extract all saving/loading of channel information into its own model
        private void SaveChannelsToDisk()
        {
            File.WriteAllText("channels.json", JsonConvert.SerializeObject(ChannelData.ToArray()));
        }

        // TODO - extract all saving/loading of channel information into its own model
        private void LoadChannels()
        {
            if (File.Exists("channels.json"))
            {
                try
                {
                    var channelFileData = JsonConvert.DeserializeObject<List<ChannelFileData>>(File.ReadAllText("channels.json"));
                    var channelData = channelFileData.Select(x => x.ToChannelData());
                    ChannelData.AddRange(channelData);
                }
                catch (Exception)
                {
                    // log and notify the user of this error
                }
            }
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