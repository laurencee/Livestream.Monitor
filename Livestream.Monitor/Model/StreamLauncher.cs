using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs.TwitchTv.V3;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.ViewModels;
using Action = System.Action;

namespace Livestream.Monitor.Model
{
    public class StreamLauncher
    {
        private static readonly object WatchingStreamsLock = new object();

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
                lock (WatchingStreamsLock)
                {
                    // return a copy of the streams to prevent modification issues
                    return watchingStreams.ToList();
                }
            }
        }

        public async Task OpenChat(LivestreamModel livestreamModel, IViewAware fromScreen)
        {
            // guard against stream provider with unknown chat support
            if (!livestreamModel.ApiClient.HasChatSupport)
            {
                await fromScreen.ShowMessageAsync("Chat not supported",
                    $"No external chat support for stream provider '{livestreamModel.ApiClient.ApiName}'");
                return;
            }

            if (string.IsNullOrWhiteSpace(settingsHandler.Settings.ChatCommandLine))
            {
                await fromScreen.ShowMessageAsync("No chat command specified", "Chat command is not set in settings.");
                return;
            }

            // guard against chat command that contains no url token
            if (!settingsHandler.Settings.ChatCommandLine.Contains(Settings.CHAT_URL_REPLACEMENT_TOKEN))
            {
                await fromScreen.ShowMessageAsync("Missing url token in chat command",
                    $"Chat command is missing the url token {Settings.CHAT_URL_REPLACEMENT_TOKEN}.");
                return;
            }

            var command = settingsHandler.Settings.ChatCommandLine.Replace(Settings.CHAT_URL_REPLACEMENT_TOKEN, livestreamModel.ChatUrl);

            await Task.Run(async () =>
            {
                try
                {
                    var proc = new Process
                    {
                        StartInfo =
                        {
                            FileName = "cmd.exe",
                            Arguments = "/c " + command,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        }
                    };

                    proc.Start();
                    string errorOutput = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    // check for exit code as well because sometimes processes emit warnings in the error output stream
                    if (proc.ExitCode != 0 && errorOutput != string.Empty)
                    {
                        await Execute.OnUIThreadAsync(async () => await fromScreen.ShowMessageAsync("Error launching chat", errorOutput));
                    }
                }
                catch (Exception ex)
                {
                    await Execute.OnUIThreadAsync(async () => await fromScreen.ShowMessageAsync("Error launching chat", ex.Message));
                }
            });
        }

        public async Task OpenStream(LivestreamModel livestreamModel, IViewAware viewAware)
        {
            if (livestreamModel?.ApiClient == null || !livestreamModel.Live) return;

            var favoriteQualities = settingsHandler.Settings.GetStreamQualities(livestreamModel.ApiClient.ApiName);
            var qualities = favoriteQualities.Qualities.Union(new[] { favoriteQualities.FallbackQuality });

            string livestreamerArgs = $"{livestreamModel.StreamUrl} {string.Join(",", qualities)}";
            var apiClient = livestreamModel.ApiClient;

            // hack to pass through the client id to livestreamer
            if (settingsHandler.Settings.PassthroughClientId)
            {
                if (apiClient is TwitchApiClient)
                {
                    livestreamerArgs = $"--http-header {RequestConstants.ClientIdHeaderKey}={RequestConstants.ClientIdHeaderValue} {livestreamerArgs}";
                }
            }
            else
            {
                if (!apiClient.IsAuthorized)
                {
                    await apiClient.Authorize(viewAware);
                }

                if (apiClient.IsAuthorized && !string.IsNullOrWhiteSpace(apiClient.LivestreamerAuthorizationArg))
                {
                    livestreamerArgs += " " + apiClient.LivestreamerAuthorizationArg;
                }
            }

            var launcher = Path.GetFileName(settingsHandler.Settings.LivestreamerFullPath);

            var messageBoxViewModel = ShowLivestreamerLoadMessageBox(
                title: $"Stream '{livestreamModel.DisplayName}'",
                messageText: $"Launching {settingsHandler.Settings.LivestreamExeDisplayName}....{Environment.NewLine}'{launcher} {livestreamerArgs}'");

            lock (WatchingStreamsLock)
            {
                watchingStreams.Add(livestreamModel);
            }

            StartLivestreamer(livestreamerArgs, messageBoxViewModel, onClose: () =>
            {
                lock (WatchingStreamsLock)
                {
                    watchingStreams.Remove(livestreamModel);
                }
            });
        }

        public async Task OpenVod(VodDetails vodDetails, IViewAware viewAware)
        {
            if (string.IsNullOrWhiteSpace(vodDetails.Url) ||
                !Uri.IsWellFormedUriString(vodDetails.Url, UriKind.Absolute)) return;

            var favoriteQualities = settingsHandler.Settings.GetStreamQualities(vodDetails.ApiClient.ApiName);
            var qualities = favoriteQualities.Qualities.Union(new[] { favoriteQualities.FallbackQuality });

            string livestreamerArgs = $"--player-passthrough hls {vodDetails.Url} {string.Join(",", qualities)}";

            // hack to pass through the client id to livestreamer
            if (settingsHandler.Settings.PassthroughClientId)
            {
                // check domain name of url to see if this is a twitch.tv vod link
                bool isTwitchStream = false;
                try
                {
                    var uri = new Uri(vodDetails.Url);
                    isTwitchStream = uri.Host.Contains("twitch.tv");
                }
                catch { }

                if (isTwitchStream)
                {
                    livestreamerArgs += $" --http-header {RequestConstants.ClientIdHeaderKey}={RequestConstants.ClientIdHeaderValue}";
                }
            }
            else
            {
                var apiClient = vodDetails.ApiClient;
                if (!apiClient.IsAuthorized)
                {
                    await apiClient.Authorize(viewAware);
                }

                if (apiClient.IsAuthorized && !string.IsNullOrWhiteSpace(apiClient.LivestreamerAuthorizationArg))
                {
                    livestreamerArgs += " " + apiClient.LivestreamerAuthorizationArg;
                }
            }

            const int maxTitleLength = 70;
            var title = vodDetails.Title?.Length > maxTitleLength ? vodDetails.Title.Substring(0, maxTitleLength) + "..." : vodDetails.Title;

            var launcher = Path.GetFileName(settingsHandler.Settings.LivestreamerFullPath);

            var messageBoxViewModel = ShowLivestreamerLoadMessageBox(
                title: title,
                messageText: $"Launching {settingsHandler.Settings.LivestreamExeDisplayName}....{Environment.NewLine}'{launcher} {livestreamerArgs}'");

            StartLivestreamer(livestreamerArgs, messageBoxViewModel);
        }

        private void StartLivestreamer(string livestreamerArgs, MessageBoxViewModel messageBoxViewModel, Action onClose = null)
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
                        if (args.Data.Contains("Starting player") &&
                            settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad)
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
                    messageBoxViewModel.MessageText += Environment.NewLine + ex;
                }

                if (preventClose)
                {
                    messageBoxViewModel.MessageText += Environment.NewLine + Environment.NewLine +
                                                       $"ERROR occured in {settingsHandler.Settings.LivestreamExeDisplayName}: " +
                                                       $"Manually close this window when you've finished reading the {settingsHandler.Settings.LivestreamExeDisplayName} output.";

                    // open the message box if it was somehow closed prior to the error being displayed
                    if (!messageBoxViewModel.IsActive)
                        windowManager.ShowWindow(messageBoxViewModel, null, new WindowSettingsBuilder().SizeToContent().NoResizeBorderless().Create());
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

            var msgBox = new MessageBoxViewModel
            {
                DisplayName = "Livestreamer/Streamlink not found",
                MessageText =
                    $"Could not find livestreamer/streamlink @ '{settingsHandler.Settings.LivestreamerFullPath}'" + Environment.NewLine +
                    "Please download and install streamlink from 'https://streamlink.github.io/install.html#windows-binaries'" + Environment.NewLine +
                    "OR download and install livestreamer from 'http://docs.livestreamer.io/install.html#windows-binaries'"
            };

            var settings = new WindowSettingsBuilder().SizeToContent()
                                                      .NoResizeBorderless()
                                                      .Create();

            windowManager.ShowWindow(msgBox, null, settings);
            return false;
        }
    }
}
