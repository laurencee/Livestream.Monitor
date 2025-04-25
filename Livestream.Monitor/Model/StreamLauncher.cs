using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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

            var chatUrl = await livestreamModel.GetChatUrl;
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

            var streamUrl = await livestreamModel.GetStreamUrl;
            var messageBoxViewModel = CreateAppLoadMessageBox(
                title: $"Stream '{livestreamModel.DisplayName}'",
                initialMessageText: $"Launching {livestreamModel.ApiClient.ApiName} stream {livestreamModel.DisplayName}...");
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
            var title = vodDetails.Title?.Length > maxTitleLength ? vodDetails.Title.Substring(0, maxTitleLength) + "..." : vodDetails.Title;

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
            messageBoxViewModel.MessageText += $"{Environment.NewLine}{execCommand.FilePath} {execCommand.Args}";

            var finalArgs = execCommand.Args;
            foreach (var replacement in replacements)
            {
                finalArgs = finalArgs.Replace(replacement.Key, replacement.Value);
            }

            finalArgs = finalArgs.Trim();

            if (settingsHandler.Settings.DebugMode)
                messageBoxViewModel.MessageText += $"{Environment.NewLine}[DEBUG] {execCommand.FilePath} {finalArgs}";

            var filename = execCommand.FilePath.Trim('"');
            if (!IsValidFilePath(filename))
            {
                messageBoxViewModel.MessageText += $"{Environment.NewLine}[ERROR] Invalid FileName: {execCommand.FilePath}";
                if (!messageBoxViewModel.IsActive) ShowMessageBox(messageBoxViewModel);
                return;
            }

            if (!Path.IsPathRooted(filename))
            {
                string fullPath = Shell.ResolveExecutableFromRegistry(filename);

                // the exe might be on the PATH env var and in that case
                // we don't need to change anything as it'll automatically resolve from process.start
                if (fullPath != null)
                    filename = fullPath;
            }

            // the process needs to be launched from its own thread so it doesn't lockup the UI
            Task.Run(() =>
            {
                var proc = new Process
                {
                    StartInfo =
                    {
                        FileName = filename,
                        Arguments = finalArgs,
                        RedirectStandardOutput = execCommand.CaptureStandardOutput,
                        RedirectStandardError = execCommand.CaptureErrorOutput,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                    },
                    EnableRaisingEvents = true,
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

                    if (execCommand.CaptureErrorOutput) proc.BeginErrorReadLine();
                    if (execCommand.CaptureStandardOutput) proc.BeginOutputReadLine();

                    if (!execCommand.CaptureStandardOutput && !execCommand.CaptureErrorOutput)
                    {
                        messageBoxViewModel.TryClose();
                        onClose?.Invoke();
                    }

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
                                                       "ERROR: Manually close this window when you've finished reading output.";

                    // open the message box if it was somehow closed prior to the error being displayed
                    if (!messageBoxViewModel.IsActive) ShowMessageBox(messageBoxViewModel);
                }
                else
                    messageBoxViewModel.TryClose();
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
            windowManager.ShowWindow(messageBox, null, MessageBoxWindowsSettings);

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
                if (input.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    return false;

                string fileName = Path.GetFileName(input);
                if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return false;

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

// enums (only the members we need)
[Flags]
enum AssocF : uint
{
    OPEN_BYEXENAME = 0x2,   // identical to INIT_BYEXENAME
    NOTRUNCATE     = 0x20,
}

enum AssocStr : uint
{
    EXECUTABLE = 2
}

// ReSharper disable once InconsistentNaming
public enum HRESULT
{
    S_OK = 0x00000000,
    S_FALSE = 0x00000001,
    E_POINTER = unchecked((int)0x80004003),
    ERROR_INSUFFICIENT_BUFFER = unchecked((int)0x8007007A),
    ERROR_FILE_NOT_FOUND = unchecked((int)0x80070002),
    ERROR_PATH_NOT_FOUND = unchecked((int)0x80070003),
    ERROR_NO_ASSOCIATION = unchecked((int)0x80070483),
}

static class Shell
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern HRESULT AssocQueryString(
        AssocF flags,
        AssocStr str,
        string pszAssoc,
        string pszExtra,
        [Out] StringBuilder pszOut,
        ref uint pcchOut);

    /// <summary> Resolves an executable path from "App Path" registrations </summary>
    /// <remarks>
    /// See the registry details from
    /// https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-assocquerystringa
    /// https://learn.microsoft.com/en-us/windows/win32/shell/app-registration#finding-an-application-executable
    /// </remarks>
    public static string ResolveExecutableFromRegistry(string alias)
    {
        if (!alias.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            alias += ".exe";

        uint len = 0;
        // First call gets the required buffer size.
        var hr = AssocQueryString(AssocF.OPEN_BYEXENAME, AssocStr.EXECUTABLE,
            alias, null, null, ref len);

        if (hr != HRESULT.S_FALSE && hr != HRESULT.ERROR_INSUFFICIENT_BUFFER)
            return null;

        var sb = new StringBuilder((int)len);
        hr = AssocQueryString(AssocF.OPEN_BYEXENAME | AssocF.NOTRUNCATE,
            AssocStr.EXECUTABLE, alias, null, sb, ref len);

        return hr == HRESULT.S_OK ? sb.ToString() : null;
    }
}
