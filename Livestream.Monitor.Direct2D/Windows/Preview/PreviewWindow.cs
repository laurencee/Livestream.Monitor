using System.Drawing;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;

namespace Livestream.Monitor.Direct2D.Windows.Preview;

/// <summary>
/// A click-through popup showing a stream or VOD thumbnail next to the pointer. Only one thumbnail is visible at a
/// time, so it is decoded into one reusable bitmap; downloaded thumbnails are kept encoded.
/// </summary>
internal sealed class PreviewWindow : PopupWindow
{
    private const int PreviewWidth = 640;
    private const int PreviewHeight = 360;
    private const float ImageInset = 2;
    private const uint Black = 0xFF000000;
    private readonly ThumbnailStore thumbnails;
    private readonly ReusableBitmap image;
    private string? source;
    private string? loadedSource;
    private bool showPending;

    public PreviewWindow(nint owner)
        : base(title: "Stream preview", width: PreviewWidth, height: PreviewHeight, owner: owner)
    {
        thumbnails = new ThumbnailStore(loaded: Invalidate);
        image = new ReusableBitmap(Graphics);
    }

    protected override uint? WindowBorderColor => Black;

    public void Display(string url, Win32.Point pointer)
    {
        source = url;
        var width = (int)(PreviewWidth * Scale);
        var height = (int)(PreviewHeight * Scale);
        const int PointerOffset = 18;
        var x = pointer.X + PointerOffset;
        var y = pointer.Y + PointerOffset;
        var position = Monitors.ClampToWorkArea(x, y, width, height, Program.MonitorName);
        var flags = Win32.SwpNoActivate | Win32.SwpFrameChanged;
        Win32.SetWindowPos(Handle, insertAfter: 0, position.X, position.Y, width, height, flags);

        // Shown once its thumbnail is presented, so neither a black window nor the previous thumbnail appears first.
        // A frame is drawn even for the thumbnail already presented, which a hidden window would otherwise never show.
        showPending = true;
        NeedsPaint = true;
        Tick();
    }

    public override void Hide()
    {
        showPending = false;
        base.Hide();
    }

    public override void Dispose()
    {
        thumbnails.Dispose();
        image.Dispose();
        base.Dispose();
    }

    protected override bool PaintsWhileHidden => showPending;

    protected override void BeforeFrame()
    {
        // A hidden preview keeps its source; it loads nothing until it is displayed again.
        if (!IsVisible && !showPending) return;

        // Decode at the drawn pixel size, so the bitmap is drawn without further scaling.
        var width = (int)MathF.Round((PreviewWidth - ImageInset * 2) * Scale);
        var height = (int)MathF.Round((PreviewHeight - ImageInset * 2) * Scale);
        var current = source == loadedSource && image.PixelWidth == width && image.PixelHeight == height;
        if (source == null || current) return;

        if (thumbnails.Get(source) is not { } encoded)
        {
            // Nothing to show until the download completes, which repaints; a visible preview hides rather than
            // showing a blank frame or the previous thumbnail.
            image.Clear();
            loadedSource = null;
            if (!IsVisible) return;

            Hide();
            showPending = true;
            return;
        }

        image.Load(encoded, width, height);
        loadedSource = source;
        if (image.Bitmap == null) Hide();
        NeedsPaint = true;
    }

    protected override void OnPresented()
    {
        if (!showPending || loadedSource != source) return;

        showPending = false;
        if (image.Bitmap != null) Show();
    }

    protected override void Draw(SizeF size)
    {
        var bounds = new RectangleF(PointF.Empty, size);
        Ui.Fill(bounds, Black);
        Ui.Image(image.Bitmap, RectangleF.Inflate(bounds, -ImageInset, -ImageInset), image.Revision);
        Ui.Border(bounds, Black, ImageInset);
    }
}
