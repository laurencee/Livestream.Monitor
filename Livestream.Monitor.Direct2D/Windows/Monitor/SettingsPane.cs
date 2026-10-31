using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

internal sealed class SettingsPane
{
    private const float HeaderHeight = 46;
    private const float FooterHeight = 58;
    private const float Margin = 10;
    private const float RowHeight = 28;
    private const float InputHeight = 26;
    private const float InputSpacing = 6;
    private const float GroupPadding = 6;
    private const float GroupGap = 6;
    private const float GroupLabelHeight = 20;
    private const float GroupCheckHeight = 24;
    private const float GroupInputSpacing = 2;
    private const float GroupBorderThickness = 0.5f;
    private const float GroupBorderOpacity = 0.4f;
    private const float ScrollbarSpace = 6;
    private const float SectionGap = 10;
    private const float ScrollStep = 36;
    private const int MaximumViewerDigits = 7;
    private static readonly string[] BaseNames = ["Base Light", "Base Dark"];
    private static readonly MetroThemeAccentColour[] Accents = Enum.GetValues<MetroThemeAccentColour>();
    private static readonly string[] AccentNames = Enum.GetNames<MetroThemeAccentColour>();
    private readonly ISettingsHandler handler;
    private readonly IUserDialogs dialogs;
    private readonly Action close;
    private readonly Action? saved;
    private readonly Settings draft;
    private readonly HashSet<string> seededInputs = [];
    private readonly Dictionary<string, PathState> paths = new(StringComparer.Ordinal);
    private readonly bool[] expanded = [true, false, false, false];
    private float scroll;
    private float maximumScroll;
    private string minimumViewersText;
    private bool validMinimumViewers = true;

    public SettingsPane(
        ISettingsHandler handler,
        IUserDialogs dialogs,
        Action close,
        Action? saved = null)
    {
        this.handler = handler;
        this.dialogs = dialogs;
        this.close = close;
        this.saved = saved;
        draft = new Settings();
        CopyEditable(handler.Settings, draft);
        minimumViewersText = draft.MinimumEventViewers.ToString(CultureInfo.InvariantCulture);
    }

    public void Draw(SurfaceUi ui, RectangleF bounds)
    {
        ui.Overlay();
        ui.Fill(bounds, ui.Palette.Panel);
        ui.Border(bounds, ui.Palette.Border);
        var closeBounds = new RectangleF(x: bounds.X + Margin, y: bounds.Y + 9, width: 28, height: 28);
        ui.RightChevronButton(id: "settings.close", bounds: closeBounds, action: close, name: "Close");
        var titleBounds = new RectangleF(x: bounds.X + 47, y: bounds.Y + 5, width: bounds.Width - 57, height: 36);
        ui.Text("Settings", titleBounds, size: 22);

        var body = new RectangleF(
            x: bounds.X + Margin,
            y: bounds.Y + HeaderHeight,
            width: bounds.Width - Margin * 2,
            height: Math.Max(0, bounds.Height - HeaderHeight - FooterHeight)
        );

        scroll = Math.Clamp(scroll, 0, maximumScroll);
        var y = body.Y - scroll;
        ui.PushClip(body);
        DrawExpander(ui, body, ref y, index: 0, title: "General settings");
        if (expanded[0]) DrawGeneral(ui, body, ref y);
        y += SectionGap;
        DrawExpander(ui, body, ref y, index: 1, title: "Twitch");
        if (expanded[1]) DrawPlatform(ui, body, ref y, name: "Twitch", draft.Twitch);
        y += SectionGap;
        DrawExpander(ui, body, ref y, index: 2, title: "Kick");
        if (expanded[2]) DrawPlatform(ui, body, ref y, name: "Kick", draft.Kick);
        y += SectionGap;
        DrawExpander(ui, body, ref y, index: 3, title: "YouTube");
        if (expanded[3]) DrawPlatform(ui, body, ref y, name: "YouTube", draft.YouTube);
        ui.PopClip();

        maximumScroll = Math.Max(0, y + scroll - body.Bottom);

        if (scroll > maximumScroll)
        {
            scroll = maximumScroll;
            ui.Invalidate?.Invoke();
        }

        DrawScrollbar(ui, body);
        var saveBounds = new RectangleF(
            x: bounds.X + 30,
            y: bounds.Bottom - FooterHeight + 5,
            width: bounds.Width - 60,
            height: 28
        );

        var canSave = CanSave();
        ui.Button(id: "settings.save", text: "Save", bounds: saveBounds, action: Save, enabled: canSave);
    }

    public void Scroll(int steps) => scroll = Math.Clamp(scroll - steps * ScrollStep, 0, maximumScroll);

    private void DrawGeneral(SurfaceUi ui, RectangleF body, ref float y)
    {
        const float ThemeHeight = 34;
        var themes = Row(body, ref y, ThemeHeight);
        var baseBounds = new RectangleF(x: themes.X + 5, y: themes.Y + 3, width: 108, height: InputHeight);
        var accentBounds = new RectangleF(x: themes.X + 123, y: themes.Y + 3, width: 132, height: InputHeight);
        var baseIndex = handler.Settings.MetroThemeBaseColour == MetroThemeBaseColour.BaseDark ? 1 : 0;
        var accentIndex = Array.IndexOf(Accents, handler.Settings.MetroThemeAccentColour);
        ui.Combo(
            id: "settings.base",
            text: "Base Theme",
            choices: BaseNames,
            selectedIndex: baseIndex,
            bounds: baseBounds,
            changed: SetBaseTheme,
            keepOpen: true
        );
        ui.Combo(
            id: "settings.accent",
            text: "Accent Colour",
            choices: AccentNames,
            selectedIndex: accentIndex,
            bounds: accentBounds,
            changed: SetAccent,
            keepOpen: true
        );

        var minimumTop = y;
        var minimumBody = BeginGroup(body, ref y);
        var label = Row(minimumBody, ref y, GroupLabelHeight);
        ui.Text("Popular stream minimum viewers (0 for off)", label, wrap: true);
        const string MinimumToolTip =
            "Minimum number of viewers for a stream to be shown in a notification as a popular or event stream.\n" +
            "This helps you not miss out on special events or popular streams.";

        ui.ToolTip(id: "settings.minimum.label", label, MinimumToolTip);
        DrawInput(
            ui,
            minimumBody,
            ref y,
            id: "settings.minimum",
            minimumViewersText,
            placeholder: "",
            MinimumChanged,
            spacing: GroupInputSpacing,
            toolTip: MinimumToolTip);
        EndGroup(ui, body, minimumTop, ref y);

        const float GeneralCheckHeight = 20;
        DrawCheck(
            ui,
            body,
            ref y,
            "versions",
            "Check for new app versions",
            draft.CheckForNewVersions,
            value => draft.CheckForNewVersions = value,
            height: GeneralCheckHeight,
            toolTip: "When unchecked, app version checks on startup are disabled."
        );
        DrawCheck(
            ui,
            body,
            ref y,
            "notifications",
            "Disable notifications",
            draft.DisableNotifications,
            value => draft.DisableNotifications = value,
            height: GeneralCheckHeight,
            toolTip: "When checked, all notifications are disabled."
        );
        DrawCheck(
            ui,
            body,
            ref y,
            "refresherrors",
            "Disable refresh error dialogs",
            draft.DisableRefreshErrorDialogs,
            value => draft.DisableRefreshErrorDialogs = value,
            height: GeneralCheckHeight,
            toolTip: "When checked, refresh failure dialogs are disabled."
        );
        DrawCheck(
            ui,
            body,
            ref y,
            "traypopup",
            "Disable minimize to tray popup",
            draft.DisableMinimizeToTrayNotification,
            value => draft.DisableMinimizeToTrayNotification = value,
            height: GeneralCheckHeight,
            toolTip: "When checked, the first minimize-to-tray popup after launch is disabled."
        );
        DrawCheck(
            ui,
            body,
            ref y,
            "output",
            "Hide stream output on load",
            draft.HideStreamOutputMessageBoxOnLoad,
            value => draft.HideStreamOutputMessageBoxOnLoad = value,
            height: GeneralCheckHeight,
            toolTip: "When checked, the stream output box is hidden after a successful launch."
        );

        const float OpenFileGap = 4;
        y += OpenFileGap;
        var openBounds = Row(body, ref y, RowHeight);
        ui.Button(id: "settings.openfile", text: "Open settings file", bounds: openBounds, action: () => _ = OpenSettings());

        void MinimumChanged(string value)
        {
            if (value.Length > MaximumViewerDigits || value.Any(character => !char.IsAsciiDigit(character)))
            {
                ui.SetValue("settings.minimum", minimumViewersText);
                return;
            }

            minimumViewersText = value;
            validMinimumViewers = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var minimum);
            if (validMinimumViewers) draft.MinimumEventViewers = minimum;
        }
    }

    private void DrawPlatform(
        SurfaceUi ui,
        RectangleF body,
        ref float y,
        string name,
        ApiPlatformSettings platform)
    {
        if (platform is TwitchSettings twitch)
        {
            var authTop = y;
            var authBody = BeginGroup(body, ref y);
            var authLabel = Row(authBody, ref y, GroupLabelHeight);
            ui.Text("Auth token", authLabel);
            const string AuthToolTip =
                "Optional Twitch OAuth token. Delete it from settings.json and relaunch the app to reauthorize.";

            DrawInput(
                ui,
                authBody,
                ref y,
                "settings.auth",
                twitch.AuthToken,
                "",
                value => twitch.AuthToken = value,
                spacing: GroupInputSpacing,
                toolTip: AuthToolTip
            );
            const string BypassToolTip = "When checked, Livestream Monitor sends its Twitch client id so an OAuth " +
                                         "token is not required to launch streams.";

            DrawCheck(
                ui,
                authBody,
                ref y,
                "bypass",
                "Bypass OAuth to Twitch",
                twitch.PassthroughClientId,
                value => twitch.PassthroughClientId = value,
                height: GroupCheckHeight,
                toolTip: BypassToolTip
            );
            EndGroup(ui, body, authTop, ref y);
        }

        var hint = platform is TwitchSettings ? "Args support {url} and {auth_token}." : "Args support {url}.";
        var streamId = name + ".stream";
        var vodId = name + ".vod";
        var chatId = name + ".chat";
        DrawCommand(ui, body, ref y, streamId, title: "Stream command", platform.StreamCommand, hint);
        DrawCommand(ui, body, ref y, vodId, title: "VOD command", platform.VodCommand, hint);
        DrawCommand(ui, body, ref y, chatId, title: "Chat command", platform.ChatCommand, hint);
    }

    private void DrawCommand(
        SurfaceUi ui,
        RectangleF body,
        ref float y,
        string id,
        string title,
        ExecCommand command,
        string hint)
    {
        var heading = Row(body, ref y, GroupCheckHeight);
        ui.Text(title, heading, weight: FontWeight.SemiBold);
        var pathTop = y;
        var pathBody = BeginGroup(body, ref y);
        var pathLabel = Row(pathBody, ref y, GroupLabelHeight);
        ui.Text("File path", pathLabel);
        DrawInput(
            ui,
            pathBody,
            ref y,
            id + ".path",
            command.FilePath,
            "",
            value => command.FilePath = value,
            spacing: GroupInputSpacing,
            toolTip: "Enter a command name on PATH or a full executable path."
        );

        const float StatusHeight = 32;
        var state = ResolvePath(command.FilePath);
        var statusBounds = Row(pathBody, ref y, StatusHeight);
        const uint ValidPathColor = 0xFF008000;
        const uint InvalidPathColor = 0xFFCD5C5C;
        var statusColor = state.Valid ? ValidPathColor : InvalidPathColor;
        ui.Text(state.Message, statusBounds, statusColor, size: 11, wrap: true);
        EndGroup(ui, body, pathTop, ref y);

        var argsTop = y;
        var argsBody = BeginGroup(body, ref y);
        var argsLabel = Row(argsBody, ref y, GroupLabelHeight);
        ui.Text("Arguments", argsLabel);
        const float HintHeight = 18;
        var hintBounds = Row(argsBody, ref y, HintHeight);
        ui.Text(hint, hintBounds, ui.Palette.MutedText, size: 11, wrap: true);
        DrawInput(
            ui,
            argsBody,
            ref y,
            id + ".args",
            command.Args,
            "",
            value => command.Args = value,
            spacing: GroupInputSpacing
        );
        EndGroup(ui, body, argsTop, ref y);

        var captureTop = y;
        var captureBody = BeginGroup(body, ref y);
        DrawCheck(
            ui,
            captureBody,
            ref y,
            id + ".stdout",
            "Capture stdout",
            command.CaptureStandardOutput,
            value => command.CaptureStandardOutput = value,
            height: GroupCheckHeight
        );
        DrawCheck(
            ui,
            captureBody,
            ref y,
            id + ".stderr",
            "Capture stderr",
            command.CaptureErrorOutput,
            value => command.CaptureErrorOutput = value,
            height: GroupCheckHeight
        );
        EndGroup(ui, body, captureTop, ref y);
        y += SectionGap;
    }

    private static RectangleF BeginGroup(RectangleF body, ref float y)
    {
        y += GroupPadding;
        return body with { X = body.X + GroupPadding, Width = body.Width - GroupPadding * 2 };
    }

    private static void EndGroup(
        SurfaceUi ui,
        RectangleF body,
        float top,
        ref float y)
    {
        y += GroupPadding;
        var border = new RectangleF(x: body.X, y: top, width: body.Width - ScrollbarSpace, height: y - top);
        var color = ui.Palette.IsDark
            ? Theme.Blend(ui.Palette.Panel, ui.Palette.Border, GroupBorderOpacity)
            : ui.Palette.Border;

        var thickness = ui.Palette.IsDark ? GroupBorderThickness : 1;
        ui.Border(border, color, thickness);
        y += GroupGap;
    }

    private void DrawExpander(
        SurfaceUi ui,
        RectangleF body,
        ref float y,
        int index,
        string title)
    {
        var bounds = Row(body, ref y, RowHeight);
        ui.Expander(
            "settings.section." + index,
            title,
            expanded[index],
            bounds,
            value => expanded[index] = value);
    }

    private void DrawInput(
        SurfaceUi ui,
        RectangleF body,
        ref float y,
        string id,
        string? value,
        string placeholder,
        Action<string> changed,
        float spacing = InputSpacing,
        string? toolTip = null)
    {
        var bounds = Row(body, ref y, InputHeight);
        y += spacing;
        if (seededInputs.Add(id)) ui.SetValue(id, value ?? "");
        ui.Input(id, placeholder, bounds, changed, toolTip: toolTip);
    }

    private static void DrawCheck(
        SurfaceUi ui,
        RectangleF body,
        ref float y,
        string id,
        string text,
        bool value,
        Action<bool> changed,
        float height = RowHeight,
        string? toolTip = null)
    {
        var bounds = Row(body, ref y, height);
        var widgetId = "settings." + id;
        ui.Check(widgetId, text, value, bounds, changed, toolTip: toolTip);
    }

    private static RectangleF Row(RectangleF body, ref float y, float height)
    {
        var bounds = new RectangleF(x: body.X, y: y, width: body.Width - ScrollbarSpace, height: height);
        y += height;
        return bounds;
    }

    private void DrawScrollbar(SurfaceUi ui, RectangleF body)
    {
        if (maximumScroll <= 0 || body.Height <= 0) return;

        const float MinimumThumbHeight = 24;
        const float ScrollbarWidth = 4;
        var height = Math.Max(MinimumThumbHeight, body.Height * body.Height / (body.Height + maximumScroll));
        var top = body.Y + (body.Height - height) * scroll / maximumScroll;
        var thumb = new RectangleF(x: body.Right - ScrollbarWidth, y: top, width: ScrollbarWidth, height: height);
        ui.Fill(thumb, ui.Palette.MutedText);
        var up = new RectangleF(x: body.Right - ScrollbarWidth, y: body.Y, width: ScrollbarWidth, height: top - body.Y);
        var down = new RectangleF(
            x: body.Right - ScrollbarWidth,
            y: thumb.Bottom,
            width: ScrollbarWidth,
            height: body.Bottom - thumb.Bottom
        );

        ui.Hit(id: "settings.scrollup", bounds: up, action: () => Scroll(1), name: "Scroll up");
        ui.Hit(id: "settings.scrolldown", bounds: down, action: () => Scroll(-1), name: "Scroll down");
    }

    private void SetBaseTheme(int index)
    {
        var baseTheme = index == 0 ? MetroThemeBaseColour.BaseLight : MetroThemeBaseColour.BaseDark;
        handler.Settings.MetroThemeBaseColour = baseTheme;
        handler.SaveSettings();
        saved?.Invoke();
    }

    private void SetAccent(int index)
    {
        handler.Settings.MetroThemeAccentColour = Accents[index];
        handler.SaveSettings();
        saved?.Invoke();
    }

    private bool CanSave()
    {
        if (!validMinimumViewers) return false;

        var changed = !EditableMatch(draft, handler.Settings);
        return changed && Valid(draft.Twitch) && Valid(draft.Kick) && Valid(draft.YouTube);
    }

    private void Save()
    {
        if (!CanSave()) return;

        CopyEditable(draft, handler.Settings);
        handler.SaveSettings();
        saved?.Invoke();
    }

    // The settings this pane edits and saves together; the theme applies at once instead. A setting added here is
    // also compared in EditableMatch.
    private static void CopyEditable(Settings source, Settings target)
    {
        target.MinimumEventViewers = source.MinimumEventViewers;
        target.CheckForNewVersions = source.CheckForNewVersions;
        target.DisableNotifications = source.DisableNotifications;
        target.DisableRefreshErrorDialogs = source.DisableRefreshErrorDialogs;
        target.DisableMinimizeToTrayNotification = source.DisableMinimizeToTrayNotification;
        target.HideStreamOutputMessageBoxOnLoad = source.HideStreamOutputMessageBoxOnLoad;
        CopyPlatform(source.Twitch, target.Twitch);
        CopyPlatform(source.Kick, target.Kick);
        CopyPlatform(source.YouTube, target.YouTube);
        target.Twitch.AuthToken = source.Twitch.AuthToken;
        target.Twitch.PassthroughClientId = source.Twitch.PassthroughClientId;
    }

    private static bool EditableMatch(Settings first, Settings second) =>
        first.MinimumEventViewers == second.MinimumEventViewers &&
        first.CheckForNewVersions == second.CheckForNewVersions &&
        first.DisableNotifications == second.DisableNotifications &&
        first.DisableRefreshErrorDialogs == second.DisableRefreshErrorDialogs &&
        first.DisableMinimizeToTrayNotification == second.DisableMinimizeToTrayNotification &&
        first.HideStreamOutputMessageBoxOnLoad == second.HideStreamOutputMessageBoxOnLoad &&
        first.Twitch.AuthToken == second.Twitch.AuthToken &&
        first.Twitch.PassthroughClientId == second.Twitch.PassthroughClientId &&
        Matches(first.Twitch, second.Twitch) &&
        Matches(first.Kick, second.Kick) &&
        Matches(first.YouTube, second.YouTube);

    private async Task OpenSettings()
    {
        try
        {
            var start = new ProcessStartInfo(SettingsHandler.SettingsFileName) { UseShellExecute = true };
            Process.Start(start);
        }
        catch (Exception exception)
        {
            var message = $"Failed to open settings file '{SettingsHandler.SettingsFileName}' {Environment.NewLine}" +
                          exception.ExtractErrorMessage();

            await dialogs.ShowMessage("Error", message);
        }
    }

    private bool Valid(ApiPlatformSettings platform) =>
        ResolvePath(platform.StreamCommand.FilePath).Valid &&
        ResolvePath(platform.VodCommand.FilePath).Valid &&
        ResolvePath(platform.ChatCommand.FilePath).Valid;

    private PathState ResolvePath(string? path)
    {
        path ??= "";
        if (paths.TryGetValue(path, out var cached)) return cached;

        var valid = WindowsCommandResolver.TryResolveExecutable(path, out var resolved);
        var message = valid ? "Resolved: " + resolved : "Unable to resolve this command or executable path.";
        if (string.IsNullOrWhiteSpace(path)) message = "Enter a command name or full executable path.";
        var state = new PathState(valid, message);
        paths.Add(path, state);
        return state;
    }

    private static bool Matches(ApiPlatformSettings first, ApiPlatformSettings second) =>
        MatchesCommand(first.StreamCommand, second.StreamCommand) &&
        MatchesCommand(first.VodCommand, second.VodCommand) &&
        MatchesCommand(first.ChatCommand, second.ChatCommand);

    private static bool MatchesCommand(ExecCommand first, ExecCommand second) =>
        first.FilePath == second.FilePath && first.Args == second.Args &&
        first.CaptureStandardOutput == second.CaptureStandardOutput &&
        first.CaptureErrorOutput == second.CaptureErrorOutput;

    private static void CopyPlatform(ApiPlatformSettings source, ApiPlatformSettings target)
    {
        CopyCommand(source.StreamCommand, target.StreamCommand);
        CopyCommand(source.VodCommand, target.VodCommand);
        CopyCommand(source.ChatCommand, target.ChatCommand);
    }

    private static void CopyCommand(ExecCommand source, ExecCommand target)
    {
        target.FilePath = source.FilePath;
        target.Args = source.Args;
        target.CaptureStandardOutput = source.CaptureStandardOutput;
        target.CaptureErrorOutput = source.CaptureErrorOutput;
    }

    private sealed record PathState(bool Valid, string Message);
}
