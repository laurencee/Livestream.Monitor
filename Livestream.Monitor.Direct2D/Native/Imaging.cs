using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

/// <summary>IWICImagingFactory: image decoding and scaling to Direct2D-ready pixels, and PNG encoding.</summary>
internal sealed unsafe partial class WicFactory(void* pointer) : ComObject(pointer)
{
    private const int CreateDecoderFromStreamSlot = 4;
    private const int CreateEncoderSlot = 8;
    private const int CreateFormatConverterSlot = 10;
    private const int CreateBitmapScalerSlot = 11;
    private const int CreateStreamSlot = 14;

    public static readonly Guid PixelFormat32BppBgra = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");
    public static readonly Guid PixelFormat32BppPbgra = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

    private static readonly Guid ClassId = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid Iid = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid ContainerFormatPng = new("1b7cfaf4-713f-473c-bbcd-6137425faeaf");

    public static WicFactory Create()
    {
        const uint InProcessServer = 1;
        Check(CoCreateInstance(in ClassId, outer: null, InProcessServer, in Iid, out var factory));
        return new WicFactory(factory);
    }

    /// <summary>A stream that writes a new file at the path.</summary>
    public WicStream CreateFileStream(string path)
    {
        const uint GenericWrite = 0x40000000;
        var stream = CreateStream();
        try
        {
            stream.InitializeFromFilename(path, GenericWrite);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>A stream over memory that must stay pinned while the stream is used.</summary>
    public WicStream CreateMemoryStream(byte* data, int length)
    {
        var stream = CreateStream();
        try
        {
            stream.InitializeFromMemory(data, (uint)length);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Decodes the first frame of an image stream and converts it to the pixel format.</summary>
    public ComObject DecodeFirstFrame(ComObject stream, in Guid pixelFormat)
    {
        const uint CacheOnLoad = 1;
        var createDecoder =
            (delegate* unmanaged<void*, void*, void*, uint, out void*, int>)Methods[CreateDecoderFromStreamSlot];

        Check(createDecoder(
            Pointer,
            stream.Pointer,
            null,
            CacheOnLoad,
            out var decoderPointer));

        using var decoder = new WicDecoder(decoderPointer);
        using var frame = decoder.GetFrame(index: 0);
        var createConverter = (delegate* unmanaged<void*, out void*, int>)Methods[CreateFormatConverterSlot];
        Check(createConverter(Pointer, out var converter));
        var formatConverter = new WicFormatConverter(converter);

        try
        {
            formatConverter.Initialize(frame, pixelFormat);
            return formatConverter;
        }
        catch
        {
            formatConverter.Dispose();
            throw;
        }
    }

    /// <summary>Encodes 32-bit pixels, rows from the top, as a PNG into the stream.</summary>
    public void EncodePng(
        ComObject stream,
        int width,
        int height,
        uint stride,
        byte* pixels,
        in Guid pixelFormat)
    {
        const uint NoCache = 2;
        // Native calls receive pointers to stack copies, which the GC never moves, rather than to static fields.
        var container = ContainerFormatPng;
        var create = (delegate* unmanaged<void*, Guid*, void*, out void*, int>)Methods[CreateEncoderSlot];
        Check(create(
            Pointer,
            &container,
            null,
            out var encoderPointer));

        using var encoder = new WicEncoder(encoderPointer);
        encoder.Initialize(stream, NoCache);

        using var frame = encoder.CreateNewFrame();
        frame.Initialize();
        frame.SetSize((uint)width, (uint)height);
        frame.SetPixelFormat(pixelFormat);
        frame.WritePixels(
            (uint)height,
            stride,
            stride * (uint)height,
            pixels);
        frame.Commit();
        encoder.Commit();
    }

    /// <summary>Scales the source to the size with Fant interpolation, which suits downscaling photos.</summary>
    public WicBitmapScaler CreateScaler(ComObject source, int width, int height)
    {
        Check(((delegate* unmanaged<void*, out void*, int>)Methods[CreateBitmapScalerSlot])(Pointer, out var created));
        var scaler = new WicBitmapScaler(created);

        try
        {
            scaler.Initialize(source, (uint)width, (uint)height);
            return scaler;
        }
        catch
        {
            scaler.Dispose();
            throw;
        }
    }

    private WicStream CreateStream()
    {
        Check(((delegate* unmanaged<void*, out void*, int>)Methods[CreateStreamSlot])(Pointer, out var stream));
        return new WicStream(stream);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid classId,
        void* outer,
        uint context,
        in Guid iid,
        out void* instance);
}

/// <summary>IWICStream.</summary>
internal sealed unsafe class WicStream(void* pointer) : ComObject(pointer)
{
    private const int InitializeFromFilenameSlot = 15;
    private const int InitializeFromMemorySlot = 16;

    public void InitializeFromFilename(string path, uint access)
    {
        var initialize = (delegate* unmanaged<void*, char*, uint, int>)Methods[InitializeFromFilenameSlot];
        fixed (char* fileName = path) Check(initialize(Pointer, fileName, access));
    }

    public void InitializeFromMemory(byte* data, uint length)
    {
        var initialize = (delegate* unmanaged<void*, byte*, uint, int>)Methods[InitializeFromMemorySlot];
        Check(initialize(Pointer, data, length));
    }
}

/// <summary>IWICBitmapDecoder.</summary>
internal sealed unsafe class WicDecoder(void* pointer) : ComObject(pointer)
{
    private const int GetFrameSlot = 13;

    public ComObject GetFrame(uint index)
    {
        Check(((delegate* unmanaged<void*, uint, out void*, int>)Methods[GetFrameSlot])(Pointer, index, out var frame));
        return new ComObject(frame);
    }
}

/// <summary>IWICFormatConverter.</summary>
internal sealed unsafe class WicFormatConverter(void* pointer) : ComObject(pointer)
{
    private const int InitializeSlot = 8;

    /// <summary>Converts the source to the format without dithering or a palette.</summary>
    public void Initialize(ComObject source, Guid pixelFormat)
    {
        var initialize =
            (delegate* unmanaged<void*, void*, Guid*, uint, void*, double, uint, int>)Methods[InitializeSlot];

        Check(initialize(
            Pointer,
            source.Pointer,
            &pixelFormat,
            0,
            null,
            0,
            0));
    }
}

/// <summary>IWICBitmapEncoder.</summary>
internal sealed unsafe class WicEncoder(void* pointer) : ComObject(pointer)
{
    private const int InitializeSlot = 3;
    private const int CreateNewFrameSlot = 10;
    private const int CommitSlot = 11;

    public void Initialize(ComObject stream, uint cacheOption)
    {
        var initialize = (delegate* unmanaged<void*, void*, uint, int>)Methods[InitializeSlot];
        Check(initialize(Pointer, stream.Pointer, cacheOption));
    }

    /// <summary>A new frame without encoder options.</summary>
    public WicFrameEncode CreateNewFrame()
    {
        var create = (delegate* unmanaged<void*, out void*, void*, int>)Methods[CreateNewFrameSlot];
        Check(create(Pointer, out var frame, null));
        return new WicFrameEncode(frame);
    }

    public void Commit() => Check(((delegate* unmanaged<void*, int>)Methods[CommitSlot])(Pointer));
}

/// <summary>IWICBitmapFrameEncode.</summary>
internal sealed unsafe class WicFrameEncode(void* pointer) : ComObject(pointer)
{
    private const int InitializeSlot = 3;
    private const int SetSizeSlot = 4;
    private const int SetPixelFormatSlot = 6;
    private const int WritePixelsSlot = 10;
    private const int CommitSlot = 12;

    public void Initialize() => Check(((delegate* unmanaged<void*, void*, int>)Methods[InitializeSlot])(Pointer, null));

    public void SetSize(uint width, uint height) =>
        Check(((delegate* unmanaged<void*, uint, uint, int>)Methods[SetSizeSlot])(Pointer, width, height));

    /// <summary>Requests the pixel format; the encoder may substitute the closest one it supports.</summary>
    public void SetPixelFormat(Guid pixelFormat) =>
        Check(((delegate* unmanaged<void*, ref Guid, int>)Methods[SetPixelFormatSlot])(Pointer, ref pixelFormat));

    public void WritePixels(
        uint lineCount,
        uint stride,
        uint bufferSize,
        byte* pixels)
    {
        var write = (delegate* unmanaged<void*, uint, uint, uint, byte*, int>)Methods[WritePixelsSlot];
        Check(write(Pointer, lineCount, stride, bufferSize, pixels));
    }

    public void Commit() => Check(((delegate* unmanaged<void*, int>)Methods[CommitSlot])(Pointer));
}

/// <summary>IWICBitmapScaler, with the IWICBitmapSource pixel copy it inherits.</summary>
internal sealed unsafe class WicBitmapScaler(void* pointer) : ComObject(pointer)
{
    private const int CopyPixelsSlot = 7;
    private const int InitializeSlot = 8;

    public void Initialize(ComObject source, uint width, uint height)
    {
        const uint Fant = 3;
        var initialize = (delegate* unmanaged<void*, void*, uint, uint, uint, int>)Methods[InitializeSlot];
        Check(initialize(Pointer, source.Pointer, width, height, Fant));
    }

    /// <summary>Copies every scaled row, from the top, into the buffer.</summary>
    public void CopyPixels(uint stride, Span<byte> buffer)
    {
        var copy = (delegate* unmanaged<void*, void*, uint, uint, byte*, int>)Methods[CopyPixelsSlot];
        fixed (byte* pixels = buffer)
        {
            Check(copy(
                Pointer,
                null,
                stride,
                (uint)buffer.Length,
                pixels));
        }
    }
}
