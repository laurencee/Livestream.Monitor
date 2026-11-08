using System.Runtime.InteropServices;
using System.Xml.Linq;
using Livestream.Monitor.Core;
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
    private const string DisplayName = "Livestream Monitor Direct2D";
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
            if (server?.GetValue("ServerExecutable") is not string previous || File.Exists(previous)) return;

            RegisterCurrentSender();
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
        var iconUrl = await LivestreamNotification.FindChannelIconUrl(channel);
        return await images.Find(iconUrl, channelIcon: true);
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
        RegisterSender(Environment.ProcessPath!, Directory.GetCurrentDirectory());
        registered = true;
    }

    internal static void RegisterSender(string executable, string dataDirectory)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var shortcut = Path.Combine(programs, DisplayName + ".lnk");
        using var server = Registry.CurrentUser.CreateSubKey(ServerKeyPath);
        var profile = Path.TrimEndingDirectorySeparator(dataDirectory);
        // A trailing separator on a volume root needs escaping before the closing command-line quote.
        if (Path.EndsInDirectorySeparator(profile)) profile += Path.DirectorySeparatorChar;
        var command = $"\"{executable}\" {ActivationSwitch} --data-directory \"{profile}\"";
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
        if (iconPath != null) identity.SetValue("IconUri", iconPath, RegistryValueKind.ExpandString);
    }

    private static string? CacheSenderIcon()
    {
        try
        {
            // The Shell reads IconUri from disk, including after the app has exited.
            // https://github.com/microsoft/WindowsAppSDK/blob/main/dev/AppNotifications/AppNotificationUtility.cpp
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(localData, "Livestream.Monitor", "Direct2D");
            var path = Path.Combine(directory, "NotificationIcon.ico");
            using var icon = EmbeddedImages.Open(EmbeddedImages.TrayIcon) ??
                             throw new FileNotFoundException("The embedded application icon is missing.");

            Directory.CreateDirectory(directory);

            using var file = File.Create(path);
            icon.CopyTo(file);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
            File.AppendAllText("direct2d-errors.log", $"{DateTimeOffset.Now:o} Stream notification: {exception}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError(logError.Message);
        }
    }
}
