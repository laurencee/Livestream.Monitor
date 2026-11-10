using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

internal sealed class SettingsPane
{
    private const float HeaderHeight = 46;
    private const float SaveButtonHeight = 28;
    private const float SaveButtonPadding = 5;
    private const float FooterDividerHeight = 1;
    private const float FooterHeight = FooterDividerHeight + SaveButtonHeight + SaveButtonPadding * 2;
    private const float Margin = 10;
    private const float RowHeight = 28;
    private const float InputHeight = 26;
    private const float InputSpacing = 6;
    private const float GroupPadding = 6;
    internal const float GroupGap = 6;
    private const float LabelSpacing = 4;
    private const float StatusSpacing = 5;
    private const float TextSize = 12;
    private const float HeadingSpacing = 8;
    private const float GroupCheckHeight = 24;
    private const float PreferenceCheckHeight = 20;
    private const float GroupInputSpacing = 2;
    private const float GroupBorderThickness = 0.5f;
    private const float GroupBorderOpacity = 0.4f;
    private const float ScrollbarSpace = VerticalScrollBar.Width + 2;
    private const float SectionGap = 10;
    private const int ScrollStep = 36;
    private const int MaximumViewerDigits = 7;
    private const int TwitchSectionIndex = 1;
    private const int KickSectionIndex = 2;
    private const int YouTubeSectionIndex = 3;
    private const int StreamNotificationsSectionIndex = 5;
    private const string MinimumViewersId = "settings.minimum";
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
    private readonly bool[] expanded = [true, false, false, false, false, false];
    private readonly StreamQualityPane streamQualities;
    private int scroll;
    private int maximumScroll;
    private string minimumViewersText;
    private bool validMinimumViewers = true;
    private string? revealInput;
    private RectangleF? revealBounds;
    private string? focusRevealedInput;

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
        streamQualities = new StreamQualityPane(handler.Settings);
        draft = new Settings();
        CopyEditable(handler.Settings, draft);
        minimumViewersText = draft.MinimumEventViewers.ToString(CultureInfo.InvariantCulture);
        ScrollBar = new VerticalScrollBar(SetScroll);
    }

    public VerticalScrollBar ScrollBar { get; }
    public bool HasQualityChanges => streamQualities.HasChanges;

    public void DiscardQualityChanges(SurfaceUi ui) => streamQualities.Revert(ui);

    public void SyncTrayPopupSetting() =>
        draft.DisableMinimizeToTrayNotification = handler.Settings.DisableMinimizeToTrayNotification;

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
        DrawExpander(ui, body, ref y, index: StreamNotificationsSectionIndex, title: "Stream Notifications");
        if (expanded[StreamNotificationsSectionIndex]) DrawStreamNotifications(ui, body, ref y);
        y += SectionGap;
        DrawExpander(ui, body, ref y, index: TwitchSectionIndex, title: "Twitch");
        if (expanded[TwitchSectionIndex]) DrawPlatform(ui, body, ref y, name: "Twitch", draft.Twitch);
        y += SectionGap;
        DrawExpander(ui, body, ref y, index: KickSectionIndex, title: "Kick");
        if (expanded[KickSectionIndex]) DrawPlatform(ui, body, ref y, name: "Kick", draft.Kick);
        y += SectionGap;
        DrawExpander(ui, body, ref y, index: YouTubeSectionIndex, title: "YouTube");
        if (expanded[YouTubeSectionIndex]) DrawPlatform(ui, body, ref y, name: "YouTube", draft.YouTube);
        y += SectionGap;
        DrawExpander(ui, body, ref y, index: 4, title: "Stream quality");
        if (expanded[4]) streamQualities.Draw(ui, body with { Width = body.Width - ScrollbarSpace }, ref y);
        ui.PopClip();

        var viewport = (int)body.Height;
        var extent = (int)MathF.Ceiling(y + scroll - body.Top);
        maximumScroll = Math.Max(0, extent - viewport);

        RevealInvalidInput(ui, body);

        var barBounds = body with { X = body.Right - VerticalScrollBar.Width, Width = VerticalScrollBar.Width };
        var scrollbarExtent = viewport >= VerticalScrollBar.MinimumDraggableHeight ? extent : viewport;
        ScrollBar.Update(area: barBounds, extent: scrollbarExtent, viewport, offset: scroll, lineSize: ScrollStep);
        ScrollBar.Draw(ui, automationPrefix: "settings");

        var footerTop = bounds.Bottom - FooterHeight;
        var divider = body with { Y = footerTop, Height = FooterDividerHeight };
        ui.Fill(divider, ui.Palette.Border);

        var saveBounds = new RectangleF(
            x: bounds.X + 30,
            y: divider.Bottom + SaveButtonPadding,
            width: bounds.Width - 60,
            height: SaveButtonHeight
        );

        var otherChanges = !EditableMatch(draft, handler.Settings);
        var hasChanges = HasChanges(otherChanges);
        var invalidField = hasChanges ? FindInvalidField(otherChanges) : null;
        var canSave = hasChanges && invalidField == null;
        if (invalidField is { } field)
        {
            const float ErrorButtonWidth = 105;
            const float ButtonGap = 6;
            var errorBounds = saveBounds with { X = saveBounds.Right - ErrorButtonWidth, Width = ErrorButtonWidth };
            saveBounds.Width -= ErrorButtonWidth + ButtonGap;
            ui.Button(
                id: "settings.showerror",
                text: "Show error",
                bounds: errorBounds,
                action: () => Reveal(field),
                accent: true,
                toolTip: "Show the field blocking Save"
            );
        }

        ui.Button(id: "settings.save", text: "Save", bounds: saveBounds, action: Save, enabled: canSave, accent: canSave);
    }

    public void Scroll(int steps) => SetScroll(Math.Clamp(scroll - steps * ScrollStep, 0, maximumScroll));

    public bool ScrollWithKey(int key)
    {
        var pageHeight = (int)ScrollBar.Bounds.Height;
        var next = key switch
        {
            VirtualKey.PageUp => Math.Max(0, scroll - pageHeight),
            VirtualKey.PageDown => Math.Min(maximumScroll, scroll + pageHeight),
            VirtualKey.Home => 0,
            VirtualKey.End => maximumScroll,
            _ => scroll,
        };

        if (next == scroll) return false;

        SetScroll(next);
        return true;
    }

    private void SetScroll(int offset)
    {
        if (offset == scroll) return;

        scroll = offset;
        revealInput = null;
        focusRevealedInput = null;
    }

    public void FocusRevealedInput(SurfaceUi ui)
    {
        if (focusRevealedInput is not { } id) return;

        focusRevealedInput = null;
        ui.FocusVisibleInput(id);
    }

    private void Reveal(InvalidField field)
    {
        expanded[field.SectionIndex] = true;
        revealInput = field.Id;
    }

    private void RevealInvalidInput(SurfaceUi ui, RectangleF body)
    {
        var nextScroll = Math.Min(scroll, maximumScroll);
        if (revealBounds is { } target)
        {
            if (!body.Contains(target))
            {
                var topMargin = Math.Max(0, (body.Height - target.Height) / 2);
                var offset = target.Top - body.Top - topMargin;
                nextScroll = Math.Clamp(scroll + (int)MathF.Round(offset), 0, maximumScroll);
            }

            if (nextScroll == scroll && body.Contains(target))
            {
                focusRevealedInput = revealInput;
                ui.Invalidate?.Invoke();
            }
        }

        if (nextScroll == scroll || revealBounds == null) revealInput = null;
        revealBounds = null;
        if (nextScroll == scroll) return;

        scroll = nextScroll;
        ui.Invalidate?.Invoke();
    }

    private void DrawGeneral(SurfaceUi ui, RectangleF body, ref float y)
    {
        var themes = Row(body, ref y, InputHeight);
        var baseBounds = new RectangleF(x: themes.X + 5, y: themes.Y, width: 108, height: InputHeight);
        var accentBounds = new RectangleF(x: themes.X + 123, y: themes.Y, width: 132, height: InputHeight);
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

        y += GroupGap;
        var checkboxesTop = y;
        var checkboxesBody = BeginGroup(body, ref y);
        DrawCheck(
            ui,
            checkboxesBody,
            ref y,
            "versions",
            "Check for new app versions",
            draft.CheckForNewVersions,
            value => draft.CheckForNewVersions = value,
            height: PreferenceCheckHeight,
            toolTip: "When unchecked, app version checks on startup are disabled."
        );
        DrawCheck(
            ui,
            checkboxesBody,
            ref y,
            "refresherrors",
            "Disable refresh error dialogs",
            draft.DisableRefreshErrorDialogs,
            value => draft.DisableRefreshErrorDialogs = value,
            height: PreferenceCheckHeight,
            toolTip: "When checked, refresh failure dialogs are disabled."
        );
        DrawCheck(
            ui,
            checkboxesBody,
            ref y,
            "traypopup",
            "Disable minimize to tray popup",
            draft.DisableMinimizeToTrayNotification,
            value => draft.DisableMinimizeToTrayNotification = value,
            height: PreferenceCheckHeight,
            toolTip: "When checked, the first minimize-to-tray popup after launch is disabled."
        );
        DrawCheck(
            ui,
            checkboxesBody,
            ref y,
            "output",
            "Hide stream output on load",
            draft.HideStreamOutputMessageBoxOnLoad,
            value => draft.HideStreamOutputMessageBoxOnLoad = value,
            height: PreferenceCheckHeight,
            toolTip: "When checked, the stream output box is hidden after a successful launch."
        );
        EndGroup(ui, body, checkboxesTop, ref y);

        const float OpenFileGap = 4;
        y += OpenFileGap;
        var openBounds = Row(body, ref y, RowHeight);
        const string DocumentSymbol = "\uE8A5";
        ui.Button(id: "settings.openfile", text: "Open settings file", bounds: openBounds, action: () => _ = OpenSettings(), symbol: DocumentSymbol);
    }

    private void DrawStreamNotifications(SurfaceUi ui, RectangleF body, ref float y)
    {
        var minimumTop = y;
        var minimumBody = BeginGroup(body, ref y);
        var label = DrawTextRow(ui, text: "Popular alert threshold (viewers; 0 = off)", minimumBody, ref y, spacing: LabelSpacing);
        const string MinimumToolTip =
            "Minimum number of viewers for a stream to be shown in a notification as a popular or event stream.\n" +
            "This helps you not miss out on special events or popular streams.";

        ui.ToolTip(id: "settings.minimum.label", label, MinimumToolTip);
        var inputBounds = DrawInput(
            ui,
            minimumBody,
            ref y,
            id: MinimumViewersId,
            minimumViewersText,
            placeholder: "",
            MinimumChanged,
            spacing: GroupInputSpacing,
            toolTip: MinimumToolTip);

        if (!validMinimumViewers)
        {
            const string ErrorMessage = "Enter a viewer threshold, or 0 to turn it off.";
            var errorColor = InvalidFieldColor(ui.Palette);
            var error = DrawTextRow(ui: ui, text: ErrorMessage, body: minimumBody, y: ref y, spacing: GroupInputSpacing, color: errorColor);
            MarkInvalidInput(ui: ui, id: MinimumViewersId, inputBounds: inputBounds, statusBounds: error, viewportHeight: body.Height);
        }

        EndGroup(ui, body, minimumTop, ref y);

        var checkboxesTop = y;
        var checkboxesBody = BeginGroup(body, ref y);
        DrawCheck(
            ui,
            checkboxesBody,
            ref y,
            "notifications",
            "Disable notifications",
            draft.DisableNotifications,
            value => draft.DisableNotifications = value,
            height: PreferenceCheckHeight,
            toolTip: "When checked, all notifications are disabled."
        );
        DrawCheck(
            ui,
            checkboxesBody,
            ref y,
            "foregroundonstreamonline",
            "Bring app forward on live alerts",
            draft.BringToForegroundOnStreamOnline,
            value => draft.BringToForegroundOnStreamOnline = value,
            height: PreferenceCheckHeight,
            toolTip: "When a followed stream goes live and the app isn't minimized, bring the app window to the foreground."
        );
        EndGroup(ui, body, checkboxesTop, ref y);

        var notificationTop = y;
        var notificationBody = BeginGroup(body, ref y);
        DrawTextRow(ui, text: "Stream notification image", notificationBody, ref y, spacing: LabelSpacing);
        var image = Row(notificationBody, ref y, PreferenceCheckHeight);
        var channelIcon = image with { Width = image.Width / 2 };
        var liveThumbnail = channelIcon with { X = channelIcon.Right };
        ui.Radio(
            "settings.notificationimage.icon",
            "Channel icon",
            draft.StreamNotificationImage == StreamNotificationImage.ChannelIcon,
            channelIcon,
            _ => draft.StreamNotificationImage = StreamNotificationImage.ChannelIcon,
            group: "settings.notificationimage");
        ui.Radio(
            "settings.notificationimage.thumbnail",
            "Live thumbnail",
            draft.StreamNotificationImage == StreamNotificationImage.LiveThumbnail,
            liveThumbnail,
            _ => draft.StreamNotificationImage = StreamNotificationImage.LiveThumbnail,
            group: "settings.notificationimage");
        y += InputSpacing;

        DrawTextRow(ui, text: "Stream notification popup duration", notificationBody, ref y, spacing: LabelSpacing);
        var duration = Row(notificationBody, ref y, PreferenceCheckHeight);
        var shortBanner = duration with { Width = duration.Width / 2 };
        var longBanner = shortBanner with { X = shortBanner.Right };
        ui.Radio(
            "settings.bannerduration.short",
            "Short",
            !draft.UseLongStreamNotificationBanners,
            shortBanner,
            _ => draft.UseLongStreamNotificationBanners = false,
            group: "settings.bannerduration");
        ui.Radio(
            "settings.bannerduration.long",
            "Long",
            draft.UseLongStreamNotificationBanners,
            longBanner,
            _ => draft.UseLongStreamNotificationBanners = true,
            group: "settings.bannerduration");
        y += GroupInputSpacing;
        EndGroup(ui, body, notificationTop, ref y);

        void MinimumChanged(string value)
        {
            if (value.Length > MaximumViewerDigits || value.Any(character => !char.IsAsciiDigit(character)))
            {
                ui.SetValue(MinimumViewersId, minimumViewersText);
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
            DrawTextRow(ui, text: "Auth token", authBody, ref y, spacing: LabelSpacing);
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
        y += SectionGap;
        DrawCommand(ui, body, ref y, vodId, title: "VOD command", platform.VodCommand, hint);
        y += SectionGap;
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
        DrawTextRow(ui, title, body, ref y, spacing: HeadingSpacing, weight: FontWeight.SemiBold);
        var pathTop = y;
        var pathBody = BeginGroup(body, ref y);
        DrawTextRow(ui, text: "File path", pathBody, ref y, spacing: LabelSpacing);
        var pathId = id + ".path";
        var inputBounds = DrawInput(
            ui,
            pathBody,
            ref y,
            pathId,
            command.FilePath,
            "",
            value => command.FilePath = value,
            spacing: GroupInputSpacing,
            toolTip: "Enter a command name on PATH or a full executable path."
        );

        var state = ResolvePath(command.FilePath);
        var statusColor = state.Valid ? ui.Palette.Text : InvalidFieldColor(ui.Palette);
        const float HintFontSize = 11;
        var status = DrawTextRow(ui, state.Message, pathBody, ref y, spacing: GroupInputSpacing, color: statusColor, size: HintFontSize);
        if (!state.Valid) MarkInvalidInput(ui: ui, id: pathId, inputBounds: inputBounds, statusBounds: status, viewportHeight: body.Height);
        EndGroup(ui, body, pathTop, ref y);

        var argsTop = y;
        var argsBody = BeginGroup(body, ref y);
        DrawTextRow(ui, text: "Arguments", argsBody, ref y, spacing: LabelSpacing);
        DrawTextRow(ui, hint, argsBody, ref y, spacing: StatusSpacing, color: ui.Palette.MutedText, size: HintFontSize);
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
        EndGroup(ui, body, captureTop, ref y, spacing: 0);
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
        ref float y,
        float spacing = GroupGap)
    {
        y += GroupPadding;
        var border = new RectangleF(x: body.X, y: top, width: body.Width - ScrollbarSpace, height: y - top);
        var color = ui.Palette.IsDark
            ? Theme.Blend(ui.Palette.Panel, ui.Palette.Border, GroupBorderOpacity)
            : ui.Palette.Border;

        var thickness = ui.Palette.IsDark ? GroupBorderThickness : 1;
        ui.Border(border, color, thickness);
        y += spacing;
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
        if (expanded[index]) y += GroupGap;
    }

    private RectangleF DrawInput(
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
        return bounds;
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

    private static RectangleF DrawTextRow(
        SurfaceUi ui,
        string text,
        RectangleF body,
        ref float y,
        float spacing,
        uint? color = null,
        float size = TextSize,
        FontWeight weight = FontWeight.Normal)
    {
        var content = body with { Width = body.Width - ScrollbarSpace };
        return ui.TextRow(text, content, ref y, spacing, color, size, weight);
    }

    private void MarkInvalidInput(
        SurfaceUi ui,
        string id,
        RectangleF inputBounds,
        RectangleF statusBounds,
        float viewportHeight)
    {
        ui.Border(RectangleF.Inflate(inputBounds, -1, -1), InvalidFieldColor(ui.Palette));
        if (revealInput != id) return;

        var target = RectangleF.Union(inputBounds, statusBounds);
        revealBounds = target.Height <= viewportHeight ? target : inputBounds;
    }

    private static uint InvalidFieldColor(ThemePalette palette)
    {
        const uint DarkThemeError = 0xFFFFADAD;
        const uint LightThemeError = 0xFFB00020;
        return palette.IsDark ? DarkThemeError : LightThemeError;
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

    private bool HasChanges(bool otherChanges)
    {
        var current = handler.Settings;
        return otherChanges || !validMinimumViewers ||
               draft.BringToForegroundOnStreamOnline != current.BringToForegroundOnStreamOnline ||
               draft.StreamNotificationImage != current.StreamNotificationImage ||
               draft.UseLongStreamNotificationBanners != current.UseLongStreamNotificationBanners ||
               streamQualities.HasChanges;
    }

    private void Save()
    {
        var otherChanges = !EditableMatch(draft, handler.Settings);
        if (!HasChanges(otherChanges) || FindInvalidField(otherChanges) != null) return;

        if (otherChanges) CopyEditable(draft, handler.Settings);
        else
        {
            handler.Settings.BringToForegroundOnStreamOnline = draft.BringToForegroundOnStreamOnline;
            handler.Settings.StreamNotificationImage = draft.StreamNotificationImage;
            handler.Settings.UseLongStreamNotificationBanners = draft.UseLongStreamNotificationBanners;
        }

        streamQualities.SaveChanges();
        handler.SaveSettings();
        if (otherChanges) saved?.Invoke();
    }

    // The settings this pane edits and saves together; the theme applies at once instead. A setting added here is
    // also compared in EditableMatch, except the foreground and banner options, which can be saved without validating playback commands.
    private static void CopyEditable(Settings source, Settings target)
    {
        target.MinimumEventViewers = source.MinimumEventViewers;
        target.CheckForNewVersions = source.CheckForNewVersions;
        target.DisableNotifications = source.DisableNotifications;
        target.BringToForegroundOnStreamOnline = source.BringToForegroundOnStreamOnline;
        target.StreamNotificationImage = source.StreamNotificationImage;
        target.UseLongStreamNotificationBanners = source.UseLongStreamNotificationBanners;
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

    private InvalidField? FindInvalidField(bool otherChanges)
    {
        if (!validMinimumViewers) return new InvalidField(MinimumViewersId, StreamNotificationsSectionIndex);
        if (!otherChanges) return null;

        return FindInvalidPath(draft.Twitch, "Twitch", TwitchSectionIndex) ??
               FindInvalidPath(draft.Kick, "Kick", KickSectionIndex) ??
               FindInvalidPath(draft.YouTube, "YouTube", YouTubeSectionIndex);

        InvalidField? FindInvalidPath(ApiPlatformSettings platform, string name, int sectionIndex)
        {
            if (!ResolvePath(platform.StreamCommand.FilePath).Valid) return new InvalidField(name + ".stream.path", sectionIndex);
            if (!ResolvePath(platform.VodCommand.FilePath).Valid) return new InvalidField(name + ".vod.path", sectionIndex);
            if (!ResolvePath(platform.ChatCommand.FilePath).Valid) return new InvalidField(name + ".chat.path", sectionIndex);

            return null;
        }
    }

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

    private readonly record struct InvalidField(string Id, int SectionIndex);
    private sealed record PathState(bool Valid, string Message);
}
