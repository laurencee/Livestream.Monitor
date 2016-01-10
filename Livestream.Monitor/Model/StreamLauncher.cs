using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.ViewModels;

namespace Livestream.Monitor.Model
{
    public class StreamLauncher
    {
        private readonly ISettingsHandler settingsHandler;
        private readonly IWindowManager windowManager;

        public StreamLauncher(ISettingsHandler settingsHandler, IWindowManager windowManager)
        {
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));
            
            this.settingsHandler = settingsHandler;
            this.windowManager = windowManager;
        }

        public async Task OpenChat(LivestreamModel livestreamModel, IViewAware fromScreen)
        {
            if (livestreamModel == null) return;

            var chromeLocation = settingsHandler.Settings.ChromeFullPath;
            if (!File.Exists(chromeLocation))
            {
                await fromScreen.ShowMessageAsync("Chrome not found",
                    $"Could not find chrome @ {chromeLocation}.{Environment.NewLine} The chat function relies on chrome to function.");
                return;
            }

            string chromeArgs = $"--app=http://www.twitch.tv/{livestreamModel.DisplayName}/chat?popout=true --window-size=350,758";

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

        public void OpenStream(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null || !livestreamModel.Live) return;

            // Fall back to source stream quality for non-partnered Livestreams
            var streamQuality = (!livestreamModel.IsPartner &&
                                 settingsHandler.Settings.DefaultStreamQuality != StreamQuality.Source)
                                    ? StreamQuality.Source
                                    : settingsHandler.Settings.DefaultStreamQuality;

            string livestreamerArgs = $"http://www.twitch.tv/{livestreamModel.DisplayName}/ {streamQuality}";
            var messageBoxViewModel = ShowLivestreamerLoadMessageBox(
                title: $"Stream '{livestreamModel.DisplayName}'", 
                messageText: "Launching livestreamer...");

            // Notify the user if the quality has been swapped back to source due to the livestream not being partenered (twitch specific).
            if (!livestreamModel.IsPartner && streamQuality != StreamQuality.Source)
            {
                messageBoxViewModel.MessageText += Environment.NewLine + "[NOTE] Channel is not a twitch partner so falling back to Source quality";
            }

            StartLivestreamer(livestreamerArgs, messageBoxViewModel);
        }

        public void OpenVod(VodDetails vodDetails)
        {
            if (string.IsNullOrWhiteSpace(vodDetails.Url) || !Uri.IsWellFormedUriString(vodDetails.Url, UriKind.Absolute)) return;

            string livestreamerArgs = $"--player-passthrough hls {vodDetails.Url} best";
            const int maxTitleLength = 70;
            var title = vodDetails.Title?.Length > maxTitleLength ? vodDetails.Title.Substring(0, maxTitleLength) + "..." : vodDetails.Title;

            var messageBoxViewModel = ShowLivestreamerLoadMessageBox(
                title: title,
                messageText: "Launching livestreamer....");

            StartLivestreamer(livestreamerArgs, messageBoxViewModel);
        }

        private void StartLivestreamer(string livestreamerArgs, MessageBoxViewModel messageBoxViewModel)
        {
            if (!CheckLivestreamerExists()) return;

            // the process needs to be launched from its own thread so it doesn't lockup the UI
            Task.Run(() =>
            {
                // work around an issue where focus isn't returned to the main window on the messagebox window closing (even though the owner is set correctly)
                messageBoxViewModel.Deactivated += (sender, args) =>
                {
                    var mainWindow = Application.Current.MainWindow;
                    if (mainWindow != null && mainWindow.IsVisible)
                    {
                        mainWindow.Activate();
                    }
                };

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
                        if (args.Data != null)
                        {
                            preventClose = true;
                            messageBoxViewModel.MessageText += Environment.NewLine + args.Data;
                        }
                    };
                proc.OutputDataReceived +=
                    (sender, args) =>
                    {
                        if (args.Data != null) messageBoxViewModel.MessageText += Environment.NewLine + args.Data;
                    };

                try
                {
                    proc.Start();

                    proc.BeginErrorReadLine();
                    proc.BeginOutputReadLine();

                    proc.WaitForExit();
                    if (proc.ExitCode != 0) preventClose = true;
                }
                catch (Exception)
                {
                    // TODO log errors opening stream
                }

                if (preventClose)
                {
                    messageBoxViewModel.MessageText += Environment.NewLine + Environment.NewLine +
                                                       "ERROR occured in Livestreamer: Manually close this window when you've finished reading the livestreamer output.";
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
                MessageText = messageText
            };
            
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
