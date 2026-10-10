using System.Diagnostics.CodeAnalysis;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Rendering;

/// <summary>
/// A flip-model swap chain composed onto one window by DirectComposition, which presents without a redirection bitmap
/// (https://learn.microsoft.com/archive/msdn-magazine/2014/june/windows-with-c-high-performance-window-layering-using-the-windows-composition-engine).
/// Every frame paints an opaque background, so the swap chain ignores alpha; that lets text use ClearType, which
/// Direct2D cannot draw onto transparent targets.
/// </summary>
internal sealed class OverlaySurface(GraphicsDevice graphics, nint window) : IDisposable
{
    private const uint BufferCount = 2;

    private DxgiSwapChain? swapChain;
    private DCompositionTarget? target;
    private DCompositionVisual? visual;
    private D2DBitmap? targetBitmap;
    private int deviceGeneration;
    private bool drawing;

    private int PixelWidth { get; set; }

    private int PixelHeight { get; set; }

    /// <summary>False until the swap chain exists, and again after the shared device has been recreated.</summary>
    [MemberNotNullWhen(true, nameof(swapChain))]
    public bool IsCurrent => swapChain is not null && deviceGeneration == graphics.Generation;

    /// <summary>A PNG path that receives the next drawn frame before it is presented (diagnostic snapshots).</summary>
    public string? PendingSnapshotPath { get; set; }

    /// <summary>
    /// Makes the swap chain match the device and the window's pixel size; returns true when the buffers changed.
    /// </summary>
    public bool Ensure(int pixelWidth, int pixelHeight)
    {
        pixelWidth = Math.Max(pixelWidth, val2: 1);
        pixelHeight = Math.Max(pixelHeight, val2: 1);

        if (!IsCurrent)
        {
            Create(pixelWidth, pixelHeight);
            return true;
        }

        if (pixelWidth == PixelWidth && pixelHeight == PixelHeight) return false;

        graphics.Context.SetTarget(target: null);
        targetBitmap!.Dispose();
        targetBitmap = null;

        // A failure leaves no swap chain, so the next frame creates a complete one instead of drawing to a released
        // target.
        try
        {
            swapChain.ResizeBuffers(BufferCount, (uint)pixelWidth, (uint)pixelHeight);
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            targetBitmap = CreateTargetBitmap();
        }
        catch
        {
            Release();
            throw;
        }

        return true;
    }

    /// <summary>Targets the shared device context at this surface; the caller draws until EndAndPresent.</summary>
    public D2DDeviceContext Begin()
    {
        var context = graphics.Context;
        context.SetTarget(targetBitmap);
        context.BeginDraw();
        drawing = true;
        return context;
    }

    /// <summary>Ends a draw interrupted by an exception, so the shared context can begin the next one.</summary>
    public void AbortDraw()
    {
        if (!drawing) return;

        drawing = false;
        graphics.Context.EndDraw();
        graphics.Context.SetTarget(target: null);
    }

    /// <summary>Ends drawing and presents; device loss throws (see GraphicsDevice.IsDeviceLost).</summary>
    public void EndAndPresent()
    {
        var context = graphics.Context;
        int result = context.EndDraw();
        drawing = false;
        context.SetTarget(target: null);
        ComObject.Check(result);

        if (PendingSnapshotPath is { } path)
        {
            PendingSnapshotPath = null;
            SaveBackBuffer(path);
        }

        // Queue each surface without a per-window wait; DesktopWindow synchronizes animated batches with DWM.
        ComObject.Check(swapChain!.Present());
    }

    /// <summary>Releases a swap chain made on a replaced device, which otherwise keeps that device alive.</summary>
    public void ReleaseIfStale()
    {
        if (swapChain is not null && !IsCurrent) Release();
    }

    public void Dispose() => Release();

    private void Create(int pixelWidth, int pixelHeight)
    {
        Release();

        const uint RenderTargetOutput = 0x20;
        const uint StretchScaling = 0;
        const uint FlipSequential = 3;
        const uint IgnoreAlpha = 3;
        var description = new SwapChainDescription1(
            width: (uint)pixelWidth,
            height: (uint)pixelHeight,
            format: DxgiFormat.B8G8R8A8UNorm,
            stereo: 0,
            sampleDescription: new SampleDescription(count: 1, quality: 0),
            bufferUsage: RenderTargetOutput,
            bufferCount: BufferCount,
            scaling: StretchScaling,
            swapEffect: FlipSequential,
            alphaMode: IgnoreAlpha,
            flags: 0);

        try
        {
            swapChain = graphics.DxgiFactory.CreateSwapChainForComposition(graphics.Direct3DDevice, description);
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            targetBitmap = CreateTargetBitmap();
            target = graphics.Composition.CreateTargetForHwnd(window, isTopmost: false);
            visual = graphics.Composition.CreateVisual();
            visual.SetContent(swapChain);
            target.SetRoot(visual);
            graphics.Composition.Commit();
            deviceGeneration = graphics.Generation;
        }
        catch
        {
            Release();
            throw;
        }
    }

    // Copies the back buffer through a CPU-readable staging texture and encodes it as a PNG. The swap chain ignores
    // alpha, so the copy marks every pixel opaque.
    private unsafe void SaveBackBuffer(string path)
    {
        using var backBuffer = new D3D11Texture2D(swapChain!.GetBuffer(index: 0, D3D11Texture2D.Iid));

        const uint StagingUsage = 3;
        const uint CpuRead = 0x20000;
        var description = backBuffer.GetDescription() with
        {
            Usage = StagingUsage,
            BindFlags = 0,
            CpuAccessFlags = CpuRead,
            MiscFlags = 0,
        };

        using var staging = graphics.Direct3DDevice.CreateTexture2D(description);
        using var immediate = graphics.Direct3DDevice.GetImmediateContext();
        immediate.CopyResource(staging, backBuffer);
        var mapped = immediate.MapForReading(staging);

        byte[] pixels;
        try
        {
            var length = checked((int)(mapped.RowPitch * PixelHeight));
            pixels = new ReadOnlySpan<byte>((byte*)mapped.Data, length).ToArray();
        }
        finally
        {
            immediate.Unmap(staging);
        }

        const int AlphaOffset = 3;
        const int BytesPerPixel = 4;
        for (var i = AlphaOffset; i < pixels.Length; i += BytesPerPixel) pixels[i] = byte.MaxValue;

        using var file = graphics.ImagingFactory.CreateFileStream(path);
        fixed (byte* data = pixels)
        {
            graphics.ImagingFactory.EncodePng(
                file,
                PixelWidth,
                PixelHeight,
                mapped.RowPitch,
                data,
                WicFactory.PixelFormat32BppPbgra);
        }
    }

    private unsafe D2DBitmap CreateTargetBitmap()
    {
        using var surface = new ComObject(swapChain!.GetBuffer(index: 0, DxgiSwapChain.SurfaceIid));

        const uint IgnoreAlpha = 3;
        const uint TargetOptions = 1;
        const uint CannotDrawOptions = 2;
        var properties = new D2DBitmapProperties1(
            pixelFormat: new D2DPixelFormat(DxgiFormat.B8G8R8A8UNorm, IgnoreAlpha),
            dpiX: 96,
            dpiY: 96,
            options: TargetOptions | CannotDrawOptions,
            colorContext: 0);

        return graphics.Context.CreateBitmapFromDxgiSurface(surface, properties);
    }

    private void Release()
    {
        targetBitmap?.Dispose();
        visual?.Dispose();
        target?.Dispose();
        swapChain?.Dispose();
        targetBitmap = null;
        visual = null;
        target = null;
        swapChain = null;
    }
}
