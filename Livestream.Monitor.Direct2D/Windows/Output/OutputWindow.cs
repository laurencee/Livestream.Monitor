using System.Drawing;
using System.Runtime.InteropServices;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows.Output;

/// <summary>
/// Shows a launched process's captured output in a read-only Rich Edit control, which keeps native selection and
/// copying, with a drawn caption, scrollbar and footer. The window grows with its content up to a fixed size.
/// </summary>
internal sealed class OutputWindow : CanvasWindow
{
    // Separates the output box from the caption and from the hide on load check, and the check from the bottom edge.
    private const float Margin = 10;

    // The output box and the hide on load check sit closer to the window's sides, leaving the text more room.
    private const float SideMargin = 5;
    private const float TextInset = 2;
    private const float CheckHeight = 13;
    private const float FooterHeight = CheckHeight + Margin;
    private const float DiscardNoticeHeight = 18;
    private const float MaximumViewportWidth = 700;
    private const float MaximumViewportHeight = 500;
    private const float MinimumWidth = 160;

    // Room for the caption, the check and a line of text, however small the app is.
    private const float MinimumHeight = 100;
    private const float MaximumAppWidthShare = 0.75f;
    private const float InitialWidth = 552;

    // A typical stream start prints seven lines; the window opens with room for them and the empty line after them.
    private const int InitialLines = 8;
    private const int RefreshMilliseconds = 100;
    private const uint GetLineCount = 0x00BA;
    private const uint ScrollLines = 0x00B6;
    private const uint GetLineIndex = 0x00BB;
    private const uint GetFirstVisibleLine = 0x00CE;
    private const uint PositionFromCharacter = 0x0426;

    // The WPF limits alone, which bound the size the output asks for whatever the app's size.
    private static readonly SizeF WpfMaximumSize = MaximumSize(new SizeF(float.MaxValue, float.MaxValue));
    private readonly nint text;
    private readonly nint owner;
    private readonly string title;
    private readonly ISettingsHandler settings;
    private readonly BoundedOutputBuffer buffer = new();
    private readonly OutputScrollbar scrollbar;
    private long nextRefresh;
    private bool positioned;

    // The window opens at the initial size and only grows, to fit wider or longer output up to the WPF limits; it is shown
    // no larger than the app's current size allows, so it grows back once the app does.
    private SizeF requestedSize;

    // Unknown until the app is seen restored, when only the WPF limits apply.
    private SizeF appSize = new(float.MaxValue, float.MaxValue);
    private SizeF appliedAppSize;
    private bool showDiscardNotice;
    private ThemePalette? outputPalette;

    static OutputWindow()
    {
        // Rich Edit's window procedure must remain loaded for the lifetime of its HWNDs.
        var library = Path.Combine(Environment.SystemDirectory, "msftedit.dll");
        NativeLibrary.Load(library);
    }

    public OutputWindow(
        nint owner,
        string title,
        ISettingsHandler settings,
        Func<ThemePalette>? theme) : base(title: title, width: 1, height: 1, owner: owner)
    {
        this.owner = owner;
        this.title = title;
        this.settings = settings;
        scrollbar = new OutputScrollbar(ScrollToLine);
        ThemeSource = theme ?? (() => Theme.Create(settings.Settings));
        var windowStyle = Win32.GetWindowLongPtr(Handle, Win32.GwlStyle);
        windowStyle &= ~(nint)(Win32.WsThickFrame | Win32.WsMinimizeBox);
        Win32.SetWindowLongPtr(Handle, Win32.GwlStyle, windowStyle);
        var style = Win32.EsMultiLine | Win32.EsReadOnly;
        text = Control(className: "RICHEDIT50W", text: "", bounds: Rectangle.Empty, style: style);
        const uint SetTextMode = 0x0459;
        const uint PlainText = 1;
        Win32.SendMessage(text, SetTextMode, PlainText, lParam: 0);

        // Hide Rich Edit's own scrollbar and receive its scroll requests; the window draws its own scrollbar.
        const uint ShowScrollbar = 0x0460;
        const uint GetEventMask = 0x043B;
        const uint SetEventMask = 0x0445;
        const nuint ScrollNotifications = 4;
        const nuint WheelNotifications = 8;
        Win32.SendMessage(text, ShowScrollbar, Win32.SbVertical, lParam: 0);
        var events = (nuint)Win32.SendMessage(text, GetEventMask, wParam: 0, lParam: 0);
        var eventMask = (nint)(events | ScrollNotifications | WheelNotifications);
        Win32.SendMessage(text, SetEventMask, wParam: 0, eventMask);

        requestedSize = RequestedSize(new SizeF(InitialWidth, 0), contentWidth: 0, LinesHeight(InitialLines));
    }

    /// <summary>True once the process has exited; closing then destroys the window instead of hiding it.</summary>
    public bool Completed { get; set; }
    public event Action? Dismissed;
    protected override bool CanResize => false;

    /// <summary>
    /// The largest the window may be: the WPF viewport limits, and no wider than three quarters of the app nor taller than
    /// the app.
    /// </summary>
    public static SizeF MaximumSize(SizeF appSize)
    {
        var width = Math.Min(MaximumViewportWidth, appSize.Width * MaximumAppWidthShare);
        var height = Math.Min(TitleHeight + MaximumViewportHeight + FooterHeight, appSize.Height);
        return new SizeF(Math.Max(MinimumWidth, width), Math.Max(MinimumHeight, height));
    }

    public static SizeF CalculateSize(float contentWidth, float contentHeight, SizeF maximumSize)
    {
        var horizontalPadding = (SideMargin + TextInset) * 2;
        var verticalPadding = (Margin + TextInset) * 2;
        var width = Math.Clamp(contentWidth + horizontalPadding + OutputScrollbar.Width, MinimumWidth, maximumSize.Width);
        var height = Math.Min(TitleHeight + contentHeight + verticalPadding + FooterHeight, maximumSize.Height);
        return new SizeF(width, height);
    }

    /// <summary>
    /// The size the output asks for once it needs room for the content as well: it only grows, within the WPF limits
    /// whatever the app's size, so the window grows back to it once a smaller app is enlarged.
    /// </summary>
    public static SizeF RequestedSize(SizeF requestedSize, float contentWidth, float contentHeight)
    {
        var contentSize = CalculateSize(contentWidth, contentHeight, WpfMaximumSize);
        return new SizeF(Math.Max(requestedSize.Width, contentSize.Width), Math.Max(requestedSize.Height, contentSize.Height));
    }

    /// <summary>The window size shown for the requested size, with room for a notice, within the app's size.</summary>
    public static SizeF ShownSize(SizeF requestedSize, float noticeHeight, SizeF appSize)
    {
        var maximumSize = MaximumSize(appSize);
        return new SizeF(Math.Min(requestedSize.Width, maximumSize.Width), Math.Min(requestedSize.Height + noticeHeight, maximumSize.Height));
    }

    // As in WPF, the window is activated whenever it is shown.
    public void ShowInitial()
    {
        RefreshOutput();
        Tick();
        Show();
        Activate();
    }

    public void ShowFinal()
    {
        RefreshOutput();
        NeedsPaint = true;
        Show();
        Activate();
    }

    // Activates the window and draws it again at once, so it shows its active chrome rather than the inactive frame it
    // was shown with.
    private void Activate()
    {
        Win32.SetForegroundWindow(Handle);
        Tick();
    }

    /// <summary>Appends a line; safe to call from any thread.</summary>
    public void Append(string line)
    {
        buffer.Append(line.ReplaceLineEndings("\n"));
        buffer.Append("\n");
    }

    /// <summary>Appends captured output with normalized line breaks; safe to call from any thread.</summary>
    public void AppendChunk(ReadOnlySpan<char> chunk) => buffer.Append(chunk);

    protected override void BeforeFrame()
    {
        var now = Environment.TickCount64;
        scrollbar.Repeat(now);
        if (now < nextRefresh || !Win32.IsWindowVisible(Handle)) return;

        nextRefresh = now + RefreshMilliseconds;
        RefreshOutput();
    }

    protected override void Draw(SizeF size)
    {
        var palette = Ui.Palette;
        ApplyOutputTheme(palette);
        DrawCaption(title, size.Width);
        var noticeHeight = showDiscardNotice ? DiscardNoticeHeight : 0;
        var viewportHeight = Math.Max(0, size.Height - TitleHeight - FooterHeight - noticeHeight);
        var outputBounds = PlaceOutput(size);
        Ui.Fill(outputBounds, palette.Input);
        Ui.Border(outputBounds, palette.Border);
        var bar = RectangleF.Inflate(outputBounds, -TextInset, -TextInset);
        bar.X = bar.Right - OutputScrollbar.Width;
        bar.Width = OutputScrollbar.Width;
        var state = ReadScrollState();
        if (state.First > LastPage(state)) ScrollToLine(LastPage(state));
        scrollbar.Update(bar, state.Lines, state.Visible, state.First);
        scrollbar.Draw(Ui);
        var footerTop = TitleHeight + viewportHeight;

        if (showDiscardNotice)
        {
            var notice = new RectangleF(
                x: SideMargin,
                y: footerTop,
                width: size.Width - SideMargin * 2,
                height: DiscardNoticeHeight);

            Ui.Text(text: "Earlier output discarded", notice, palette.MutedText, size: 11);
            footerTop += DiscardNoticeHeight;
        }

        var checkBounds = new RectangleF(
            x: SideMargin,
            y: footerTop,
            width: size.Width - SideMargin * 2,
            height: CheckHeight);

        var hideOnLoad = settings.Settings.HideStreamOutputMessageBoxOnLoad;
        Ui.Check(
            id: "output.hide",
            text: "Hide on load",
            value: hideOnLoad,
            bounds: checkBounds,
            changed: SetHideOnLoad,
            toolTip: "Hides this message box automatically once a stream has loaded successfully");
    }

    protected override bool PreprocessKey(int key)
    {
        if (Win32.GetFocus() != text) return base.PreprocessKey(key);
        if (key != VirtualKey.A || !Win32.IsKeyDown(VirtualKey.Control)) return false;

        Win32.SendMessage(text, Win32.EmSetSelection, wParam: 0, lParam: -1);
        return true;
    }

    protected override void OnPointerDown(PointF point)
    {
        if (scrollbar.MouseDown(point) && scrollbar.IsInteracting) Win32.SetCapture(Handle);
    }

    protected override void OnPointerMove(PointF point) => scrollbar.MouseMove(point);

    protected override void OnPointerUp(PointF point)
    {
        if (!scrollbar.IsInteracting) return;

        scrollbar.MouseUp();
        Win32.ReleaseCapture();
    }

    protected override void OnScroll(int steps)
    {
        const int LinesPerWheelStep = 3;
        ScrollWithinOutput(ReadScrollState().First - steps * LinesPerWheelStep);
    }

    protected override void RequestClose() => Dismiss();

    protected override void OnClosed() => Dismissed?.Invoke();

    protected override unsafe nint? OnMessage(uint message, nuint wParam, nint lParam)
    {
        const uint FilterMessage = 0x0700;

        if (message == Win32.WmNotify)
        {
            var notification = (Win32.NotificationHeader*)lParam;

            if (notification->Window == text && notification->Code == FilterMessage)
            {
                var filter = (Win32.MessageFilter*)lParam;

                if (filter->MessageId == Win32.WmVerticalScroll)
                {
                    // Rich Edit converts wheel input to line-scroll notifications before applying it.
                    const int LineUp = 0;
                    const int LineDown = 1;
                    var command = Win32.LowWord((nint)filter->WParam);
                    if (command != LineUp && command != LineDown) return 0;

                    var direction = command == LineUp ? -1 : 1;
                    ScrollWithinOutput(ReadScrollState().First + direction);
                    return 1;
                }
            }
        }

        if (message == Win32.WmCaptureChanged) scrollbar.MouseUp();
        return base.OnMessage(message, wParam, lParam);
    }

    private void RefreshOutput()
    {
        var currentAppSize = ReadAppSize();

        // The app may be resized while the output pauses; the window then keeps within the app's new size.
        if (!buffer.TryRead(out var snapshot))
        {
            if (currentAppSize == appliedAppSize) return;

            ApplySize(currentAppSize);
            NeedsPaint = true;
            return;
        }

        if (requestedSize.Width < WpfMaximumSize.Width)
        {
            const float WrapAllowance = 2;
            var padding = (SideMargin + TextInset) * 2 + OutputScrollbar.Width;

            // Lines are measured as Rich Edit lays them out, in its GDI font, which runs wider than Direct2D text; the
            // allowance covers its width being truncated to whole device pixels.
            var naturalWidth = MeasureControlText(snapshot.Text) + WrapAllowance / Scale;
            var captionWidth = MeasureWidth(title, CaptionTitleFontSize) + CaptionTitleMargins - padding;
            requestedSize = RequestedSize(requestedSize, Math.Max(naturalWidth, captionWidth), contentHeight: 0);
        }

        showDiscardNotice = snapshot.DiscardedCharacters != 0;
        ApplySize(currentAppSize);
        SetOutput(snapshot.Text);

        // Rich Edit has now wrapped the output at the window's width; the window grows to show its lines, up to the maximum.
        var lines = (int)Win32.SendMessage(text, GetLineCount, wParam: 0, lParam: 0);
        var grownSize = RequestedSize(requestedSize, contentWidth: 0, LinesHeight(lines));
        if (grownSize != requestedSize)
        {
            requestedSize = grownSize;
            ApplySize(currentAppSize);
            ScrollOutputToEnd();
        }

        NeedsPaint = true;
    }

    private void ApplySize(SizeF currentAppSize)
    {
        var noticeHeight = showDiscardNotice ? DiscardNoticeHeight : 0;
        var size = ResizeOutput(ShownSize(requestedSize, noticeHeight, currentAppSize));
        PlaceOutput(size);
        appliedAppSize = currentAppSize;
    }

    // The height of the lines at Rich Edit's line height.
    private float LinesHeight(int lines)
    {
        // A hundredth of a device pixel more keeps the control's height, rounded down to whole pixels, from losing a pixel of
        // the last line to floating-point error.
        const float RoundingAllowance = 0.01f;
        return lines * MeasureControlLineHeight() + RoundingAllowance / Scale;
    }

    // The app window's size in logical units at its own DPI; while it is minimized, the size it last had restored.
    private SizeF ReadAppSize()
    {
        if (Win32.IsIconic(owner)) return appSize;

        Win32.GetWindowRect(owner, out var bounds);
        var scale = Win32.GetDpiForWindow(owner) / (float)Win32.UserDefaultScreenDpi;
        appSize = new SizeF(bounds.Width / scale, bounds.Height / scale);
        return appSize;
    }

    // Returns the size the window has, in whole device pixels, so the output is placed as the next frame draws it.
    private SizeF ResizeOutput(SizeF size)
    {
        Win32.GetWindowRect(Handle, out var previous);
        var width = (int)Math.Ceiling(size.Width * Scale);
        var height = (int)Math.Ceiling(size.Height * Scale);
        var deviceSize = new SizeF(width / Scale, height / Scale);
        if (previous.Width == width && previous.Height == height && positioned) return deviceSize;

        var flags = Win32.SwpNoMove | Win32.SwpNoZOrder | Win32.SwpNoActivate | Win32.SwpFrameChanged;
        Win32.SetWindowPos(Handle, insertAfter: 0, x: 0, y: 0, width, height, flags);

        if (!positioned)
        {
            CenterOnOwner(owner);
            positioned = true;
        }
        else
        {
            var position = Monitors.ClampToWorkArea(
                previous.Left,
                previous.Top,
                width,
                height,
                Program.MonitorName);

            flags = Win32.SwpNoSize | Win32.SwpNoZOrder | Win32.SwpNoActivate;
            Win32.SetWindowPos(Handle, insertAfter: 0, position.X, position.Y, width: 0, height: 0, flags);
        }

        return deviceSize;
    }

    private void SetOutput(string output)
    {
        // Without the final line break, Rich Edit has no empty last line to count, show or scroll to.
        var shown = output.EndsWith('\n') ? output[..^1] : output;
        Win32.SetWindowText(text, shown);

        // Apply text, layout and tail position as one update; do not depend on a later paint or timer.
        var end = Win32.GetWindowTextLength(text);
        Win32.SendMessage(text, Win32.EmSetSelection, wParam: (nuint)end, lParam: end);
        ScrollOutputToEnd();
    }

    private void ScrollOutputToEnd() => ScrollWithinOutput(int.MaxValue);

    // Scrolls the line to the top, stopping where the last line reaches the bottom.
    private void ScrollWithinOutput(int line) => ScrollToLine(Math.Clamp(line, 0, LastPage(ReadScrollState())));

    private static int LastPage((int Lines, int Visible, int First) state) => Math.Max(0, state.Lines - state.Visible);

    private unsafe (int Lines, int Visible, int First) ReadScrollState()
    {
        var lines = (int)Win32.SendMessage(text, GetLineCount, wParam: 0, lParam: 0);
        var visible = lines;

        if (lines > 1)
        {
            var secondLine = Win32.SendMessage(text, GetLineIndex, wParam: 1, lParam: 0);
            var firstPosition = new Win32.Point();
            var secondPosition = new Win32.Point();
            Win32.SendMessage(text, PositionFromCharacter, wParam: (nuint)(&firstPosition), lParam: 0);
            Win32.SendMessage(text, PositionFromCharacter, wParam: (nuint)(&secondPosition), secondLine);
            Win32.GetClientRect(text, out var viewport);
            var lineHeight = Math.Max(1, secondPosition.Y - firstPosition.Y);
            visible = Math.Max(1, viewport.Height / lineHeight);
        }

        var first = (int)Win32.SendMessage(text, GetFirstVisibleLine, wParam: 0, lParam: 0);
        return (lines, visible, first);
    }

    private void ScrollToLine(int target)
    {
        var current = (int)Win32.SendMessage(text, GetFirstVisibleLine, wParam: 0, lParam: 0);
        var lineDelta = target - current;
        Win32.SendMessage(text, ScrollLines, wParam: 0, lParam: lineDelta);
        NeedsPaint = true;
    }

    private RectangleF PlaceOutput(SizeF size)
    {
        var noticeHeight = showDiscardNotice ? DiscardNoticeHeight : 0;
        var viewportHeight = Math.Max(0, size.Height - TitleHeight - FooterHeight - noticeHeight);
        var bounds = new RectangleF(
            x: SideMargin,
            y: TitleHeight + Margin,
            width: Math.Max(1, size.Width - SideMargin * 2),
            height: Math.Max(1, viewportHeight - Margin * 2));

        var inner = RectangleF.Inflate(bounds, -TextInset, -TextInset);
        inner.Width = Math.Max(1, inner.Width - OutputScrollbar.Width);
        PlaceInput(text, inner, visible: true, enabled: true);
        return bounds;
    }

    private unsafe void ApplyOutputTheme(ThemePalette palette)
    {
        if (outputPalette == palette) return;

        outputPalette = palette;
        const uint SetBackgroundColor = 0x0443;
        const uint SetCharacterFormat = 0x0444;
        const uint CharacterColor = 0x40000000;
        const uint AllText = 4;
        var background = (nint)Win32.ColorRef(palette.Input);
        Win32.SendMessage(text, SetBackgroundColor, wParam: 0, background);
        var format = new Win32.CharacterFormat
        {
            Size = (uint)sizeof(Win32.CharacterFormat),
            Mask = CharacterColor,
            TextColor = Win32.ColorRef(palette.Text),
        };

        var formatPointer = (nint)(&format);
        Win32.SendMessage(text, SetCharacterFormat, wParam: 0, formatPointer);
        Win32.SendMessage(text, SetCharacterFormat, AllText, formatPointer);
    }

    private void SetHideOnLoad(bool value)
    {
        settings.Settings.HideStreamOutputMessageBoxOnLoad = value;
        settings.SaveSettings();
    }

    private void Dismiss()
    {
        if (Completed) Close();
        else Hide();
    }
}
