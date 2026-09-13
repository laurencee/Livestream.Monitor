using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.ViewModels;
using Action = System.Action;

namespace Livestream.Monitor.Model
{
    public class StreamLauncher(ISettingsHandler settingsHandler, IWindowManager windowManager)
    {
        private static readonly object WatchingStreamsLock = new();
        private static readonly ExpandoObject MessageBoxWindowsSettings =
            new WindowSettingsBuilder().SizeToContent().NoResizeBorderless().Create();

        private readonly ISettingsHandler settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
        private readonly IWindowManager windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        private readonly List<LivestreamModel> watchingStreams = [];

        internal IProcessWindowSource ProcessWindows { get; set; } = new WindowsProcessWindowSource();

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
            if (!livestreamModel.ApiClient.HasChatSupport)
            {
                await fromScreen.ShowMessageAsync("Chat not supported",
                    $"No external chat support for stream provider '{livestreamModel.ApiClient.ApiName}'");
                return;
            }

            string chatUrl;
            try
            {
                chatUrl = await livestreamModel.GetChatUrl();
            }
            catch (Exception ex)
            {
                await fromScreen.ShowMessageAsync("Error opening chat",
                    $"An error occurred attempting to open the chat: {ex.ExtractErrorMessage()}");
                return;
            }

            var messageBoxViewModel = CreateAppLoadMessageBox(
                title: $"Chat '{livestreamModel.DisplayName}'",
                initialMessageText: $"Launching chat for {livestreamModel.DisplayName}...");

            var platformSettings = settingsHandler.Settings.GetPlatformSettings(livestreamModel.ApiClient.ApiName);
            var replacements = platformSettings.GetReplacements(chatUrl);

            LaunchApp(messageBoxViewModel, platformSettings.ChatCommand, replacements);
        }

        public async Task OpenStream(LivestreamModel livestreamModel, IViewAware fromScreen)
        {
            if (livestreamModel?.ApiClient == null || !livestreamModel.Live) return;

            string streamUrl;
            try
            {
                streamUrl = await livestreamModel.GetStreamUrl();
            }
            catch (Exception ex)
            {
                await fromScreen.ShowMessageAsync("Error opening stream",
                    $"An error occurred attempting to open the stream: {ex.ExtractErrorMessage()}");
                return;
            }

            var initialMessage = $"Launching {livestreamModel.ApiClient.ApiName} stream {livestreamModel.DisplayName}...";
            var messageBoxViewModel = CreateAppLoadMessageBox(
                title: $"Stream '{livestreamModel.DisplayName}'",
                initialMessageText: initialMessage);
            ShowMessageBox(messageBoxViewModel);

            var platformSettings = settingsHandler.Settings.GetPlatformSettings(livestreamModel.ApiClient.ApiName);
            var replacements = GetReplacementsWithQualities(livestreamModel.ApiClient, platformSettings, streamUrl);

            lock (WatchingStreamsLock)
            {
                watchingStreams.Add(livestreamModel);
            }

            LaunchApp(messageBoxViewModel, platformSettings.StreamCommand, replacements, () =>
            {
                lock (WatchingStreamsLock)
                {
                    watchingStreams.Remove(livestreamModel);
                }
            });
        }

        public async Task OpenVod(VodDetails vodDetails, IViewAware fromScreen)
        {
            if (string.IsNullOrWhiteSpace(vodDetails.Url) ||
                !Uri.IsWellFormedUriString(vodDetails.Url, UriKind.Absolute))
            {
                await fromScreen.ShowMessageAsync("VOD Url invalid", $"Invalid VOD url: '{vodDetails.Url}'");
                return;
            }

            const int maxTitleLength = 70;
            var title = vodDetails.Title?.Length > maxTitleLength
                ? vodDetails.Title.Substring(0, maxTitleLength) + "..."
                : vodDetails.Title;

            var messageBoxViewModel = CreateAppLoadMessageBox(
                title: title,
                initialMessageText: $"Launching VOD: {title}...");
            ShowMessageBox(messageBoxViewModel);

            var platformSettings = settingsHandler.Settings.GetPlatformSettings(vodDetails.ApiClient.ApiName);
            var replacements = GetReplacementsWithQualities(vodDetails.ApiClient, platformSettings, vodDetails.Url);

            LaunchApp(messageBoxViewModel, platformSettings.VodCommand, replacements);
        }

        public void LaunchApp(
            MessageBoxViewModel messageBoxViewModel,
            ExecCommand execCommand,
            IReadOnlyDictionary<string, string> replacements,
            Action onClose = null)
        {
            AppendOutput(messageBoxViewModel, $"{execCommand.FilePath} {execCommand.Args}");

            var finalArgs = execCommand.Args;
            foreach (var replacement in replacements)
            {
                finalArgs = finalArgs.Replace(replacement.Key, replacement.Value);
            }

            finalArgs = finalArgs.Trim();

            if (settingsHandler.Settings.DebugMode)
                AppendOutput(messageBoxViewModel, $"[DEBUG] {execCommand.FilePath} {finalArgs}");

            var filename = execCommand.FilePath.Trim('"');
            if (!IsValidFilePath(filename))
            {
                AppendOutput(messageBoxViewModel, $"[ERROR] Invalid FileName: {execCommand.FilePath}");
                OnUIThread(() =>
                {
                    if (!messageBoxViewModel.IsActive) ShowMessageBox(messageBoxViewModel);
                });

                return;
            }

            if (WindowsCommandResolver.TryResolveExecutable(filename, out var resolvedPath))
                filename = resolvedPath;

            // Process exit and redirected-stream draining must not block the dispatcher.
            Task.Run(async () =>
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = filename,
                    Arguments = finalArgs,
                    RedirectStandardOutput = execCommand.CaptureStandardOutput,
                    RedirectStandardError = execCommand.CaptureErrorOutput,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                proc.EnableRaisingEvents = true;

                var failed = false;

                using var observationCancellation = new CancellationTokenSource();
                var observation = Task.CompletedTask;

                proc.ErrorDataReceived += (_, args) => AppendOutput(messageBoxViewModel, args.Data);
                proc.OutputDataReceived += (_, args) => AppendOutput(messageBoxViewModel, args.Data);

                try
                {
                    proc.Start();

                    if (execCommand.CaptureErrorOutput) proc.BeginErrorReadLine();
                    if (execCommand.CaptureStandardOutput) proc.BeginOutputReadLine();

                    if (!execCommand.CaptureStandardOutput && !execCommand.CaptureErrorOutput)
                    {
                        OnUIThread(() => messageBoxViewModel.TryClose());
                        onClose?.Invoke();
                    }
                    else
                    {
                        observation = ObserveWindow(proc, messageBoxViewModel, observationCancellation.Token);
                    }

                    proc.WaitForExit();
                    if (proc.ExitCode != 0)
                    {
                        failed = true;
                        var exitCode = proc.ExitCode.ToString(CultureInfo.InvariantCulture);
                        AppendOutput(messageBoxViewModel, $"ERROR: Process exited with code {exitCode}.");
                    }

                    onClose?.Invoke();
                }
                catch (Exception ex)
                {
                    failed = true;
                    AppendOutput(messageBoxViewModel, ex.ToString());
                }
                finally
                {
                    observationCancellation.Cancel();
                    // Join the observer before final UI state, so readiness cannot hide a failure window.
                    await observation.ConfigureAwait(false);
                }

                OnUIThread(() =>
                {
                    if (failed)
                    {
                        messageBoxViewModel.MessageText += Environment.NewLine + Environment.NewLine +
                            "ERROR: Manually close this window when you've finished reading output.";
                        if (!messageBoxViewModel.IsActive) ShowMessageBox(messageBoxViewModel);
                    }
                    else
                    {
                        messageBoxViewModel.TryClose();
                    }
                });
            });
        }

        private async Task ObserveWindow(
            Process process,
            MessageBoxViewModel output,
            CancellationToken cancellationToken)
        {
            long creationTime;
            try
            {
                creationTime = process.StartTime.ToUniversalTime().ToFileTimeUtc();
            }
            catch (InvalidOperationException)
            {
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return;
            }

            var observer = new ProcessWindowObserver(ProcessWindows, process.Id, creationTime);
            var dispatcher = Application.Current?.Dispatcher;
            var found = await observer
                .WaitForWindow(cancellationToken, () => dispatcher?.HasShutdownStarted == true)
                .ConfigureAwait(false);
            if (!found) return;

            OnUIThread(() =>
            {
                if (!cancellationToken.IsCancellationRequested &&
                    settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad)
                {
                    output.TryClose();
                }
            });
        }

        private static void AppendOutput(MessageBoxViewModel output, string text)
        {
            if (text == null) return;

            OnUIThread(() => output.MessageText += Environment.NewLine + text);
        }

        private static void OnUIThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher?.HasShutdownStarted == true) return;

            Execute.OnUIThread(() =>
            {
                if (dispatcher?.HasShutdownStarted != true) action();
            });
        }

        private MessageBoxViewModel CreateAppLoadMessageBox(string title, string initialMessageText)
        {
            var messageBoxViewModel = new MessageBoxViewModel
            {
                DisplayName = title,
                MessageText = initialMessageText,
            };
            messageBoxViewModel.InitSettingsHandler(settingsHandler);
            return messageBoxViewModel;
        }

        private void ShowMessageBox(MessageBoxViewModel messageBox) =>
            OnUIThread(() => windowManager.ShowWindow(messageBox, null, MessageBoxWindowsSettings));

        private Dictionary<string, string> GetReplacementsWithQualities(
            IApiClient apiClient,
            ApiPlatformSettings platformSettings,
            string url)
        {
            var favoriteQualities = settingsHandler.Settings.GetStreamQualities(apiClient.ApiName);
            var qualities = favoriteQualities.Qualities.Union([favoriteQualities.FallbackQuality]);

            var replacements = platformSettings.GetReplacements(url).ToDictionary(x => x.Key, x => x.Value);
            replacements["{qualities}"] = string.Join(",", qualities);
            return replacements;
        }

        private static bool IsValidFilePath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            try
            {
                if (input.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;

                string fileName = Path.GetFileName(input);
                if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;

                // throws on malformed paths, can catch some stuff the previous stuff wont
                _ = Path.GetFullPath(input);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
