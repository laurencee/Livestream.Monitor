using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.ViewModels;
using Action = System.Action;

namespace Livestream.Monitor.Model
{
    public class StreamLauncher
    {
        private static readonly object watchingStreamsLock = new object();

        private readonly ISettingsHandler settingsHandler;
        private readonly IWindowManager windowManager;
        private readonly List<LivestreamModel> watchingStreams = new List<LivestreamModel>();

        public StreamLauncher(ISettingsHandler settingsHandler, IWindowManager windowManager)
        {
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));

            this.settingsHandler = settingsHandler;
            this.windowManager = windowManager;
        }

        public List<LivestreamModel> WatchingStreams
        {
            get
            {
                lock (watchingStreamsLock)
                {
                    // return a copy of the streams to prevent modification issues
                    return watchingStreams.ToList();
                }
            }
        }

        public async Task OpenChat(LivestreamModel livestreamModel, IViewAware fromScreen)
        {
            // guard against invalid/missing chrome path
            var chromeLocation = settingsHandler.Settings.ChromeFullPath;
            if (string.IsNullOrWhiteSpace(chromeLocation))
            {
                await fromScreen.ShowMessageAsync("No chrome locations specified",
                    $"Chrome location is not set in settings.{Environment.NewLine}Chat relies on chrome to function.");
                return;
            }
            if (!File.Exists(chromeLocation))
            {
                await fromScreen.ShowMessageAsync("Chrome not found",
                    $"Could not find chrome @ {chromeLocation}.{Environment.NewLine}Chat relies on chrome to function.");
                return;
            }
            // guard against stream provider not having chat support
            if (!livestreamModel.ApiClient.HasChatSupport)
            {
                await fromScreen.ShowMessageAsync("Chat not supported",
                    $"No external chat support for stream provider '{livestreamModel.ApiClient.ApiName}'");
                return;
            }
            
            string chromeArgs = $"--app={livestreamModel.ChatUrl} --window-size=350,758";

            await Task.Run(async () =>
            {
                try
                {
                    var proc = new Process()
                    {
                        StartInfo =
                        {
                            FileName = chromeLocation,
                            Arguments = chromeArgs,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        }
                    };

                    proc.Start();
                }
                catch (Exception ex)
                {
                    await fromScreen.ShowMessageAsync("Error launching chat", ex.Message);
                }
            });
        }

        public void OpenStream(LivestreamModel livestreamModel, string streamQuality)
        {
            if (livestreamModel?.ApiClient == null || !livestreamModel.Live) return;
            
            string livestreamerArgs = $"{livestreamModel.StreamUrl} {streamQuality}";

            // hack to pass through the client id to livestreamer 
            if (settingsHandler.Settings.PassthroughClientId && livestreamModel.ApiClient is TwitchApiClient)
            {
                livestreamerArgs = $"--http-header {ExternalAPIs.TwitchTv.RequestConstants.ClientIdHeaderKey}={ExternalAPIs.TwitchTv.RequestConstants.ClientIdHeaderValue} {livestreamerArgs}";
            }

            var messageBoxViewModel = ShowLivestreamerLoadMessageBox(
                title: $"Stream '{livestreamModel.DisplayName}'",
                messageText: $"Launching livestreamer....{Environment.NewLine}'livestreamer.exe {livestreamerArgs}'");

            // Notify the user if the quality has been swapped back to source due to the livestream not being partenered (twitch specific).
            if (!livestreamModel.IsPartner && streamQuality != StreamQuality.Best.ToString())
            {
                messageBoxViewModel.MessageText += Environment.NewLine + $"[NOTE] Channel is not a twitch partner so falling back to {StreamQuality.Best} quality";
            }
            
            lock (watchingStreamsLock)
            {
                watchingStreams.Add(livestreamModel);
            }

            StartLivestreamer(livestreamerArgs, streamQuality, messageBoxViewModel, onClose: () =>
            {
                lock (watchingStreamsLock)
                {
                    watchingStreams.Remove(livestreamModel);
                }
            });
        }

        public void OpenVod(VodDetails vodDetails)
        {
            if (string.IsNullOrWhiteSpace(vodDetails.Url) || !Uri.IsWellFormedUriString(vodDetails.Url, UriKind.Absolute)) return;

            string livestreamerArgs = $"--player-passthrough hls {vodDetails.Url} best";
            const int maxTitleLength = 70;
            var title = vodDetails.Title?.Length > maxTitleLength ? vodDetails.Title.Substring(0, maxTitleLength) + "..." : vodDetails.Title;

            var messageBoxViewModel = ShowLivestreamerLoadMessageBox(
                title: title,
                messageText: $"Launching livestreamer....{Environment.NewLine}'livestreamer.exe {livestreamerArgs}'");

            StartLivestreamer(livestreamerArgs, "best", messageBoxViewModel);
        }

        private void StartLivestreamer(string livestreamerArgs, string streamQuality, MessageBoxViewModel messageBoxViewModel, Action onClose = null)
        {
            if (!CheckLivestreamerExists()) return;

            // the process needs to be launched from its own thread so it doesn't lockup the UI
            Task.Run(() =>
            {
                var proc = new Process
                {
                    StartInfo =
                    {
                        FileName = settingsHandler.Settings.LivestreamerFullPath,
                        Arguments = livestreamerArgs,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    },
                    EnableRaisingEvents = true
                };

                bool preventClose = false;

                // see below for output handler
                proc.ErrorDataReceived +=
                    (sender, args) =>
                    {
                        if (args.Data == null) return;

                        preventClose = true;
                        messageBoxViewModel.MessageText += Environment.NewLine + args.Data;
                    };
                proc.OutputDataReceived +=
                    (sender, args) =>
                    {
                        if (args.Data == null) return;
                        if (args.Data.StartsWith("[cli][info] Starting player") && settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad)
                        {
                            messageBoxViewModel.TryClose();
                            // can continue adding messages, the view model still exists so it doesn't really matter
                        }

                        messageBoxViewModel.MessageText += Environment.NewLine + args.Data;
                    };

                try
                {
                    proc.Start();

                    proc.BeginErrorReadLine();
                    proc.BeginOutputReadLine();

                    proc.WaitForExit();
                    if (proc.ExitCode != 0)
                    {
                        preventClose = true;
                    }

                    onClose?.Invoke();
                }
                catch (Exception ex)
                {
                    preventClose = true;
                    messageBoxViewModel.MessageText += Environment.NewLine + ex.ToString();
                }

                if (preventClose)
                {                    
                    messageBoxViewModel.MessageText += Environment.NewLine + Environment.NewLine +
                                                       "ERROR occured in Livestreamer: Manually close this window when you've finished reading the livestreamer output.";

                    // open the message box if it was somehow closed prior to the error being displayed
                    if (!messageBoxViewModel.IsActive) windowManager.ShowWindow(messageBoxViewModel, null, new WindowSettingsBuilder().SizeToContent().NoResizeBorderless().Create());
                }
                else if (string.IsNullOrEmpty(streamQuality))
                {
                    messageBoxViewModel.MessageText += Environment.NewLine + Environment.NewLine +
                                                       "No stream quality provided: Manually close this window when you've finished reading the livestreamer output.";

                    if (!messageBoxViewModel.IsActive) windowManager.ShowWindow(messageBoxViewModel, null, new WindowSettingsBuilder().SizeToContent().NoResizeBorderless().Create());
                }
                else
                    messageBoxViewModel.TryClose();
            });
        }

        private MessageBoxViewModel ShowLivestreamerLoadMessageBox(string title, string messageText)
        {
            var messageBoxViewModel = new MessageBoxViewModel
            {
                DisplayName = title,
                MessageText = messageText,
            };
            messageBoxViewModel.ShowHideOnLoadCheckbox(settingsHandler);

            var settings = new WindowSettingsBuilder().SizeToContent()
                                                      .NoResizeBorderless()
                                                      .Create();

            windowManager.ShowWindow(messageBoxViewModel, null, settings);
            return messageBoxViewModel;
        }

        private bool CheckLivestreamerExists()
        {
            if (File.Exists(settingsHandler.Settings.LivestreamerFullPath)) return true;

            var msgBox = new MessageBoxViewModel()
            {
                DisplayName = "Livestreamer not found",
                MessageText =
                    $"Could not find livestreamer @ {settingsHandler.Settings.LivestreamerFullPath}.{Environment.NewLine} Please download and install livestreamer from 'http://docs.livestreamer.io/install.html#windows-binaries'"
            };

            var settings = new WindowSettingsBuilder().SizeToContent()
                                                      .NoResizeBorderless()
                                                      .Create();

            windowManager.ShowWindow(msgBox, null, settings);
            return false;
        }
    }
}
