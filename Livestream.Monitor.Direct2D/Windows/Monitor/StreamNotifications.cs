using System.Runtime.InteropServices;
using System.Xml.Linq;
using Livestream.Monitor.Core;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Model;
using Microsoft.Win32;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

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

    public int FindFollowedRow(List<LivestreamModel> rows)
    {
        var key = Key;
        var channel = ChannelId;
        var exact = rows.FindIndex(stream => stream.UniqueStreamKey.Equals(key) && stream.ChannelIdentifier.ChannelId == channel);
        if (exact >= 0) return exact;

        // YouTube replaces a live video's row with an offline row for its channel.
        return rows.FindIndex(stream => !stream.Live && stream.ApiClient.ApiName == key.ApiClientName &&
                                        stream.ChannelIdentifier.ChannelId == channel);
    }
}

internal sealed class StreamNotifications : IDisposable
{
    internal const string ActivationSwitch = "--toast-activated";
    private const string AppId = "Livestream.Monitor.Direct2D";
    private const string DisplayName = "Livestream Monitor Direct2D";
    private static readonly Guid ActivatorId = new("5b6bc679-f166-4ef8-a71a-dc0b716fa43b");
    private static readonly string ServerKeyPath = $@"Software\Classes\CLSID\{ActivatorId:B}\LocalServer32";
    private readonly ToastActivator? activator;
    private NotificationThumbnails? thumbnails;
    private bool registered;

    public StreamNotifications(Func<StreamNotificationTarget, bool> activate)
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

    public static ToastActivator RegisterActivator(Func<StreamNotificationTarget, int> activate) =>
        new(ActivatorId, (appId, arguments) =>
        {
            const int InvalidArgument = unchecked((int)0x80070057);
            if (!string.Equals(appId, AppId, StringComparison.OrdinalIgnoreCase) ||
                !StreamNotificationTarget.TryDecode(arguments, out var target)) return InvalidArgument;

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

    public Task<Uri?> GetThumbnail(string? url)
    {
        if (activator == null || string.IsNullOrWhiteSpace(url)) return Task.FromResult<Uri?>(null);

        return (thumbnails ??= new NotificationThumbnails()).Get(url);
    }

    public bool TryShow(
        string? title,
        string? message,
        StreamNotificationTarget target,
        Uri? thumbnail,
        bool longBanner)
    {
        if (activator == null) return false;

        try
        {
            if (!registered) RegisterCurrentSender();

            try
            {
                Show(title, message, target, thumbnail, longBanner);
            }
            catch (COMException) when (thumbnail != null)
            {
                Show(title, message, target, thumbnail: null, longBanner);
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

    private static void Show(
        string? title,
        string? message,
        StreamNotificationTarget target,
        Uri? thumbnail,
        bool longBanner)
    {
        var toast = BuildToast(title, message, target, thumbnail, longBanner);
        WindowsToast.Show(AppId, toast.ToString(SaveOptions.DisableFormatting));
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
