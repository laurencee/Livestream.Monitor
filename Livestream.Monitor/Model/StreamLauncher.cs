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

        public void StartStream(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null || !livestreamModel.Live) return;

            var livestreamPath = settingsHandler.Settings.LivestreamerFullPath;
            if (!File.Exists(livestreamPath))
            {
                var msgBox = new MessageBoxViewModel()
                {
                    DisplayName = "Livestreamer not found",
                    MessageText = $"Could not find livestreamer @ {livestreamPath}.{Environment.NewLine} Please download and install livestreamer from 'http://docs.livestreamer.io/install.html#windows-binaries'"
                };
                var settings = new WindowSettingsBuilder().SizeToContent()
                                                      .WithWindowStyle(WindowStyle.ToolWindow)
                                                      .WithResizeMode(ResizeMode.NoResize)
                                                      .Create();
                windowManager.ShowWindow(msgBox, null, settings);
                return;
            }

            // Fall back to source stream quality for non-partnered Livestreams
            var streamQuality = (!livestreamModel.IsPartner &&
                                 settingsHandler.Settings.DefaultStreamQuality != StreamQuality.Source)
                                    ? StreamQuality.Source
                                    : settingsHandler.Settings.DefaultStreamQuality;

            string livestreamerArgs = $"http://www.twitch.tv/{livestreamModel.DisplayName}/ {streamQuality}";
            var messageBoxViewModel = ShowStreamLoadMessageBox(livestreamModel, settingsHandler.Settings.DefaultStreamQuality);

            // the process needs to be launched from its own thread so it doesn't lockup the UI
            Task.Run(() =>
            {
                var proc = new Process
                {
                    StartInfo =
                    {
                        FileName = livestreamPath,
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

        private MessageBoxViewModel ShowStreamLoadMessageBox(LivestreamModel selectedLivestream, StreamQuality streamQuality)
        {
            var messageBoxViewModel = new MessageBoxViewModel
            {
                DisplayName = $"Stream '{selectedLivestream.DisplayName}'",
                MessageText = "Launching livestreamer..."
            };

            // Notify the user if the quality has been swapped back to source due to the livestream not being partenered (twitch specific).
            if (!selectedLivestream.IsPartner && streamQuality != StreamQuality.Source)
            {
                messageBoxViewModel.MessageText += Environment.NewLine + "[NOTE] Channel is not a twitch partner so falling back to Source quality";
            }

            var settings = new WindowSettingsBuilder().SizeToContent()
                                                      .WithWindowStyle(WindowStyle.ToolWindow)
                                                      .WithResizeMode(ResizeMode.NoResize)
                                                      .Create();

            windowManager.ShowWindow(messageBoxViewModel, null, settings);
            return messageBoxViewModel;
        }
    }
}
