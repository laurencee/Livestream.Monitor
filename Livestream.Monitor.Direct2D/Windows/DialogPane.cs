using System.Drawing;
using Livestream.Monitor.Core;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows;

internal enum DialogKind { Message, Confirmation, Input, Progress }

internal sealed class DialogPane(
    CanvasWindow owner,
    string title,
    string message,
    DialogKind kind,
    string affirmative,
    string negative) : IDisposable
{
    private const string InputId = "dialog.input";
    private const float TitleFontSize = 26;
    private const float MessageFontSize = 15;
    private const float ButtonWidth = 80;
    private const float ButtonHeight = 35;
    private const float ScrollBarGap = 4;
    private const float FooterHeight = 85;
    private const float ProgressBarHeight = 6;

    // The start of each progress dot's cycle, in seconds, from MahApps 2.4.11 MetroProgressBar's storyboard.
    private static readonly double[] ProgressDotDelays = [0, 0.167, 0.333, 0.5, 0.667];

    private readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long shownAt = Environment.TickCount64;

    // The message is shown in a native read-only edit control, so its text can be selected and copied.
    private readonly nint messageText = owner.CreateReadOnlyText(message, MessageFontSize);
    private RectangleF messageBounds;
    private bool scrolls;
    private int drawnFirstLine;
    private int maximumFirstLine;
    private string input = "";
    private bool synchronizeInput = true;
    private bool focusRequested = true;
    private bool disposed;

    public Task<string?> Completion => completion.Task;
    public bool IsProgress => kind == DialogKind.Progress;
    public bool IsScrolling => ScrollBar.IsInteracting;
    public RectangleF PanelBounds { get; private set; }
    private VerticalScrollBar ScrollBar => field ??= new VerticalScrollBar(ScrollToLine);

    public void Draw(SurfaceUi ui, SizeF size)
    {
        const uint BackdropOpacity = 0xB3000000;
        const uint RgbMask = 0x00FFFFFF;
        var viewport = new RectangleF(PointF.Empty, size);
        ui.Fill(viewport, BackdropOpacity | (ui.Palette.Text & RgbMask));
        var contentWidth = Math.Max(180, size.Width / 2);
        var left = (size.Width - contentWidth) / 2;
        var titleHeight = owner.MeasureWrapped(title, contentWidth, TitleFontSize);
        var maximumMessageHeight = Math.Max(43, size.Height - titleHeight - 165);

        // A message taller than the dialog has room for is narrowed for a scroll bar beside it. The width drawn last is
        // tried first, so a settled layout places the message control only once.
        var lineHeight = owner.MeasureControlLineHeight(MessageFontSize);
        var lineCount = CountLines();

        var overflows = LinesHeight(lineCount) > maximumMessageHeight;
        if (overflows != scrolls)
        {
            scrolls = overflows;
            lineCount = CountLines();
        }

        // As in MahApps 2.4.11, the title has a 10 px top padding and the message a 5 px top margin, which a message
        // dialog's content grid adds 10 to; a progress dialog keeps the empty footer of a message dialog, with the
        // progress bar along the panel's bottom edge. A dialog is at least a quarter of the window's height, with the
        // spare height between its content and its footer.
        const float TitleTop = 10;
        var messageHeight = Math.Min(LinesHeight(lineCount), maximumMessageHeight);
        var messageGap = IsProgress ? 5 : 15;
        var inputHeight = kind == DialogKind.Input ? 32 : 0;
        var barHeight = IsProgress ? ProgressBarHeight : 0;
        var contentHeight = TitleTop + titleHeight + messageGap + messageHeight + inputHeight + FooterHeight + barHeight;
        var panelHeight = Math.Max(contentHeight, size.Height / 4);
        var spareHeight = panelHeight - contentHeight;
        var top = (size.Height - panelHeight) / 2;
        var panel = new RectangleF(x: 1, y: top, width: size.Width - 2, height: panelHeight);
        PanelBounds = panel;
        ui.Fill(panel, ui.Palette.Background);
        var titleBounds = new RectangleF(x: left, y: top + TitleTop, width: contentWidth, height: titleHeight);
        ui.Text(title, titleBounds, size: TitleFontSize, wrap: true);
        messageBounds = new RectangleF(
            x: left,
            y: titleBounds.Bottom + messageGap,
            width: TextWidth(),
            height: messageHeight
        );

        owner.PlaceInput(messageText, messageBounds, visible: true, enabled: true);
        var visibleLines = Math.Max(1, (int)(messageHeight / lineHeight));
        maximumFirstLine = Math.Max(0, lineCount - visibleLines);
        drawnFirstLine = FirstVisibleLine();
        var barBounds = messageBounds with { X = left + contentWidth - VerticalScrollBar.Width, Width = VerticalScrollBar.Width };
        ScrollBar.Update(barBounds, extent: lineCount, viewport: visibleLines, offset: drawnFirstLine);
        ScrollBar.Draw(ui);

        var contentBottom = messageBounds.Bottom;

        if (kind == DialogKind.Input)
        {
            var inputBounds = new RectangleF(x: left, y: contentBottom + 2, width: contentWidth, height: 30);
            ui.Input(
                InputId,
                "",
                inputBounds,
                value => input = value,
                Accept
            );

            if (synchronizeInput)
            {
                ui.SetValue(InputId, input);
                synchronizeInput = false;
            }

            contentBottom = inputBounds.Bottom;
        }

        if (IsProgress)
        {
            var progressBounds = panel with { Y = panel.Bottom - ProgressBarHeight, Height = ProgressBarHeight };
            DrawProgressBar(ui, progressBounds);
            return;
        }

        // As in MahApps, the last button has a 5 px right margin and the buttons are 10 px apart.
        const float ButtonRightMargin = 5;
        var hasCancel = kind is DialogKind.Input or DialogKind.Confirmation;
        var buttonsRight = left + contentWidth - ButtonRightMargin;
        var affirmativeLeft = buttonsRight - ButtonWidth - (hasCancel ? ButtonWidth + 10 : 0);
        var affirmativeBounds = new RectangleF(
            x: affirmativeLeft,
            y: contentBottom + spareHeight + 25,
            width: ButtonWidth,
            height: ButtonHeight
        );

        ui.Button(
            id: "dialog.accept",
            text: affirmative,
            bounds: affirmativeBounds,
            action: Accept,
            accent: true,
            weight: FontWeight.SemiBold,
            flat: true
        );
        if (!hasCancel) return;

        var cancelBounds = affirmativeBounds with { X = buttonsRight - ButtonWidth };
        ui.Button(
            id: "dialog.cancel",
            text: negative,
            bounds: cancelBounds,
            action: Dispose,
            weight: FontWeight.SemiBold,
            flat: true
        );

        float TextWidth() => scrolls ? contentWidth - ScrollBarGap - VerticalScrollBar.Width : contentWidth;

        // The message control wraps its text at its width, so it is placed at that width before its lines are counted.
        int CountLines()
        {
            const uint GetLineCount = 0x00BA;
            var wrapBounds = messageBounds with { Width = TextWidth() };
            owner.PlaceInput(messageText, wrapBounds, visible: true, enabled: true);
            return (int)Win32.SendMessage(messageText, GetLineCount, wParam: 0, lParam: 0);
        }

        // A hundredth of a logical pixel more keeps the control's height, rounded down to whole device pixels, from
        // losing a pixel of the last line to floating-point error.
        float LinesHeight(int lines)
        {
            const float RoundingAllowance = 0.01f;
            return lines * lineHeight + RoundingAllowance;
        }
    }

    /// <summary>
    /// Draws MahApps 2.4.11 MetroProgressBar's indeterminate animation, without a track as MahApps draws it: five dots that
    /// slide in, rest at a third of the bar and leave at two thirds, while the group they belong to drifts right.
    /// </summary>
    private void DrawProgressBar(SurfaceUi ui, RectangleF bar)
    {
        // MetroProgressBar sizes the dots and the group's travel by the bar's width.
        var width = bar.Width;
        var (diameter, spacing, groupStart, groupEnd) = width switch
        {
            <= 180 => (4f, 4f, -34f, 0.4352f * width - 25.731f),
            <= 280 => (5f, 7f, -50.5f, 0.4352f * width + 27.84f),
            _ => (6f, 9f, -63f, 0.4352f * width + 58.862f),
        };

        const double Period = 3.917;
        var time = (Environment.TickCount64 - shownAt) / 1000.0 % Period;
        var group = groupStart + (groupEnd - groupStart) * (float)(time / Period);
        var rest = width / 3;
        var exit = width * 2 / 3;
        var top = bar.Y + (bar.Height - diameter) / 2;

        ui.PushClip(bar);

        for (var i = 0; i < ProgressDotDelays.Length; i++)
        {
            var delay = ProgressDotDelays[i];
            var age = time - delay;
            const double DotLifetime = 3;
            if (age is < 0 or >= DotLifetime) continue;

            // Each dot's slot in the group lies left of the previous dot's; its slide starts with the storyboard.
            const double SlideInEnd = 0.5;
            const double SlideOutStart = 2;
            const float SlideInFrom = -50;
            const float SlideOutBy = 100;
            var slot = (ProgressDotDelays.Length - 1 - i) * (diameter + spacing);
            var slideInEnd = SlideInEnd + delay;
            var slide = time < slideInEnd
                ? SlideInFrom * (float)(1 - time / slideInEnd)
                : age < SlideOutStart ? 0 : SlideOutBy * (float)(age - SlideOutStart);

            var travel = age < 1
                ? rest * Ease(age)
                : age < SlideOutStart ? rest : rest + (exit - rest) * Ease(age - SlideOutStart);

            var dot = new RectangleF(x: bar.X + group + slot + slide + travel, y: top, width: diameter, height: diameter);
            ui.Fill(dot, ui.Palette.Accent, radius: diameter / 2);
        }

        ui.PopClip();

        // The storyboard's KeySpline 0.4,0,0.6,1: a cubic Bézier from (0, 0) to (1, 1), solved for x by bisection.
        static float Ease(double progress)
        {
            const int Iterations = 20;
            double low = 0, high = 1;

            for (var i = 0; i < Iterations; i++)
            {
                var middle = (low + high) / 2;
                if (Bezier(middle, 0.4, 0.6) < progress) low = middle;
                else high = middle;
            }

            return (float)Bezier((low + high) / 2, 0, 1);
        }

        static double Bezier(double t, double first, double second) =>
            3 * (1 - t) * (1 - t) * t * first + 3 * (1 - t) * t * t * second + t * t * t;
    }

    public void Resume()
    {
        synchronizeInput = true;
        focusRequested = true;
    }

    public bool Focus(SurfaceUi ui)
    {
        if (!focusRequested || IsProgress) return false;

        focusRequested = false;
        ui.Focus(kind == DialogKind.Input ? InputId : "dialog.accept");
        return true;
    }

    /// <summary>
    /// Ends any press on the scroll bar and hides the message control while another dialog covers this one; the next
    /// draw shows it again.
    /// </summary>
    public void Suspend()
    {
        ScrollBar.MouseUp();
        Win32.ShowWindow(messageText, Win32.SwHide);
    }

    public bool Key(int key)
    {
        if (key == VirtualKey.A && Win32.IsKeyDown(VirtualKey.Control) && Win32.GetFocus() == messageText)
        {
            Win32.SendMessage(messageText, Win32.EmSetSelection, wParam: 0, lParam: -1);
            return true;
        }

        if (key == VirtualKey.Escape && !IsProgress)
        {
            Dispose();
            return true;
        }

        if (key == VirtualKey.Enter && !IsProgress)
        {
            Accept();
            return true;
        }

        return false;
    }

    public void Scroll(int steps)
    {
        const int LinesPerWheelStep = 3;
        ScrollToLine(FirstVisibleLine() - steps * LinesPerWheelStep);
    }

    /// <summary>Starts a press on the message's scroll bar; false when the point is not on it.</summary>
    public bool MouseDown(PointF point) => ScrollBar.MouseDown(point, Environment.TickCount64);

    /// <summary>True when the point is on the message's scroll bar while it is shown.</summary>
    public bool IsOnScrollBar(PointF point) => ScrollBar.IsVisible && ScrollBar.Bounds.Contains(point);

    /// <summary>Follows the pointer; true when the scroll bar's appearance or position changed.</summary>
    public bool MouseMove(PointF point) => ScrollBar.MouseMove(point);

    public void MouseUp() => ScrollBar.MouseUp();

    /// <summary>
    /// Repeats a held scroll bar step; true when the message scrolled since it was drawn, including by the control itself,
    /// such as while text is selected by dragging past its edge.
    /// </summary>
    public bool Tick(long now) => ScrollBar.Repeat(now) || FirstVisibleLine() != drawnFirstLine;

    private void ScrollToLine(int line)
    {
        const uint ScrollLines = 0x00B6;
        var target = Math.Clamp(line, 0, maximumFirstLine);
        Win32.SendMessage(messageText, ScrollLines, wParam: 0, lParam: target - FirstVisibleLine());
    }

    private int FirstVisibleLine()
    {
        const uint GetFirstVisibleLine = 0x00CE;
        return (int)Win32.SendMessage(messageText, GetFirstVisibleLine, wParam: 0, lParam: 0);
    }

    private void Accept() => Complete(input);

    public void Dispose() => Complete(result: null);

    private void Complete(string? result)
    {
        if (disposed) return;

        owner.DestroyControl(messageText);
        owner.RemoveDialog(this);
        disposed = true;
        completion.TrySetResult(result);
    }
}

internal sealed class NativeDialogs(Func<CanvasWindow> owner) : IUserDialogs
{
    public async Task ShowMessage(string title, string message)
    {
        using var dialog = owner().ShowDialog(title, message, kind: DialogKind.Message, affirmative: "OK");
        await dialog.Completion;
    }

    public async Task<bool> Confirm(
        string title,
        string message,
        string affirmative = "OK",
        string negative = "Cancel")
    {
        using var dialog = owner().ShowDialog(title, message, DialogKind.Confirmation, affirmative, negative);
        return await dialog.Completion != null;
    }

    public Task<string> Prompt(string title, string message) => Prompt(title, message, "OK");

    public async Task<string> Prompt(string title, string message, string affirmative)
    {
        using var dialog = owner().ShowDialog(title, message, DialogKind.Input, affirmative);
        return await dialog.Completion ?? "";
    }

    public IDisposable Progress(string title, string message) =>
        owner().ShowDialog(title, message, kind: DialogKind.Progress, affirmative: "");
}
