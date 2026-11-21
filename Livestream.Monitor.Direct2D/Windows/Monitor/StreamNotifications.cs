using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using Microsoft.Win32;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

internal enum TrayToastAction
{
    None,
    Restore,
    DisablePopup,
}

internal readonly record struct ToastActivation
{
    private const string RestoreTrayArguments = "1|tray|restore";
    private const string DisableTrayPopupArguments = "1|tray|disable";

    public ToastActivation(StreamNotificationTarget stream) => Stream = stream;

    public ToastActivation(TrayToastAction trayAction) => TrayAction = trayAction;

    public StreamNotificationTarget? Stream { get; }

    public TrayToastAction TrayAction { get; }

    public string Encode()
    {
        if (Stream is { } stream) return stream.Encode();

        return TrayAction switch
        {
            TrayToastAction.Restore => RestoreTrayArguments,
            TrayToastAction.DisablePopup => DisableTrayPopupArguments,
            _ => throw new InvalidOperationException("The notification has no activation target."),
        };
    }

    public static bool TryDecode(string? arguments, out ToastActivation target)
    {
        var trayAction = arguments switch
        {
            RestoreTrayArguments => TrayToastAction.Restore,
            DisableTrayPopupArguments => TrayToastAction.DisablePopup,
            _ => TrayToastAction.None,
        };

        if (trayAction != TrayToastAction.None)
        {
            target = new ToastActivation(trayAction);
            return true;
        }

        if (StreamNotificationTarget.TryDecode(arguments, out var stream))
        {
            target = new ToastActivation(stream);
            return true;
        }

        target = default;
        return false;
    }
}

internal readonly record struct StreamNotificationTarget(UniqueStreamKey Key, string ChannelId, bool Followed)
{
    internal const int MaxArgumentLength = 4096;
    private const string Version = "1";
    private const string FollowedPage = "followed";
    private const string TopPage = "top";

    public string Encode()
    {
        var page = Followed ? FollowedPage : TopPage;
        var provider = Uri.EscapeDataString(Key.ApiClientName);
        var stream = Uri.EscapeDataString(Key.StreamId);
        var channel = Uri.EscapeDataString(ChannelId);
        return $"{Version}|{page}|{provider}|{stream}|{channel}";
    }

    public static bool TryDecode(string? arguments, out StreamNotificationTarget target)
    {
        target = default;
        if (arguments is not { Length: > 0 and <= MaxArgumentLength }) return false;

        var parts = arguments.Split('|');
        if (parts is not [Version, FollowedPage or TopPage, { Length: > 0 }, { Length: > 0 }, { Length: > 0 }]) return false;

        var provider = Uri.UnescapeDataString(parts[2]);
        var stream = Uri.UnescapeDataString(parts[3]);
        var channel = Uri.UnescapeDataString(parts[4]);
        var key = new UniqueStreamKey(provider, stream);
        var decoded = new StreamNotificationTarget(key, channel, parts[1] == FollowedPage);
        if (decoded.Encode() != arguments) return false;

        target = decoded;
        return true;
    }

    public bool MatchesStream(LivestreamModel stream) =>
        stream.UniqueStreamKey.Equals(Key) && stream.ChannelIdentifier.ChannelId == ChannelId;

    public int FindFollowedRow(List<LivestreamModel> rows)
    {
        for (var i = 0; i < rows.Count; i++)
            if (MatchesStream(rows[i])) return i;

        // YouTube replaces a live video's row with an offline row for its channel.
        var key = Key;
        var channel = ChannelId;
        return rows.FindIndex(stream => !stream.Live && stream.ApiClient.ApiName == key.ApiClientName &&
                                        stream.ChannelIdentifier.ChannelId == channel);
    }
}

internal sealed class StreamNotifications : IDisposable
{
    internal const string ActivationSwitch = "--toast-activated";
    internal const string MinimizedTitle = "Livestream Monitor";
    internal const string MinimizedMessage = "Livestream Monitor minimized to tray";
    private const string AppId = "Livestream.Monitor.Direct2D";
    private const string DisplayName = "Livestream Monitor";

    // Earlier versions named the Start menu shortcut after the Direct2D frontend.
    private const string LegacyShortcutName = "Livestream Monitor Direct2D.lnk";

    private static readonly Guid ActivatorId = new("5b6bc679-f166-4ef8-a71a-dc0b716fa43b");
    private static readonly string ServerKeyPath = $@"Software\Classes\CLSID\{ActivatorId:B}\LocalServer32";
    private readonly ToastActivator? activator;
    private NotificationThumbnails? thumbnails;
    private bool registered;

    public StreamNotifications(Func<ToastActivation, bool> activate)
    {
        try
        {
            WindowsToast.SetProcessIdentity(AppId);
            const int ServerUnavailable = unchecked((int)0x800706BA);
            activator = RegisterActivator(target => activate(target) ? 0 : ServerUnavailable);
        }
        catch (COMException exception)
        {
            LogFailure(exception);
        }
    }

    public static ToastActivator RegisterActivator(Func<ToastActivation, int> activate) =>
        new(ActivatorId, (appId, arguments) =>
        {
            const int InvalidArgument = unchecked((int)0x80070057);
            if (!string.Equals(appId, AppId, StringComparison.OrdinalIgnoreCase) ||
                !ToastActivation.TryDecode(arguments, out var target)) return InvalidArgument;

            return activate(target);
        });

    public void RefreshMovedRegistration()
    {
        if (activator == null) return;

        try
        {
            using var server = Registry.CurrentUser.OpenSubKey(ServerKeyPath);
            if (server?.GetValue("ServerExecutable") is not string previous) return;

            var executable = Environment.ProcessPath!;
            var command = GetRefreshedSenderCommand(
                previous,
                server.GetValue(null) as string,
                executable,
                Directory.GetCurrentDirectory());

            if (command != null) RegisterSenderCommand(executable, command);
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogFailure(exception);
        }
    }

    public async Task<Uri?> FindChannelIcon(ChannelIdentifier channel)
    {
        if (activator == null) return null;

        var images = thumbnails ??= new NotificationThumbnails();
        var iconUrl = await FindChannelIconUrl(channel);
        return await images.Find(iconUrl, channelIcon: true);
    }

    private static async Task<string?> FindChannelIconUrl(ChannelIdentifier channel)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var lookup = channel.ApiClient.FindChannelIconUrl(channel, deadline.Token);
            return await lookup.WaitAsync(deadline.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or TimeoutException or
                                          InvalidDataException or JsonException)
        {
            return null;
        }
    }

    public Task<Uri?> FindThumbnail(string? url)
    {
        if (activator == null) return Task.FromResult<Uri?>(null);

        return (thumbnails ??= new NotificationThumbnails()).Find(url);
    }

    public bool TryShow(
        string? title,
        string? message,
        StreamNotificationTarget target,
        Uri? thumbnail,
        bool longBanner,
        bool retryWithoutImage = true) =>
        TryShowToast(() => BuildToast(title, message, target, thumbnail, longBanner), retryWithoutImage);

    public bool TryShowMinimized() => TryShowToast(BuildMinimizedToast);

    private bool TryShowToast(Func<XElement> buildToast, bool retryWithoutImage = false)
    {
        if (activator == null) return false;

        try
        {
            if (!registered) RegisterCurrentSender();

            var toast = buildToast();
            var image = toast.Element("visual")?.Element("binding")?.Element("image");
            try
            {
                Show(toast);
            }
            catch (COMException) when (retryWithoutImage && image != null)
            {
                image.Remove();
                Show(toast);
            }

            return true;
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogFailure(exception);
            return false;
        }
    }

    public void Dispose()
    {
        thumbnails?.Dispose();
        activator?.Dispose();
    }

    private void RegisterCurrentSender()
    {
        var executable = Environment.ProcessPath!;
        var command = BuildSenderCommand(executable, Directory.GetCurrentDirectory());

        RegisterSenderCommand(executable, command);
        registered = true;
    }

    internal static string? GetRefreshedSenderCommand(
        string previous,
        string? previousCommand,
        string executable,
        string dataDirectory)
    {
        const StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;
        var legacySibling = Path.Join(Path.GetDirectoryName(executable), "Livestream.Monitor.Direct2D.exe");
        if (string.Equals(previous, legacySibling, PathComparison) &&
            string.Equals(Path.GetFileName(executable), "Livestream.Monitor.exe", PathComparison))
        {
            // An in-place upgrade can leave both executables on disk. Keep the registered profile for existing toasts.
            var quotedPrevious = $"\"{previous}\"";
            var prefix = $"{quotedPrevious} {ActivationSwitch} --data-directory \"";
            if (previousCommand?.StartsWith(prefix, PathComparison) == true) return $"\"{executable}\"{previousCommand[quotedPrevious.Length..]}";

            return BuildSenderCommand(executable, dataDirectory);
        }

        return File.Exists(previous) ? null : BuildSenderCommand(executable, dataDirectory);
    }

    private static string BuildSenderCommand(string executable, string dataDirectory)
    {
        var profile = Path.TrimEndingDirectorySeparator(dataDirectory);

        // A trailing separator on a volume root needs escaping before the closing command-line quote.
        if (Path.EndsInDirectorySeparator(profile)) profile += Path.DirectorySeparatorChar;

        return $"\"{executable}\" {ActivationSwitch} --data-directory \"{profile}\"";
    }

    private static void RegisterSenderCommand(string executable, string command)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var shortcut = Path.Combine(programs, DisplayName + ".lnk");

        DeleteLegacyShortcut(programs);

        using var server = Registry.CurrentUser.CreateSubKey(ServerKeyPath);
        if (server.GetValue(null) as string != command || !File.Exists(shortcut))
        {
            WindowsToast.SaveShortcut(shortcut, executable, AppId, ActivatorId);
            server.SetValue("", command);
        }

        server.SetValue("ServerExecutable", executable);

        // Register the sender directly so delivery does not depend on when Start indexes the shortcut.
        // https://github.com/CommunityToolkit/WindowsCommunityToolkit/blob/main/Microsoft.Toolkit.Uwp.Notifications/Toasts/Compat/ToastNotificationManagerCompat.cs
        using var identity = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
        identity.SetValue("DisplayName", DisplayName);
        identity.SetValue("CustomActivator", ActivatorId.ToString("B"));
        var iconPath = CacheSenderIcon();
        if (iconPath == null) return;

        identity.SetValue("IconUri", iconPath, RegistryValueKind.ExpandString);
        DeleteLegacyDataFiles();

        static void DeleteLegacyShortcut(string programs)
        {
            try
            {
                File.Delete(Path.Combine(programs, LegacyShortcutName));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A stale Start menu entry must not stop the sender registration.
                LogFailure(exception);
            }
        }

        // 3.1.0 kept the notification icon and thumbnails in this folder. Only those files are removed, since other files there
        // may be the user's own. The icon is removed once IconUri points to the new one. The thumbnails are removed as they
        // expire, so notification history keeps its images. The folders are removed once empty.
        static void DeleteLegacyDataFiles()
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = new DirectoryInfo(Path.Combine(localData, "Livestream.Monitor", "Direct2D"));
            if (localData.Length == 0 || !folder.Exists) return;

            try
            {
                File.Delete(Path.Combine(folder.FullName, "NotificationIcon.ico"));

                var thumbnails = new DirectoryInfo(Path.Combine(folder.FullName, "NotificationThumbnails"));
                if (thumbnails.Exists)
                {
                    NotificationThumbnails.Prune(thumbnails, currentPath: "");
                    if (!thumbnails.EnumerateFileSystemInfos().Any()) thumbnails.Delete();
                }

                if (!folder.EnumerateFileSystemInfos().Any()) folder.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Files the Shell still holds are retried at the next registration.
            }
        }
    }

    private static string? CacheSenderIcon()
    {
        try
        {
            // The Shell reads IconUri from disk, including after the app has exited.
            // https://github.com/microsoft/WindowsAppSDK/blob/main/dev/AppNotifications/AppNotificationUtility.cpp
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(localData, "Livestream.Monitor");
            var path = Path.Combine(directory, "NotificationIcon.ico");

            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, Path.GetRandomFileName());
            var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            try
            {
                using (file) ExecutableIcon.WriteTo(file, NativeLibrary.GetMainProgramHandle());

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }

            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogFailure(exception);
            return null;
        }
    }

    private static void Show(XElement toast) => WindowsToast.Show(AppId, toast.ToString(SaveOptions.DisableFormatting));

    internal static XElement BuildMinimizedToast()
    {
        var restore = new ToastActivation(TrayToastAction.Restore);
        var disable = new ToastActivation(TrayToastAction.DisablePopup);
        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"),
            new XElement("text", MinimizedTitle), new XElement("text", MinimizedMessage));
        var action = new XElement("action", new XAttribute("content", "Don't show me this again"),
            new XAttribute("arguments", disable.Encode()), new XAttribute("activationType", "foreground"));
        return new XElement("toast", new XAttribute("launch", restore.Encode()),
            new XElement("visual", binding), new XElement("actions", action));
    }

    internal static XElement BuildToast(
        string? title,
        string? message,
        StreamNotificationTarget target,
        Uri? thumbnail,
        bool longBanner)
    {
        var arguments = target.Encode();
        if (arguments.Length > StreamNotificationTarget.MaxArgumentLength)
            throw new ArgumentException("The stream notification arguments exceed the activation limit.");

        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"),
            new XElement("text", title ?? ""), new XElement("text", message ?? ""));
        if (thumbnail != null)
        {
            var image = new XElement("image", new XAttribute("placement", "appLogoOverride"),
                new XAttribute("src", thumbnail.AbsoluteUri));
            binding.Add(image);
        }

        var duration = new XAttribute("duration", longBanner ? "long" : "short");
        return new XElement("toast", new XAttribute("launch", arguments), duration, new XElement("visual", binding));
    }

    internal static void LogFailure(Exception exception)
    {
        try
        {
            File.AppendAllText(Program.ErrorLogFile, $"{DateTimeOffset.Now:o} Stream notification: {exception}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError(logError.Message);
        }
    }
}
