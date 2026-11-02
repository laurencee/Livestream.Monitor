using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Automation;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Widgets;
using Livestream.Monitor.Direct2D.Windows.Output;
using Livestream.Monitor.Direct2D.Windows.Preview;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

/// <summary> Main app window </summary>
internal sealed class MonitorWindow : CanvasWindow, INotificationHandler
{
    // The WPF toolbar row: controls 26 high with 2-pixel margins below the title bar, over a separator line.
    private const int ToolbarTop = 33;
    private const int ToolbarControlHeight = 26;
    private const int ToolbarBottom = 65;

    // The arrow area at the end of an editable combo box's input, as wide as the WPF toggle.
    private const float DropDownArrowWidth = 20;
    private const int HeaderHeight = 27;
    private const int RowHeight = 25;
    private const int FooterHeight = 36;

    // Footer controls are centred in the footer, leaving the WPF footer's 5-pixel margin above the window border.
    private const int FooterControlTop = 6;
    private const int FooterControlHeight = 24;
    private const int TileWidth = 155;
    private const int TileHeight = 120;

    // As in WPF, each tile has a 3-pixel margin, and the tile pages' content starts under their 32-pixel toolbar row.
    private const int TileMargin = 3;
    private const int TileGap = TileMargin * 2;
    private const int TileRowPitch = TileHeight + TileGap;
    private const int TileAreaTop = 63;
    private const int TilesTop = TileAreaTop + TileMargin;
    private const int TilesLeft = (int)WindowBorderThickness + TileMargin;

    // The WPF data grid's columns, inside the window border: the live column is sized to its header, the name,
    // viewers and uptime columns have fixed widths, and the status and game columns share the rest 5:2.
    private const float TableLeft = WindowBorderThickness;
    private const int LiveWidth = 45;
    private const int NameWidth = 130;
    private const int ViewersWidth = 80;
    private const int UptimeWidth = 85;

    // The tile pages scroll in pixels as WPF's scroll viewer does, 16 per line; the stream list scrolls in rows.
    private const int TileLineSize = 16;
    private const int RowLineSize = 1;

    // Windows' wheel setting, which WPF follows: lines per notch, or a page per notch.
    private const int WheelPageScroll = -1;
    private static readonly int WheelScrollLines = ReadWheelScrollLines();
    private const int SettingsWidth = 300;

    // Typing restarts these delays, so a query starts once the user pauses.
    private const int GameSuggestionDelayMilliseconds = 250;
    private const int VodQueryDelayMilliseconds = 500;
    private static readonly string[] ColumnNames = ["LIVE", "NAME", "STATUS", "GAME", "VIEWERS", "UPTIME"];
    private static readonly string[] SortProperties =
    [
        nameof(LivestreamModel.Live), nameof(LivestreamModel.DisplayName), nameof(LivestreamModel.Description),
        nameof(LivestreamModel.Game), nameof(LivestreamModel.Viewers), nameof(LivestreamModel.Uptime),
    ];
    private static readonly Version? AppVersion = typeof(MonitorWindow).Assembly.GetName().Version;
#if DEBUG
    // Shows that the debug rather than the release build is running, as the WPF title does.
    private const string BuildSuffix = " (DEBUG)";
#else
    private const string BuildSuffix = "";
#endif
    private static readonly string CaptionTitle = "LIVESTREAM MONITOR V" + AppVersion?.ToString(3) + BuildSuffix;

    // WPF bindings format in the element's language, which is en-US unless an application sets another.
    private static readonly CultureInfo WpfBindingCulture = CultureInfo.GetCultureInfo("en-US");
    private readonly NativeDialogs dialogs;
    private readonly MonitorWindowState state;
    private readonly IApiClient[] providers;
    private readonly string[] providerNames;
    private readonly string[] filterNames;
    private readonly NativeStreamLauncher launcher;
    private readonly TrayIcon tray;
    private readonly PopularLivestreamWatcher popularStreams;
    private readonly ImageCache images;
    private readonly VerticalScrollBar scrollBar;
    private ThemePalette palette;
    private SettingsPane? settingsPane;
    private bool confirmingSettingsClose;
    private readonly PanelSlide settingsSlide = new();
    private bool rowsDirty = true;

    // In rows on the stream list and in pixels on the tile pages; one page shows at a time.
    private int scrollOffset;

    private int providerIndex;
    private int vodTypeIndex;
    private LivestreamModel? requestedNotificationStream;
    private PageRequest? requestedPage;
    private long gameSuggestionsDue;
    private long vodQueryDue;
    private string[] gameSuggestions = [];
    private bool queryQueued;
    private PreviewWindow? preview;
    private int hoveredRow = -1;
    private long previewDue;
    private bool firstMinimize = true;
    private int minimizeCount;
    private bool isImporting;

    public MonitorWindow(MonitorWindowState state, NativeDialogs dialogs) : base(CaptionTitle, Program.WindowWidth, Program.WindowHeight)
    {
        // Windows animates minimizing and restoring only windows with a caption; the client area still covers the
        // whole window.
        var windowStyle = Win32.GetWindowLongPtr(Handle, Win32.GwlStyle);
        Win32.SetWindowLongPtr(Handle, Win32.GwlStyle, windowStyle | (nint)Win32.WsCaption);
        var flags = Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoZOrder | Win32.SwpNoActivate | Win32.SwpFrameChanged;
        Win32.SetWindowPos(Handle, insertAfter: 0, x: 0, y: 0, width: 0, height: 0, flags);

        this.dialogs = dialogs;
        this.state = state;
        scrollBar = new VerticalScrollBar(ScrollTo);
        providers = state.ApiClientFactory.GetAll().ToArray();
        providerNames = providers.Select(api => api.ApiName).ToArray();
        filterNames = [FilterModel.AllApiClientsFilterName, .. providerNames];
        palette = Theme.Create(state.SettingsHandler.Settings);
        ThemeSource = () => palette;
        images = new ImageCache(Graphics);
        launcher = new NativeStreamLauncher(
            owner: Handle,
            settingsHandler: state.SettingsHandler,
            dispatcher: Program.Dispatcher,
            theme: ThemeSource
        );

        tray = new TrayIcon(Handle, Restore, Close);
        popularStreams = new PopularLivestreamWatcher(
            settingsHandler: state.SettingsHandler,
            notificationHandler: this,
            openStream: ShowTopStreams,
            apiClientFactory: state.ApiClientFactory
        );

        state.Changed += () =>
        {
            rowsDirty = true;
            NeedsPaint = true;
        };

        state.SettingsHandler.Settings.PropertyChanged +=
            (_, _) => palette = Theme.Create(state.SettingsHandler.Settings);

        state.StreamOnline += NotifyOnline;
        Tick();
        Show();

        // As in WPF, the window is activated when it first shows, so it takes keyboard input at once.
        Win32.SetForegroundWindow(Handle);
        Tick();
    }

    public MonitorWindowState State => state;
    protected override bool IsAnimating =>
        base.IsAnimating || settingsSlide.IsAnimating || state.IsContentLoading && !HasDialog ||
        scrollBar.IsInteracting;
    private IApiClient SelectedProvider => providers[Math.Clamp(providerIndex, 0, providers.Length - 1)];
    private int ItemCount => state.Page == MonitorPage.Vods ? state.Vods.Count : state.Rows.Count;

    protected override void BeforeFrame()
    {
        if (!isImporting) state.Tick();
        var now = Environment.TickCount64;

        if (settingsSlide.Tick(PanelSlide.NowMilliseconds))
        {
            NeedsPaint = true;
            OnPointerMove(Pointer);
        }

        if (settingsSlide is { IsOpen: false, IsAnimating: false }) settingsPane = null;
        if (state.IsContentLoading && !HasDialog) NeedsPaint = true;
        if (scrollBar.Repeat(now)) NeedsPaint = true;
        UpdatePreview(now);

        if (gameSuggestionsDue != 0 && now >= gameSuggestionsDue)
        {
            gameSuggestionsDue = 0;
            _ = FindGames();
        }

        if (vodQueryDue != 0 && now >= vodQueryDue)
        {
            vodQueryDue = 0;
            queryQueued = true;
        }

        if (queryQueued && !state.Busy)
        {
            queryQueued = false;
            state.PageNumber = 0;
            _ = Query();
        }

        if (requestedPage is { } request)
        {
            requestedPage = null;
            providerIndex = request.ProviderIndex;
            SwitchPage(request.Page);
        }

        if (!state.Busy && requestedNotificationStream is { } stream)
        {
            requestedNotificationStream = null;
            Ui.SetValue("filter", "");
            state.Filter = "";
            state.ProviderFilter = FilterModel.AllApiClientsFilterName;
            state.OnlineOnly = false;
            SwitchPage(MonitorPage.Followed);
            state.RebuildRows();
            state.SelectedIndex = state.Rows.IndexOf(stream);
            KeepSelectionVisible();
        }

        if (!rowsDirty) return;

        state.RebuildRows();
        rowsDirty = false;
    }

    protected override RectangleF? CoveredArea(SizeF size) => settingsPane == null ? null : SettingsBounds(size);
    private RectangleF SettingsBounds(SizeF size)
    {
        var left = size.Width - SettingsWidth * settingsSlide.VisibleFraction;
        return new RectangleF(x: left, y: 0, width: SettingsWidth, height: size.Height);
    }

    protected override bool IsCaption(PointF point) =>
        base.IsCaption(point) && (settingsPane == null || point.X < SettingsBounds(ClientSize).Left);

    protected override void Draw(SizeF size)
    {
        DrawCaption(CaptionTitle, size.Width, minimize: true);
        var topBounds = new RectangleF(x: size.Width - 293, y: 0, width: 84, height: TitleHeight);
        Ui.Button(
            "top",
            "Top Streams",
            topBounds,
            () => SwitchPage(MonitorPage.TopStreams),
            chrome: true,
            toolTip: "Show top streams from twitch"
        );
        var vodBounds = new RectangleF(x: size.Width - 208, y: 0, width: 78, height: TitleHeight);
        Ui.Button(
            "vod",
            "Vod Viewer",
            vodBounds,
            () => SwitchPage(MonitorPage.Vods),
            chrome: true,
            toolTip: "View and open vods from twitch streams"
        );
        var settingsBounds = new RectangleF(x: size.Width - 129, y: 0, width: 35, height: TitleHeight);
        Ui.Button(
            id: "settings",
            text: "⚙",
            bounds: settingsBounds,
            action: ToggleSettings,
            chrome: true,
            name: "Settings"
        );

        DrawCommandSeparator(size.Width - 209);
        DrawCommandSeparator(size.Width - 130);
        DrawCommandSeparator(size.Width - 94);

        if (state.Page == MonitorPage.Followed)
        {
            DrawFollowedToolbar(size.Width);
            DrawGrid(size);
        }
        else
        {
            DrawPageToolbar(size.Width);
            DrawTiles(size);
            DrawFooter(size);
        }

        if (state.Busy) Ui.Fill(new RectangleF(x: 1, y: TitleHeight, width: size.Width - 2, height: 2), palette.Accent);
        if (settingsPane == null) return;

        Ui.Overlay();
        settingsPane.Draw(Ui, SettingsBounds(size));
    }

    private void DrawFollowedToolbar(float width)
    {
        // Control positions follow the WPF toolbar, whose controls sit 2 pixels apart after the bold filter label.
        const int Y = ToolbarTop;
        const int Height = ToolbarControlHeight;
        Ui.Text("Filter:", new RectangleF(x: 13, y: Y, width: 36, height: Height), weight: FontWeight.Bold);
        var filterBounds = new RectangleF(x: 50, y: Y, width: 100, height: Height);
        Ui.Input(
            "filter",
            "name...",
            filterBounds,
            value =>
            {
                state.Filter = value;
                FilterChanged();
            },
            clear: true
        );
        var filterIndex = Array.IndexOf(filterNames, state.ProviderFilter);
        var filterProviderBounds = new RectangleF(x: 154, y: Y, width: 47, height: Height);
        Ui.Combo(
            "filter.provider",
            state.ProviderFilter,
            filterNames,
            filterIndex,
            filterProviderBounds,
            index =>
            {
                state.ProviderFilter = filterNames[index];
                FilterChanged();
            }
        );
        var onlineBounds = new RectangleF(x: 205, y: Y, width: 24, height: Height);
        Ui.Button(
            "filter.online",
            "",
            onlineBounds,
            () =>
            {
                state.OnlineOnly = !state.OnlineOnly;
                FilterChanged();
            },
            toolTip: "Toggle show online streams only",
            name: "Online only"
        );
        var onlineImage = images.GetEmbedded(state.OnlineOnly ? EmbeddedImages.Live : EmbeddedImages.Offline);
        const float OnlineImageSize = 13;
        var onlineImageBounds = new RectangleF(
            x: onlineBounds.X + (onlineBounds.Width - OnlineImageSize) / 2,
            y: Y + (Height - OnlineImageSize) / 2,
            width: OnlineImageSize,
            height: OnlineImageSize);

        Ui.Image(onlineImage, onlineImageBounds);
        Separator(237);
        var chatBounds = new RectangleF(x: 245, y: Y, width: 50, height: Height);
        Ui.Button(
            "chat",
            "CHAT",
            chatBounds,
            () => _ = OpenChat(state.SelectedStream),
            CanOpenChat(state.SelectedStream)
        );
        var refreshBounds = new RectangleF(x: 299, y: Y, width: 21, height: Height);
        var canRefresh = state is { Busy: false, StreamsModel.CanRefreshLivestreams: true };
        Ui.Button(
            id: "refresh",
            text: "↻",
            refreshBounds,
            action: () => _ = Query(),
            enabled: canRefresh,
            toolTip: "Refresh Livestreams",
            name: "Refresh"
        );
        Separator(328);
        var extra = Math.Max(0, width - Program.WindowWidth);
        var channelBounds = new RectangleF(x: 336, y: Y, width: 150 + extra, height: Height);
        Ui.Input(id: "channel", placeholder: "add new livestream...", channelBounds, enter: () => _ = AddChannel());
        var providerName = SelectedProvider.ApiName;
        var providerBounds = new RectangleF(x: 490 + extra, y: Y, width: 80, height: Height);
        Ui.Combo(
            "provider",
            providerName,
            providerNames,
            providerIndex,
            providerBounds,
            SelectProvider,
            !state.Busy
        );
        var addBounds = new RectangleF(x: 574 + extra, y: Y, width: 19, height: Height);
        var canAdd = !state.Busy && !string.IsNullOrWhiteSpace(Ui.Value("channel"));
        Ui.Button(
            id: "add",
            text: "+",
            addBounds,
            action: () => _ = AddChannel(),
            enabled: canAdd,
            toolTip: "Add new livestream",
            name: "Add"
        );
        var importBounds = new RectangleF(x: 597 + extra, y: Y, width: 50, height: Height);
        Ui.Button(
            "import",
            "Import",
            importBounds,
            () => _ = Import(),
            !state.Busy,
            toolTip: "Import livestreams from the provided username"
        );
    }

    // The WPF toolbar separators are as high as the toolbar controls.
    private void Separator(float x)
    {
        var bounds = new RectangleF(x: x, y: ToolbarTop, width: 1, height: ToolbarControlHeight);
        Ui.Fill(bounds, palette.Border);
    }

    // The WPF window commands are separated by short translucent lines, including one before the caption buttons.
    private void DrawCommandSeparator(float x)
    {
        const float Top = 8;
        const float Height = 14;
        const float Opacity = 0.25f;
        var bounds = new RectangleF(x: x, y: Top, width: 1, height: Height);
        Ui.Fill(bounds, Theme.Blend(Ui.TitleBarColor, palette.TitleText, Opacity));
    }

    private void DrawPageToolbar(float width)
    {
        // Control positions follow the WPF page headers, whose controls have 3-pixel margins; the VOD header's own
        // margin is 5 pixels narrower than the top streams header's.
        const int Y = ToolbarTop;
        const int Height = ToolbarControlHeight;
        var left = state.Page == MonitorPage.Vods ? 9 : 14;

        // The WPF back button glyph is a 6-pixel triangle; the font this frontend falls back to draws it larger.
        const float BackGlyphSize = 7;
        var backBounds = new RectangleF(x: left, y: Y, width: 25, height: Height);
        Ui.Button(
            id: "back",
            text: "◀",
            backBounds,
            action: () => SwitchPage(MonitorPage.Followed),
            size: BackGlyphSize,
            toolTip: "Go back to livestream list",
            name: "Back"
        );
        var providerName = SelectedProvider.ApiName;
        var providerBounds = new RectangleF(x: left + 31, y: Y, width: 80, height: Height);
        Ui.Combo(
            "provider",
            providerName,
            providerNames,
            providerIndex,
            providerBounds,
            SelectProvider,
            !state.Busy
        );

        if (state.Page == MonitorPage.TopStreams)
        {
            Ui.Text("Game Filter", new RectangleF(x: 133, y: Y, width: 63, height: Height));
            var gameBounds = new RectangleF(x: 201, y: Y, width: 175, height: Height);
            Ui.Input(
                "game",
                "Partial game name...",
                gameBounds,
                _ => gameSuggestionsDue = Environment.TickCount64 + GameSuggestionDelayMilliseconds,
                () =>
                {
                    Ui.CloseMenu();
                    queryQueued = true;
                },
                SelectedProvider.HasTopStreamGameFilterSupport,
                clear: true,
                trailing: DropDownArrowWidth
            );
            var choicesBounds = gameBounds with { X = gameBounds.Right - DropDownArrowWidth, Width = DropDownArrowWidth };
            Ui.DropDownButton(
                id: "game.choices",
                bounds: choicesBounds,
                frame: gameBounds,
                action: () => _ = FindGames(),
                menuId: "game.suggestions",
                enabled: SelectedProvider.HasTopStreamGameFilterSupport,
                name: "Find games"
            );

            // One tooltip covers the input and its choices button, as it covers the whole WPF combo box.
            if (SelectedProvider.HasTopStreamGameFilterSupport)
            {
                const string GameToolTip = "Filter to show only streams from a particular game. You can type a " +
                                           "partial game name to search for any matching game names from twitch.";

                Ui.ToolTip(id: "game", gameBounds, GameToolTip);
            }

            var refreshBounds = new RectangleF(x: 381, y: Y, width: 22, height: Height);
            Ui.Button(
                id: "refresh",
                text: "↻",
                refreshBounds,
                action: () => _ = Query(),
                enabled: !state.Busy,
                toolTip: "Refresh Top Streams",
                name: "Refresh"
            );
            return;
        }

        Ui.Text("Stream", new RectangleF(x: 129, y: Y, width: 40, height: Height));
        var channelBounds = new RectangleF(x: 173, y: Y, width: 140, height: Height);
        Ui.Input(
            "vod.channel",
            "streamid...",
            channelBounds,
            _ => vodQueryDue = Environment.TickCount64 + VodQueryDelayMilliseconds,
            () =>
            {
                vodQueryDue = 0;
                queryQueued = true;
            },
            trailing: DropDownArrowWidth
        );
        var channelsBounds = channelBounds with { X = channelBounds.Right - DropDownArrowWidth, Width = DropDownArrowWidth };
        Ui.DropDownButton(
            id: "vod.channels",
            bounds: channelsBounds,
            frame: channelBounds,
            action: ShowKnownStreams,
            menuId: "vod.known",
            name: "Known channels"
        );
        var types = SelectedProvider.VodTypes;
        var type = types.Count == 0 ? "" : types[Math.Clamp(vodTypeIndex, 0, types.Count - 1)];
        var typeBounds = new RectangleF(x: 319, y: Y, width: 100, height: Height);
        Ui.Combo(
            "vod.type",
            type,
            types,
            vodTypeIndex,
            typeBounds,
            index =>
            {
                vodTypeIndex = index;
                state.PageNumber = 0;
                _ = Query();
            },
            !state.Busy && types.Count != 0
        );
        Ui.Text("Vod URL", new RectangleF(x: 427, y: Y, width: 48, height: Height));
        var urlBounds = new RectangleF(x: 480, y: Y, width: Math.Max(80, width - 566), height: Height);
        const string UrlToolTip =
            "Any valid twitch vod URL can be placed in this textbox, not just urls from vod tiles.";

        Ui.Input("vod.url", placeholder: "", urlBounds, toolTip: UrlToolTip, name: "Vod URL");

        // Wider than the WPF button, whose bold upper-case text is narrower than this text.
        var openBounds = new RectangleF(x: width - 79, y: Y, width: 70, height: Height);
        var canOpen = IsVodUrl(Ui.Value("vod.url"));
        Ui.Button(
            id: "vod.open",
            text: "Open VOD",
            openBounds,
            action: () => _ = OpenVodUrl(),
            enabled: canOpen,
            toolTip: "Opens up the VOD through livestreamer/streamlink (with HLS passthrough to allow seeking)"
        );
    }

    private static float[] ColumnStarts(float width)
    {
        const int MinimumFlexibleWidth = 175;
        var fixedWidth = TableLeft + LiveWidth + NameWidth + ViewersWidth + UptimeWidth;
        var flexible = Math.Max(MinimumFlexibleWidth, width - fixedWidth);
        var statusWidth = flexible * 5 / 7;
        var nameLeft = TableLeft + LiveWidth;
        var statusLeft = nameLeft + NameWidth;
        var viewersLeft = width - ViewersWidth - UptimeWidth;
        return [TableLeft, nameLeft, statusLeft, statusLeft + statusWidth, viewersLeft, width - UptimeWidth, width];
    }

    private void DrawGrid(SizeF size)
    {
        // The header matches the WPF data grid: upper-case bold labels over the window background, between a line
        // above and a rule below.
        const float HeaderRuleHeight = 3;
        const float HeaderFontSize = 12;
        var top = ToolbarBottom + HeaderHeight;
        var visible = VisibleRows(size);
        var lastOffset = Math.Max(0, state.Rows.Count - visible);
        if (!state.IsContentLoading) scrollOffset = Math.Clamp(scrollOffset, 0, lastOffset);

        // As in WPF, the bar runs from the header to the window border, and the columns end at its left edge.
        var barBounds = new RectangleF(
            x: size.Width - WindowBorderThickness - VerticalScrollBar.Width,
            y: top,
            width: VerticalScrollBar.Width,
            height: size.Height - WindowBorderThickness - top);

        var rows = state.IsContentLoading ? 0 : state.Rows.Count;
        scrollBar.Update(barBounds, extent: rows, viewport: visible, scrollOffset);
        var tableWidth = scrollBar.IsVisible ? barBounds.Left : size.Width;
        var starts = ColumnStarts(tableWidth);
        var ruleBounds = new RectangleF(
            x: 0,
            y: ToolbarBottom + HeaderHeight - HeaderRuleHeight,
            width: tableWidth,
            height: HeaderRuleHeight);

        Ui.Fill(ruleBounds, palette.HeaderRule);

        // Separates the table from the toolbar above it.
        var tableTop = new RectangleF(x: 0, y: ToolbarBottom - 1, width: size.Width, height: 1);
        Ui.Fill(tableTop, palette.TableBorder);

        for (var i = 0; i < ColumnNames.Length; i++)
        {
            var column = i;
            var bounds = new RectangleF(
                x: starts[i],
                y: ToolbarBottom,
                width: starts[i + 1] - starts[i],
                height: HeaderHeight - HeaderRuleHeight
            );

            if (Ui.IsPointerOver(bounds)) Ui.Fill(bounds, palette.Hover);

            // The WPF column headers inset their label 10 pixels, as their cells inset their text 5.
            const float HeaderLabelInset = 10;
            var sort = state.SettingsHandler.Settings.LivestreamListSortState;
            var labelBounds = bounds with { X = bounds.X + HeaderLabelInset, Width = bounds.Width - HeaderLabelInset };
            Ui.Label(ColumnNames[i], labelBounds, size: HeaderFontSize, weight: FontWeight.Bold);

            if (sort?.Column == SortProperties[i])
            {
                var labelWidth = MeasureWidth(ColumnNames[i], HeaderFontSize, FontWeight.Bold);
                var ascending = sort.SortDirection == ListSortDirection.Ascending;
                DrawSortArrow(labelBounds.X + labelWidth, bounds.Y, ascending);
            }

            Ui.Hit(id: "column." + i, bounds: bounds, action: () => Sort(column), name: ColumnNames[i]);

            // As in WPF, the separator spans the header and its rule.
            var separator = new RectangleF(x: bounds.Right - 1, y: bounds.Y, width: 1, height: HeaderHeight);
            Ui.Fill(separator, palette.HeaderRule);
        }

        if (state.IsContentLoading)
        {
            var loadingBounds = new RectangleF(x: 0, y: top, width: size.Width, height: size.Height - top);
            Ui.Spinner(loadingBounds, PanelSlide.NowMilliseconds);
            return;
        }

        Ui.PushClip(new RectangleF(x: 0, y: top, width: tableWidth, height: Math.Max(0, size.Height - top)));

        // As in WPF, the live image is centred in its column.
        const float LiveImageSize = 15;
        var liveLeft = starts[0] + (LiveWidth - LiveImageSize) / 2;

        // As in WPF, the selected row fades to the 40% accent once another control takes keyboard focus or the
        // window is no longer active.
        var selectionActive = Ui is { IsWindowActive: true, HasFocusedWidget: false };

        for (var i = scrollOffset; i < state.Rows.Count && i - scrollOffset <= visible; i++)
        {
            var stream = state.Rows[i];
            var y = top + (i - scrollOffset) * RowHeight;
            var bounds = new RectangleF(x: 0, y: y, width: tableWidth, height: RowHeight);
            var selected = i == state.SelectedIndex;
            var rowStates = selected ? AutomationStates.Selected : AutomationStates.None;
            var rowId = $"row.{i}";
            Ui.BeginGroup(rowId, AutomationRole.DataItem, stream.DisplayName, bounds, rowStates);

            // As in WPF, the pointer's row shows the hover colour whether or not it is selected.
            var highlighted = selected && selectionActive;
            if (Ui.IsPointerOver(bounds)) Ui.Fill(bounds, palette.RowHover);
            else if (selected) Ui.Fill(bounds, highlighted ? palette.Selection : palette.Tile);
            var color = highlighted ? palette.SelectionText : palette.Text;
            var liveImage = images.GetEmbedded(stream.Live ? EmbeddedImages.Live : EmbeddedImages.Offline);
            Ui.Image(liveImage, new RectangleF(x: liveLeft, y: y + 5, width: LiveImageSize, height: LiveImageSize));
            Cell(column: 1, stream.DisplayName);
            Cell(column: 2, stream.Description ?? "");
            Cell(column: 3, stream.Game ?? "");
            Cell(column: 4, stream.Viewers.ToString(CultureInfo.CurrentCulture));
            Cell(column: 5, state.GetDisplayedUptime(stream));
            Ui.EndGroup();

            void Cell(int column, string text)
            {
                const float CellPadding = 5;
                var cell = new RectangleF(
                    x: starts[column] + CellPadding,
                    y: y,
                    width: starts[column + 1] - starts[column] - CellPadding * 2,
                    height: RowHeight
                );

                // WPF data grid cells clip their text instead of trimming it with an ellipsis.
                Ui.Text(text, cell, color, trim: false);
            }
        }

        Ui.PopClip();
        scrollBar.Draw(Ui);
    }

    // The WPF data grid's sort arrow: a grey stem and head after the header label, measured from the WPF frontend.
    private void DrawSortArrow(float labelRight, float headerTop, bool ascending)
    {
        const uint SortArrowGray = 0xFF7F7F7F;
        const float Spacing = 4.5f;
        const float TopOffset = 10;
        const float Width = 7.5f;
        const float Height = 10;
        const float StemWidth = 2.1f;
        const float HeadHeight = 3.6f;
        var left = labelRight + Spacing;
        var top = headerTop + TopOffset;
        var stemTop = ascending ? top + HeadHeight : top;
        var headTop = ascending ? top : top + Height - HeadHeight;
        var stemLeft = left + (Width - StemWidth) / 2;
        var stem = new RectangleF(x: stemLeft, y: stemTop, width: StemWidth, height: Height - HeadHeight);
        var head = new RectangleF(x: left, y: headTop, width: Width, height: HeadHeight);
        Ui.Fill(stem, SortArrowGray);
        Ui.Triangle(head, SortArrowGray, up: ascending);
    }

    /// <summary>
    /// The tile grid for the window size. As in a WPF scroll viewer whose scroll bar shows only when needed, tiles
    /// that do not fit are laid out again beside the scroll bar.
    /// </summary>
    private TileLayout LayoutTiles(SizeF size)
    {
        var viewport = Math.Max(0, (int)(size.Height - TileAreaTop - FooterHeight));
        var width = size.Width;
        var columns = Columns(width);
        var extent = Extent(columns);

        if (extent > viewport)
        {
            width = size.Width - WindowBorderThickness - VerticalScrollBar.Width;
            columns = Columns(width);
            extent = Extent(columns);
        }

        return new TileLayout(columns, width, viewport, extent);

        static int Columns(float available) =>
            Math.Max(1, (int)(available - WindowBorderThickness * 2) / (TileWidth + TileGap));
        int Extent(int perRow) => (ItemCount + perRow - 1) / perRow * TileRowPitch;
    }

    private void DrawTiles(SizeF size)
    {
        // A queued query has not started yet, but its page shows the loader rather than stale results.
        if (state.IsContentLoading || queryQueued)
        {
            var loadingBounds = new RectangleF(
                x: 0,
                y: TileAreaTop,
                width: size.Width,
                height: size.Height - TileAreaTop - FooterHeight);

            scrollBar.Update(RectangleF.Empty, extent: 0, viewport: 1, offset: 0);
            Ui.Spinner(loadingBounds, PanelSlide.NowMilliseconds);
            return;
        }

        var layout = LayoutTiles(size);
        var columns = layout.Columns;
        scrollOffset = Math.Clamp(scrollOffset, 0, Math.Max(0, layout.Extent - layout.Viewport));
        var barBounds = new RectangleF(
            x: size.Width - WindowBorderThickness - VerticalScrollBar.Width,
            y: TileAreaTop,
            width: VerticalScrollBar.Width,
            height: layout.Viewport);

        scrollBar.Update(barBounds, layout.Extent, layout.Viewport, scrollOffset, TileLineSize);
        var clipBounds = new RectangleF(x: 0, y: TileAreaTop, width: layout.Width, height: layout.Viewport);
        Ui.PushClip(clipBounds);

        for (var i = scrollOffset / TileRowPitch * columns; i < ItemCount; i++)
        {
            var x = TilesLeft + i % columns * (TileWidth + TileGap);
            var y = TilesTop + i / columns * TileRowPitch - scrollOffset;
            if (y >= TileAreaTop + layout.Viewport) break;

            var bounds = new RectangleF(x: x, y: y, width: TileWidth, height: TileHeight);
            var stream = state.Page == MonitorPage.TopStreams ? state.Rows[i] : null;
            var tileName = stream != null ? stream.DisplayName : state.Vods[i].Title ?? "";
            var tileStates = i == state.SelectedIndex ? AutomationStates.Selected : AutomationStates.None;
            var tileId = $"tile.{i}";
            Ui.BeginGroup(tileId, AutomationRole.ListItem, tileName, bounds, tileStates);
            var following = stream != null && state.IsFollowed(stream);
            var hovered = Ui.IsPointerOver(bounds);
            var background = hovered ? palette.Highlight : following ? palette.Selection : palette.Tile;
            var color = hovered || following ? palette.AccentText : palette.Text;
            Ui.Fill(bounds, background);
            if (i == state.SelectedIndex) Ui.Border(bounds, palette.Accent, 2);

            if (stream != null)
            {
                // As in WPF, a 130-pixel-wide stack of 16-pixel text lines is centred in the tile, with the flag at
                // its top right corner and a blank line between the game and the viewer count.
                const float ContentWidth = 130;
                const float ContentLeft = (TileWidth - ContentWidth) / 2;
                const float ContentTop = 12;
                const float LineHeight = 16;
                const float FlagWidth = 16;
                const float FlagHeight = 11;
                var contentLeft = x + ContentLeft;
                var nameBounds = new RectangleF(x: contentLeft, y: y + ContentTop, width: 110, height: LineHeight);
                Ui.Text(stream.DisplayName, nameBounds, color);
                var descriptionBounds = nameBounds with { Y = nameBounds.Y + LineHeight, Width = ContentWidth };
                Ui.Text(stream.Description ?? "", descriptionBounds, color);
                Ui.ToolTip(id: $"tile.{i}.description", descriptionBounds, stream.Description);
                var gameBounds = descriptionBounds with { Y = descriptionBounds.Y + LineHeight };
                Ui.Text(stream.Game ?? "", gameBounds, color);
                Ui.ToolTip(id: $"tile.{i}.game", gameBounds, stream.Game);
                var viewers = "Viewers: " + stream.Viewers.ToString(CultureInfo.CurrentCulture);
                var viewersBounds = gameBounds with { Y = gameBounds.Y + LineHeight * 2 };
                Ui.Text(viewers, viewersBounds, color);

                if (following)
                {
                    var followingBounds = viewersBounds with { Y = viewersBounds.Y + LineHeight };
                    Ui.Text(
                        "FOLLOWING",
                        followingBounds,
                        color,
                        weight: FontWeight.Bold,
                        alignment: TextAlign.Center
                    );
                }

                var flag = images.GetFlag(stream.BroadcasterLanguage);
                var flagLeft = contentLeft + ContentWidth - FlagWidth;
                var flagBounds = new RectangleF(x: flagLeft, y: y + ContentTop, width: FlagWidth, height: FlagHeight);
                Ui.Image(flag, flagBounds);
                var language = "Broadcast Language: " + stream.BroadcasterLanguage;
                if (flag != null) Ui.ToolTip(id: $"tile.{i}.flag", flagBounds, language);
            }
            else
            {
                var vod = state.Vods[i];
                var title = vod.Title ?? "";
                var titleBounds = new RectangleF(x: x + 5, y: y + 3, width: 145, height: 34);
                Ui.Text(title, titleBounds, color, weight: FontWeight.SemiBold, wrap: true);
                Ui.ToolTip(id: $"tile.{i}.title", titleBounds, title);
                var description = vod.Description ?? "";
                var descriptionBounds = new RectangleF(x: x + 5, y: y + 40, width: 145, height: 26);
                Ui.Text(description, descriptionBounds, color, size: 11, wrap: true);
                Ui.ToolTip(id: $"tile.{i}.description", descriptionBounds, description);
                var game = vod.Game ?? "";
                var gameBounds = new RectangleF(x: x + 5, y: y + 68, width: 145, height: 18);
                Ui.Text(game, gameBounds, color, size: 11);
                var length = vod.IsUpcoming ? "Upcoming" : vod.Length.ToTotalHoursText();
                var lengthBounds = new RectangleF(x: x + 5, y: y + 84, width: 145, height: 16);
                Ui.Text(length, lengthBounds, color, size: 11);
                var recordedAt = vod.RecordedAt.ToString("MMM dd, yyyy", WpfBindingCulture);
                var recordedBounds = new RectangleF(x: x + 5, y: y + 101, width: 145, height: 16);
                Ui.Text(recordedAt, recordedBounds, color, size: 11);
            }

            Ui.EndGroup();
        }

        Ui.PopClip();
        scrollBar.Draw(Ui);
    }

    private void DrawFooter(SizeF size)
    {
        var y = size.Height - FooterHeight;
        var status = state.Status;
        var statusBounds = new RectangleF(x: 10, y: y, width: size.Width - 270, height: FooterHeight);
        Ui.Text(status, statusBounds, palette.MutedText, size: 11);
        Ui.Text("Items:", new RectangleF(x: size.Width - 260, y: y, width: 42, height: FooterHeight));
        int[] counts = [15, 30, 60];
        var control = new RectangleF(x: 0, y: y + FooterControlTop, width: 0, height: FooterControlHeight);
        var pageSizeBounds = control with { X = size.Width - 217, Width = 53 };
        Ui.Combo(
            "page.size",
            state.PageSize.ToString(),
            ["15", "30", "60"],
            Array.IndexOf(counts, state.PageSize),
            pageSizeBounds,
            index =>
            {
                state.PageSize = counts[index];
                state.PageNumber = 0;
                _ = Query();
            },
            !state.Busy
        );
        var previousBounds = control with { X = size.Width - 153, Width = 25 };
        Ui.Button(
            "page.previous",
            "◀",
            previousBounds,
            () => ChangePage(-1),
            state is { Busy: false, PageNumber: > 0 },
            name: "Previous page"
        );
        var page = "Page " + (state.PageNumber + 1);
        var pageBounds = new RectangleF(x: size.Width - 123, y: y, width: 80, height: FooterHeight);
        Ui.Text(page, pageBounds, alignment: TextAlign.Center);
        var nextBounds = control with { X = size.Width - 35, Width = 25 };
        Ui.Button(
            "page.next",
            "▶",
            nextBounds,
            () => ChangePage(1),
            state is { Busy: false, HasNextPage: true },
            name: "Next page"
        );
    }

    private void ToggleSettings()
    {
        if (settingsSlide.IsOpen)
        {
            CloseSettings();
            return;
        }

        Ui.CloseMenu();
        preview?.Hide();
        settingsPane ??= new SettingsPane(
            handler: state.SettingsHandler,
            dialogs: dialogs,
            close: CloseSettings,
            saved: () => _ = ApplySettings()
        );

        settingsSlide.SetOpen(true, PanelSlide.NowMilliseconds);
        NeedsPaint = true;
    }

    private void CloseSettings() => _ = ConfirmSettingsClose();

    private async Task ConfirmSettingsClose()
    {
        if (confirmingSettingsClose) return;

        confirmingSettingsClose = true;
        try
        {
            if (settingsPane is { HasQualityChanges: true })
            {
                var discard = await dialogs.Confirm(
                    title: "Unsaved quality changes",
                    message: "Discard quality changes and close Settings?",
                    affirmative: "Discard",
                    negative: "Keep editing");
                if (!discard) return;
            }

            Ui.CloseMenu();
            settingsPane?.DiscardQualityChanges(Ui);
            settingsSlide.SetOpen(false, PanelSlide.NowMilliseconds);
            NeedsPaint = true;
        }
        finally
        {
            confirmingSettingsClose = false;
        }
    }

    private async Task ApplySettings()
    {
        try
        {
            await state.ApiClientFactory.Get<TwitchApiClient>().Initialize();
        }
        catch (Exception exception)
        {
            await dialogs.ShowMessage("Unable to apply Twitch settings", exception.ExtractErrorMessage());
        }
    }

    private void FilterChanged()
    {
        rowsDirty = true;
        scrollOffset = 0;
        NeedsPaint = true;
    }

    private void SelectProvider(int index)
    {
        providerIndex = index;
        vodTypeIndex = 0;
        state.PageNumber = 0;
        if (state.Page != MonitorPage.Followed) _ = Query();
    }

    private void Sort(int column)
    {
        var settings = state.SettingsHandler.Settings;
        var old = settings.LivestreamListSortState;

        // As in WPF, a column sorts ascending first and then alternates.
        var descending = old?.Column == SortProperties[column] && old.SortDirection == ListSortDirection.Ascending;
        settings.LivestreamListSortState = new DataGridSortState
        {
            Column = SortProperties[column],
            SortDirection = descending ? ListSortDirection.Descending : ListSortDirection.Ascending,
        };

        state.SettingsHandler.SaveSettings();
        rowsDirty = true;
    }

    private void SwitchPage(MonitorPage page)
    {
        Ui.CloseMenu();
        preview?.Hide();
        hoveredRow = -1;

        // As in WPF, where the page is created anew each time, the VOD page starts afresh once another page is shown.
        if (state.Page == MonitorPage.Vods && page != MonitorPage.Vods)
        {
            Ui.SetValue("vod.channel", "");
            Ui.SetValue("vod.url", "");
            vodTypeIndex = 0;
            state.Vods.Clear();
        }

        // Cleared after the reset, whose cleared inputs schedule queries of their own.
        gameSuggestionsDue = 0;
        vodQueryDue = 0;

        state.Page = page;
        state.PageNumber = 0;
        state.SelectedIndex = -1;
        scrollOffset = 0;
        rowsDirty = true;

        // As in WPF, the page changes at once; its query waits for any running one, such as the startup load.
        queryQueued = page != MonitorPage.Followed;
        NeedsPaint = true;
    }

    private void ChangePage(int delta)
    {
        if (state.Busy || delta < 0 && state.PageNumber == 0 || delta > 0 && !state.HasNextPage) return;

        state.PageNumber += delta;
        scrollOffset = 0;
        _ = Query();
    }

    private Task Query() => state.Page switch
    {
        MonitorPage.TopStreams => state.QueryTop(SelectedProvider, Ui.Value("game")),
        MonitorPage.Vods => state.QueryVods(
            SelectedProvider,
            Ui.Value("vod.channel"),
            SelectedProvider.VodTypes.ElementAtOrDefault(vodTypeIndex) ?? ""
        ),
        _ => state.Refresh(),
    };

    private async Task AddChannel()
    {
        // As in WPF, the name stays after a failure so it can be corrected and added again.
        if (await state.Add(Ui.Value("channel"), SelectedProvider)) Ui.SetValue("channel", "");
    }

    private async Task Import()
    {
        if (isImporting) return;

        var provider = SelectedProvider;

        // Shown before importing starts, so automatic refreshes continue while it is open.
        if (!provider.HasFollowedChannelsQuerySupport)
        {
            var unsupported = $"{provider.ApiName} does not have support for importing followed streams";
            await dialogs.ShowMessage("No import support", unsupported);
            return;
        }

        isImporting = true;
        try
        {
            var prompt = $"Enter username for importing followed streams from '{provider.ApiName}'";
            var name = await dialogs.Prompt("Import Streams", prompt, "Import");
            await state.Import(name, provider);
        }
        finally
        {
            isImporting = false;
        }
    }

    private async Task FindGames()
    {
        if (state.Page != MonitorPage.TopStreams || !SelectedProvider.HasTopStreamGameFilterSupport) return;

        var provider = SelectedProvider;
        var filter = Ui.Value("game");
        try
        {
            var games = await provider.GetKnownGameNames(filter);

            if (IsClosed || HasDialog || state.Page != MonitorPage.TopStreams ||
                SelectedProvider != provider || Ui.Value("game") != filter)
            {
                return;
            }

            gameSuggestions = games.Select(game => game.GameName).ToArray();
            if (gameSuggestions.Length == 0) return;

            // Below the game filter input and its choices button.
            Ui.DropDown(
                "game.suggestions",
                new RectangleF(x: 201, y: ToolbarTop, width: 175, height: ToolbarControlHeight),
                gameSuggestions,
                index =>
                {
                    Ui.SetValue("game", gameSuggestions[index]);
                    gameSuggestionsDue = 0;
                    queryQueued = true;
                }
            );
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Search suggestions are optional; typed game filters remain usable during provider outages.
        }
    }

    private void ShowKnownStreams()
    {
        var names = state.StreamsModel.Livestreams.Where(stream => stream.ApiClient == SelectedProvider)
            .Select(stream => stream.DisplayName).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        if (names.Length == 0) return;

        // Below the stream name input and its known channels button.
        Ui.DropDown(
            "vod.known",
            new RectangleF(x: 173, y: ToolbarTop, width: 140, height: ToolbarControlHeight),
            names,
            index =>
            {
                Ui.SetValue("vod.channel", names[index]);
                vodQueryDue = 0;
                queryQueued = true;
            }
        );
    }

    private async Task OpenVodUrl()
    {
        var url = Ui.Value("vod.url").Trim();
        if (!IsVodUrl(url)) return;

        try
        {
            var index = state.SelectedIndex;
            var selected = index >= 0 && index < state.Vods.Count ? state.Vods[index] : null;

            // As in WPF, the selected tile's VOD opens when the box holds its URL, so the output window shows its title. A
            // typed URL names its window, where WPF leaves the title blank.
            var vod = selected != null && selected.Url == url
                ? selected
                : new VodDetails { Url = url, Title = url, ApiClient = SelectedProvider };

            launcher.OpenVod(vod);
        }
        catch (Exception exception)
        {
            await ShowVodLaunchError(exception);
        }
    }

    private static bool IsVodUrl(string url) => Uri.IsWellFormedUriString(url.Trim(), UriKind.Absolute);

    protected override void OnPointerDown(PointF point)
    {
        if (settingsPane != null && SettingsBounds(ClientSize).Contains(point)) return;
        if (PressScrollBar(point)) return;

        state.SelectedIndex = HitRow(point);
        Win32.SetFocus(Handle);
    }

    // The mouse is captured, so a held arrow or dragged thumb keeps following the pointer beyond the window.
    private bool PressScrollBar(PointF point)
    {
        if (!scrollBar.MouseDown(point, Environment.TickCount64)) return false;

        Win32.SetCapture(Handle);
        NeedsPaint = true;
        return true;
    }

    private void ScrollTo(int position)
    {
        scrollOffset = position;
        NeedsPaint = true;
    }

    private static int VisibleRows(SizeF size) =>
        Math.Max(1, (int)(size.Height - ToolbarBottom - HeaderHeight) / RowHeight);

    protected override void OnPointerUp(PointF point)
    {
        if (scrollBar.IsInteracting)
        {
            scrollBar.MouseUp();
            Win32.ReleaseCapture();
            NeedsPaint = true;
            return;
        }

        var index = HitRow(point);
        if (index < 0 || index != state.SelectedIndex || state.Busy) return;
        if (state.Page == MonitorPage.Vods) Ui.SetValue("vod.url", state.Vods[index].Url);
        if (state.Page == MonitorPage.TopStreams) _ = ToggleFollow();
    }

    protected override void OnDoubleClick(PointF point)
    {
        // The second press of a quick double click arrives as a double click; the bar treats it as another press.
        if (PressScrollBar(point)) return;
        if (state.Page == MonitorPage.Followed && HitRow(point) >= 0) _ = OpenSelected();
    }

    protected override void OnPointerMove(PointF point)
    {
        if (scrollBar.MouseMove(point)) NeedsPaint = true;
        if (scrollBar.IsInteracting) return;

        var row = HitRow(point);
        if (row == hoveredRow) return;

        hoveredRow = row;
        const int PreviewDelayMilliseconds = 500;
        previewDue = Environment.TickCount64 + PreviewDelayMilliseconds;
        preview?.Hide();
        NeedsPaint = true;
    }

    private void UpdatePreview(long now)
    {
        if (hoveredRow < 0) return;

        Win32.GetCursorPos(out var screen);
        var point = Pointer;

        if (HasDialog || Ui.HasMenu || !Win32.IsWindowVisible(Handle) ||
            !new RectangleF(PointF.Empty, ClientSize).Contains(point) || HitRow(point) != hoveredRow)
        {
            preview?.Hide();
            hoveredRow = -1;
            return;
        }

        if (now < previewDue || hoveredRow >= ItemCount) return;

        var url = state.Page == MonitorPage.Vods
            ? state.Vods[hoveredRow].PreviewImage
            : state.Rows[hoveredRow].ThumbnailUrls?.Large;

        if (string.IsNullOrWhiteSpace(url)) return;

        preview ??= new PreviewWindow(Handle);
        preview.Display(url, screen);
        previewDue = long.MaxValue;
    }

    protected override void OnScroll(int steps)
    {
        if (settingsPane != null && SettingsBounds(ClientSize).Contains(Pointer))
        {
            settingsPane.Scroll(steps);
            return;
        }

        var followed = state.Page == MonitorPage.Followed;
        var viewport = followed ? VisibleRows(ClientSize) : LayoutTiles(ClientSize).Viewport;
        var lineSize = followed ? RowLineSize : TileLineSize;
        scrollOffset = Math.Max(0, scrollOffset - steps * WheelDistance(lineSize, viewport));
    }

    private static int WheelDistance(int lineSize, int page) =>
        WheelScrollLines == WheelPageScroll ? page : WheelScrollLines * lineSize;

    private static int ReadWheelScrollLines()
    {
        const uint GetWheelScrollLines = 0x0068;
        const int WindowsDefaultLines = 3;
        var read = Win32.SystemParametersInfo(GetWheelScrollLines, parameter: 0, out var lines, update: 0);
        return read ? lines : WindowsDefaultLines;
    }

    protected override bool OnKey(int key)
    {
        if (key == VirtualKey.Escape && settingsPane != null)
        {
            CloseSettings();
            return true;
        }

        if (key is not (VirtualKey.Up or VirtualKey.Down or VirtualKey.Enter or VirtualKey.Delete)) return false;

        if (key == VirtualKey.Up) state.SelectedIndex = Math.Max(0, state.SelectedIndex - 1);
        if (key == VirtualKey.Down) state.SelectedIndex = Math.Min(ItemCount - 1, state.SelectedIndex + 1);
        if (key == VirtualKey.Enter) _ = OpenSelected();
        var removable = state is { Busy: false, Page: MonitorPage.Followed } ? state.SelectedStream : null;
        if (key == VirtualKey.Delete && removable != null) _ = state.Remove(removable);
        KeepSelectionVisible();
        return true;
    }

    private int HitRow(PointF point)
    {
        if (state.IsContentLoading || queryQueued) return -1;
        if (settingsPane != null && SettingsBounds(ClientSize).Contains(point)) return -1;
        if (!Ui.IsContentPoint(point) || scrollBar.IsVisible && scrollBar.Bounds.Contains(point)) return -1;

        var top = state.Page == MonitorPage.Followed ? ToolbarBottom + HeaderHeight : TilesTop;

        if (point.Y < top || point.Y >= ClientSize.Height - (state.Page == MonitorPage.Followed ? 0 : FooterHeight))
            return -1;

        var index = scrollOffset + (int)(point.Y - top) / RowHeight;

        if (state.Page != MonitorPage.Followed)
        {
            var columns = LayoutTiles(ClientSize).Columns;
            var x = (int)point.X - TilesLeft;
            var y = (int)point.Y - top + scrollOffset;

            if (x < 0 || x / (TileWidth + TileGap) >= columns ||
                x % (TileWidth + TileGap) >= TileWidth || y % TileRowPitch >= TileHeight)
            {
                return -1;
            }

            index = y / TileRowPitch * columns + x / (TileWidth + TileGap);
        }

        return index >= 0 && index < ItemCount ? index : -1;
    }

    private async Task ToggleFollow()
    {
        if (state.SelectedStream is not { } stream) return;

        if (state.IsFollowed(stream))
        {
            await state.Run("Error Removing Livestream", () => state.StreamsModel.RemoveLivestream(stream.ChannelIdentifier, dialogs));
        }
        else
        {
            var channel = new ChannelIdentifier(stream.ApiClient, stream.ChannelIdentifier.ChannelId);
            await state.Run("Error Adding Livestream", () => state.StreamsModel.AddLivestream(channel, dialogs));
        }
    }

    protected override nint? OnMessage(uint message, nuint wParam, nint lParam)
    {
        if (HasDialog && message is Win32.WmRightButtonDown or Win32.WmContextMenu) return 0;

        if (message == TrayIcon.CallbackMessage)
        {
            tray.OnMessage(lParam);
            return 0;
        }

        if (message == TrayIcon.TaskbarCreatedMessage)
        {
            tray.Recreate();
            return 0;
        }

        if (message == Win32.WmSysCommand && (wParam & 0xFFF0) == Win32.ScMinimize)
        {
            Minimize();
            return 0;
        }

        if (message == Win32.WmCaptureChanged) scrollBar.MouseUp();

        if (message == Win32.WmRightButtonDown)
        {
            // As in WPF, a right-click closes an open menu and then selects what is under the pointer.
            Ui.CloseMenu();
            state.SelectedIndex = HitRow(new PointF(Win32.LowWord(lParam) / Scale, Win32.HighWord(lParam) / Scale));
            NeedsPaint = true;
        }

        if (message == Win32.WmContextMenu && !state.Busy)
        {
            ShowContextMenu();
            return 0;
        }

        return base.OnMessage(message, wParam, lParam);
    }

    private void KeepSelectionVisible()
    {
        if (state.SelectedIndex < 0) return;

        if (state.Page == MonitorPage.Followed)
        {
            var selected = state.SelectedIndex;
            var end = selected + 1;
            var visibleRows = VisibleRows(ClientSize);
            scrollOffset = GetVisibleOffset(selected, end, scrollOffset, visibleRows);
            return;
        }

        var layout = LayoutTiles(ClientSize);
        var tileTop = state.SelectedIndex / layout.Columns * TileRowPitch;
        var tileBottom = tileTop + TileHeight;
        scrollOffset = GetVisibleOffset(tileTop, tileBottom, scrollOffset, layout.Viewport);
    }

    /// <summary>
    /// The offset that brings the item from start to end into the viewport, moving no further than needed.
    /// </summary>
    internal static int GetVisibleOffset(
        int start,
        int end,
        int offset,
        int viewport)
    {
        if (start < offset) return start;
        if (end > offset + viewport) return end - viewport;

        return offset;
    }

    public override void Dispose()
    {
        popularStreams.StopWatching();
        preview?.Dispose();
        images.Dispose();
        tray.Dispose();
        launcher.Dispose();
        base.Dispose();
    }

    public void AddNotification(LivestreamNotification notification)
    {
        Program.Dispatcher.Post(() =>
        {
            if (IsClosed || state.SettingsHandler.Settings.DisableNotifications ||
                notification.LivestreamModel.DontNotify)
            {
                return;
            }

            if (launcher.IsWatching(notification.LivestreamModel)) return;

            // Popular streams are usually not followed; their notifications open Top Streams, as in WPF.
            var stream = notification.LivestreamModel;

            if (state.IsFollowed(stream))
                tray.Notify(notification.Title, notification.Message, () => ShowNotificationStream(stream));
            else tray.Notify(notification.Title, notification.Message, () => ShowTopStreams(stream));
        });
    }

    /// <summary>
    /// Finishes initializing the monitored channels, whose dialogs need this window to exist, then starts watching and checks
    /// for a new version.
    /// </summary>
    internal async Task Initialize()
    {
        await state.Initialize();
        if (!IsClosed) popularStreams.StartWatching();
        if (!IsClosed && state.SettingsHandler.Settings.CheckForNewVersions) await CheckVersion();
    }

    internal void Restore()
    {
        Show();

        // Both a window hidden to the tray and one the shell minimized are minimized; restoring animates them back.
        if (Win32.IsIconic(Handle)) Win32.ShowWindow(Handle, Win32.SwRestore);
        Win32.SetForegroundWindow(Handle);
    }

    protected override void Minimize()
    {
        Win32.ShowWindow(Handle, Win32.SwMinimize);
        _ = HideAfterMinimizeAnimation(++minimizeCount);

        if (firstMinimize && !state.SettingsHandler.Settings.DisableMinimizeToTrayNotification)
            tray.Notify("Livestream Monitor", "Livestream Monitor minimized to tray", Restore);

        firstMinimize = false;
    }

    // Hiding a window cancels Windows' minimize animation, which reports no end; the delay leaves it time to finish.
    private async Task HideAfterMinimizeAnimation(int minimizeNumber)
    {
        const int MinimizeAnimationMilliseconds = 300;
        await Task.Delay(MinimizeAnimationMilliseconds);

        // A window restored during the animation stays shown, and a later minimize hides it after its own animation.
        if (minimizeNumber == minimizeCount && !IsClosed && Win32.IsIconic(Handle)) Hide();
    }

    private void NotifyOnline(LivestreamModel stream)
    {
        if (launcher.IsWatching(stream)) return;

        tray.Notify($"{stream.DisplayName} Online", stream.Description, () => ShowNotificationStream(stream));
    }

    private void ShowNotificationStream(LivestreamModel stream)
    {
        requestedNotificationStream = stream;
        Restore();
    }

    private void ShowTopStreams(LivestreamModel stream)
    {
        RequestPage(MonitorPage.TopStreams, stream.ApiClient);
        Restore();
    }

    private void RequestPage(MonitorPage page, IApiClient provider) =>
        requestedPage = new PageRequest(page, Array.IndexOf(providers, provider));

    private async Task CheckVersion()
    {
        try
        {
            var release = await new ExternalAPIs.GitHub.GitHubClient().GetLatestRelease();
            if (IsClosed) return;

            // Only an https download page is opened.
            if (release == null || !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException($"Invalid HtmlUrl provided by GitHub API for latest release: {release?.HtmlUrl ?? "<null>"}");

            if (!Version.TryParse(release.TagName, out var latest) || latest <= AppVersion) return;

            if (await dialogs.Confirm("New version available", "There is a newer version available. Go to download page?"))
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            // As in WPF, any other failure, including one opening the download page, is reported.
            if (IsClosed) return;

            var message = $"An error occurred while checking for a newer version.{Environment.NewLine}{exception.ExtractErrorMessage()}";
            await dialogs.ShowMessage("Error", message);
        }
    }

    private static bool CanOpenChat(LivestreamModel? stream) =>
        stream is { ApiClient.HasChatSupport: true, IsChatDisabled: false };

    private async Task OpenSelected()
    {
        if (state.Page != MonitorPage.Vods)
        {
            if (state.SelectedStream is { } stream) await OpenStream(stream);
            return;
        }

        try
        {
            if (state.SelectedIndex >= 0 && state.SelectedIndex < state.Vods.Count)
                launcher.OpenVod(state.Vods[state.SelectedIndex]);
        }
        catch (Exception exception)
        {
            await ShowVodLaunchError(exception);
        }
    }

    // WPF titles only the URL check as an invalid VOD URL; the launcher reports that check as invalid data.
    private Task ShowVodLaunchError(Exception exception)
    {
        var title = exception is InvalidDataException ? "VOD Url invalid" : "Error";
        return dialogs.ShowMessage(title, exception.ExtractErrorMessage());
    }

    private async Task OpenStream(LivestreamModel stream)
    {
        try
        {
            await launcher.OpenStream(stream, chat: false);
        }
        catch (Exception exception)
        {
            var message = "An error occurred attempting to open the stream: " + exception.ExtractErrorMessage();
            await dialogs.ShowMessage("Error opening stream", message);
        }
    }

    private void ToggleNotification(LivestreamModel stream)
    {
        state.SettingsHandler.Settings.ToggleNotify(stream);
        NeedsPaint = true;
    }

    // The items, text and order of the WPF frontend's stream list and top stream tile menus; VOD tiles have none.
    private void ShowContextMenu()
    {
        if (state.Page == MonitorPage.Vods || state.SelectedStream is not { } stream) return;

        var notify = !stream.DontNotify;
        (SurfaceUi.MenuChoice Choice, ContextCommand Command)[] items;

        if (state.Page == MonitorPage.Followed)
        {
            const string NotifyToolTip =
                "Untick this to prevent popup notifications about this stream coming online or showing up as a " +
                "popular stream";

            var hasVods = stream.ApiClient.HasVodViewerSupport;
            items =
            [
                (new SurfaceUi.MenuChoice("Remove Stream"), ContextCommand.Remove),
                (new SurfaceUi.MenuChoice("Copy Stream URL"), ContextCommand.CopyUrl),
                (new SurfaceUi.MenuChoice("Open in Browser"), ContextCommand.OpenInBrowser),
                (new SurfaceUi.MenuChoice("Open in Vod Viewer", Enabled: hasVods), ContextCommand.OpenVods),
                (NotifyChoice(NotifyToolTip), ContextCommand.Notify),
            ];
        }
        else
        {
            const string NotifyToolTip =
                "Untick this to prevent popup notifications about this stream coming online (if being followed) or " +
                "showing up as a popular stream";

            items =
            [
                (new SurfaceUi.MenuChoice("Copy Stream Url"), ContextCommand.CopyUrl),
                (new SurfaceUi.MenuChoice("Open Stream"), ContextCommand.Open),
                (new SurfaceUi.MenuChoice("Open Stream in Browser"), ContextCommand.OpenInBrowser),
                (new SurfaceUi.MenuChoice("Open Chat"), ContextCommand.OpenChat),
                (new SurfaceUi.MenuChoice("Open Vod Viewer"), ContextCommand.OpenVods),
                (NotifyChoice(NotifyToolTip), ContextCommand.Notify),
            ];
        }

        var choices = Array.ConvertAll(items, item => item.Choice);
        Ui.Popup(id: "context", Pointer, choices, chosen: Choose);

        void Choose(int index) => _ = RunContextCommand(stream, items[index].Command);

        SurfaceUi.MenuChoice NotifyChoice(string toolTip)
        {
            const string NotifyText = "Enable Notifications";
            return new SurfaceUi.MenuChoice(Text: NotifyText, Checked: notify, ToolTip: toolTip, Checkable: true);
        }
    }

    private async Task RunContextCommand(LivestreamModel stream, ContextCommand command)
    {
        try
        {
            switch (command)
            {
                case ContextCommand.Open:
                    await OpenStream(stream);
                    break;
                case ContextCommand.OpenChat:
                    await OpenChat(stream);
                    break;
                case ContextCommand.CopyUrl:
                    await CopyStreamUrl(stream);
                    break;
                case ContextCommand.OpenInBrowser:
                    OpenInBrowser(await stream.GetStreamUrl());
                    break;
                case ContextCommand.OpenVods:
                    var channel = stream.ChannelIdentifier;
                    Ui.SetValue("vod.channel", channel.DisplayName ?? channel.ChannelId);
                    RequestPage(MonitorPage.Vods, stream.ApiClient);
                    break;
                case ContextCommand.Remove:
                    await state.Remove(stream);
                    break;
                case ContextCommand.Notify:
                    ToggleNotification(stream);
                    break;
            }
        }
        catch (Exception exception)
        {
            await dialogs.ShowMessage("Error", exception.ExtractErrorMessage());
        }
    }

    private async Task CopyStreamUrl(LivestreamModel stream)
    {
        try
        {
            CopyToClipboard(await stream.GetStreamUrl());
        }
        catch (Exception exception)
        {
            var message = "An error occurred attempting to copy the url: " + exception.ExtractErrorMessage();
            await dialogs.ShowMessage("Error copying url", message);
        }
    }

    // The messages of the WPF frontend's chat launcher.
    private async Task OpenChat(LivestreamModel? stream)
    {
        if (stream == null) return;

        if (!stream.ApiClient.HasChatSupport)
        {
            var unsupported = $"No external chat support for stream provider '{stream.ApiClient.ApiName}'";
            await dialogs.ShowMessage("Chat not supported", unsupported);
            return;
        }

        try
        {
            await launcher.OpenStream(stream, chat: true);
        }
        catch (Exception exception)
        {
            var message = "An error occurred attempting to open the chat: " + exception.ExtractErrorMessage();
            await dialogs.ShowMessage("Error opening chat", message);
        }
    }

    private static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private unsafe void CopyToClipboard(string text)
    {
        const uint MoveableMemory = 2;
        const uint UnicodeText = 13;
        if (!Win32.OpenClipboard(Handle)) return;

        nint memory = 0;
        try
        {
            var characters = text.Length + 1;
            memory = Win32.GlobalAlloc(MoveableMemory, (nuint)(characters * sizeof(char)));
            var pointer = Win32.GlobalLock(memory);
            if (pointer == 0) throw new InvalidOperationException("Cannot allocate clipboard text.");

            var destination = new Span<char>((void*)pointer, characters);
            text.CopyTo(destination);
            destination[^1] = '\0';
            Win32.GlobalUnlock(memory);
            Win32.EmptyClipboard();
            if (Win32.SetClipboardData(UnicodeText, memory) != 0) memory = 0;
        }
        finally
        {
            if (memory != 0) Win32.GlobalFree(memory);

            Win32.CloseClipboard();
        }
    }

    private readonly record struct PageRequest(MonitorPage Page, int ProviderIndex);

    private readonly record struct TileLayout(int Columns, float Width, int Viewport, int Extent);

    private enum ContextCommand
    {
        Open,
        OpenChat,
        CopyUrl,
        OpenInBrowser,
        OpenVods,
        Remove,
        Notify,
    }
}
