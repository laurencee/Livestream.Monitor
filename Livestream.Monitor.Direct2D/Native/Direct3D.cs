using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

internal enum DxgiFormat : uint
{
    Unknown = 0,
    B8G8R8A8UNorm = 87,
}

/// <summary>DXGI_SAMPLE_DESC.</summary>
internal readonly struct SampleDescription(uint count, uint quality)
{
    public readonly uint Count = count;
    public readonly uint Quality = quality;
}

/// <summary>D3D11_TEXTURE2D_DESC.</summary>
internal struct Texture2DDescription(
    uint width,
    uint height,
    uint mipLevels,
    uint arraySize,
    DxgiFormat format,
    SampleDescription sampleDescription,
    uint usage,
    uint bindFlags,
    uint cpuAccessFlags,
    uint miscFlags)
{
    public uint Width = width;
    public uint Height = height;
    public uint MipLevels = mipLevels;
    public uint ArraySize = arraySize;
    public DxgiFormat Format = format;
    public SampleDescription SampleDescription = sampleDescription;
    public uint Usage = usage;
    public uint BindFlags = bindFlags;
    public uint CpuAccessFlags = cpuAccessFlags;
    public uint MiscFlags = miscFlags;
}

/// <summary>D3D11_MAPPED_SUBRESOURCE.</summary>
internal readonly struct MappedSubresource(nint data, uint rowPitch, uint depthPitch)
{
    public readonly nint Data = data;
    public readonly uint RowPitch = rowPitch;
    public readonly uint DepthPitch = depthPitch;
}

/// <summary>DXGI_SWAP_CHAIN_DESC1.</summary>
internal readonly struct SwapChainDescription1(
    uint width,
    uint height,
    DxgiFormat format,
    int stereo,
    SampleDescription sampleDescription,
    uint bufferUsage,
    uint bufferCount,
    uint scaling,
    uint swapEffect,
    uint alphaMode,
    uint flags)
{
    public readonly uint Width = width;
    public readonly uint Height = height;
    public readonly DxgiFormat Format = format;
    public readonly int Stereo = stereo;
    public readonly SampleDescription SampleDescription = sampleDescription;
    public readonly uint BufferUsage = bufferUsage;
    public readonly uint BufferCount = bufferCount;
    public readonly uint Scaling = scaling;
    public readonly uint SwapEffect = swapEffect;
    public readonly uint AlphaMode = alphaMode;
    public readonly uint Flags = flags;
}

/// <summary>ID3D11Device.</summary>
internal sealed unsafe partial class D3D11Device(void* pointer) : ComObject(pointer)
{
    private const int CreateTexture2DSlot = 5;
    private const int GetImmediateContextSlot = 40;
    private const uint SdkVersion = 7;

    /// <summary>Creates a device on the default adapter; returns the HRESULT, and the device on success.</summary>
    public static int Create(
        uint driverType,
        uint flags,
        ReadOnlySpan<uint> featureLevels,
        out D3D11Device? device)
    {
        void* created;
        int result;
        fixed (uint* levels = featureLevels)
        {
            result = D3D11CreateDevice(
                adapter: null,
                driverType,
                software: 0,
                flags,
                levels,
                (uint)featureLevels.Length,
                SdkVersion,
                &created,
                featureLevel: null,
                immediateContext: null);
        }

        device = result < 0 ? null : new D3D11Device(created);
        return result;
    }

    public D3D11Texture2D CreateTexture2D(in Texture2DDescription description)
    {
        var create =
            (delegate* unmanaged<void*, in Texture2DDescription, void*, out void*, int>)Methods[CreateTexture2DSlot];

        Check(create(
            Pointer,
            in description,
            null,
            out var texture));
        return new D3D11Texture2D(texture);
    }

    public D3D11DeviceContext GetImmediateContext()
    {
        ((delegate* unmanaged<void*, out void*, void>)Methods[GetImmediateContextSlot])(Pointer, out var context);
        return new D3D11DeviceContext(context);
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(
        void* adapter,
        uint driverType,
        nint software,
        uint flags,
        uint* featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        void** device,
        uint* featureLevel,
        void** immediateContext);
}

/// <summary>ID3D11DeviceContext.</summary>
internal sealed unsafe class D3D11DeviceContext(void* pointer) : ComObject(pointer)
{
    private const int MapSlot = 14;
    private const int UnmapSlot = 15;
    private const int CopyResourceSlot = 47;
    private const uint MapRead = 1;

    public void CopyResource(ComObject destination, ComObject source) =>
        ((delegate* unmanaged<void*, void*, void*, void>)Methods[CopyResourceSlot])(
            Pointer,
            destination.Pointer,
            source.Pointer);

    /// <summary>Maps a staging resource for reading.</summary>
    public MappedSubresource MapForReading(ComObject resource)
    {
        var map = (delegate* unmanaged<void*, void*, uint, uint, uint, out MappedSubresource, int>)Methods[MapSlot];
        Check(map(
            Pointer,
            resource.Pointer,
            0,
            MapRead,
            0,
            out var mapped));
        return mapped;
    }

    public void Unmap(ComObject resource) =>
        ((delegate* unmanaged<void*, void*, uint, void>)Methods[UnmapSlot])(Pointer, resource.Pointer, 0);
}

/// <summary>ID3D11Texture2D.</summary>
internal sealed unsafe class D3D11Texture2D(void* pointer) : ComObject(pointer)
{
    private const int GetDescriptionSlot = 10;

    public static readonly Guid Iid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    public Texture2DDescription GetDescription()
    {
        ((delegate* unmanaged<void*, out Texture2DDescription, void>)Methods[GetDescriptionSlot])(
            Pointer,
            out var description);

        return description;
    }
}

/// <summary>IDXGIObject; also used for the adapter, of which only the parent factory is needed.</summary>
internal unsafe class DxgiObject(void* pointer) : ComObject(pointer)
{
    private const int GetParentSlot = 6;

    /// <summary>The parent object as the given interface; the caller owns the returned pointer.</summary>
    public void* GetParent(Guid iid)
    {
        void* parent;
        Check(((delegate* unmanaged<void*, Guid*, void**, int>)Methods[GetParentSlot])(Pointer, &iid, &parent));
        return parent;
    }
}

/// <summary>IDXGIDevice.</summary>
internal sealed unsafe class DxgiDevice(void* pointer) : DxgiObject(pointer)
{
    private const int GetAdapterSlot = 7;

    public static readonly Guid Iid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    public DxgiObject GetAdapter()
    {
        Check(((delegate* unmanaged<void*, out void*, int>)Methods[GetAdapterSlot])(Pointer, out var adapter));
        return new DxgiObject(adapter);
    }
}

/// <summary>IDXGIFactory2.</summary>
internal sealed unsafe class DxgiFactory2(void* pointer) : ComObject(pointer)
{
    private const int CreateSwapChainForCompositionSlot = 24;

    public static readonly Guid Iid = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");

    public DxgiSwapChain CreateSwapChainForComposition(ComObject device, in SwapChainDescription1 description)
    {
        var create =
            (delegate* unmanaged<void*, void*, in SwapChainDescription1, void*, out void*, int>)Methods[
                CreateSwapChainForCompositionSlot];

        Check(create(
            Pointer,
            device.Pointer,
            in description,
            null,
            out var swapChain));
        return new DxgiSwapChain(swapChain);
    }
}

/// <summary>IDXGISwapChain1.</summary>
internal sealed unsafe class DxgiSwapChain(void* pointer) : ComObject(pointer)
{
    private const int PresentSlot = 8;
    private const int GetBufferSlot = 9;
    private const int ResizeBuffersSlot = 13;

    public static readonly Guid SurfaceIid = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

    /// <summary>Presents without waiting for vertical blank; returns the HRESULT.</summary>
    public int Present() => ((delegate* unmanaged<void*, uint, uint, int>)Methods[PresentSlot])(Pointer, 0, 0);

    /// <summary>A back buffer as the given interface; the caller owns the returned pointer.</summary>
    public void* GetBuffer(uint index, Guid iid)
    {
        void* buffer;
        var get = (delegate* unmanaged<void*, uint, Guid*, void**, int>)Methods[GetBufferSlot];
        Check(get(
            Pointer,
            index,
            &iid,
            &buffer));
        return buffer;
    }

    /// <summary>Resizes the buffers, keeping their count, format and flags.</summary>
    public void ResizeBuffers(uint bufferCount, uint width, uint height)
    {
        var resize = (delegate* unmanaged<void*, uint, uint, uint, DxgiFormat, uint, int>)Methods[ResizeBuffersSlot];
        Check(resize(
            Pointer,
            bufferCount,
            width,
            height,
            DxgiFormat.Unknown,
            0));
    }
}

/// <summary>IDCompositionDevice.</summary>
internal sealed unsafe partial class DCompositionDevice(void* pointer) : ComObject(pointer)
{
    private const int CommitSlot = 3;
    private const int CreateTargetForHwndSlot = 6;
    private const int CreateVisualSlot = 7;

    private static readonly Guid Iid = new("c37ea93a-e7aa-450d-b16f-9746cb0407f3");

    public static DCompositionDevice Create(DxgiDevice renderingDevice)
    {
        Check(DCompositionCreateDevice(renderingDevice.Pointer, in Iid, out var device));
        return new DCompositionDevice(device);
    }

    public void Commit() => Check(((delegate* unmanaged<void*, int>)Methods[CommitSlot])(Pointer));

    public DCompositionTarget CreateTargetForHwnd(nint window, bool isTopmost)
    {
        var create = (delegate* unmanaged<void*, nint, int, out void*, int>)Methods[CreateTargetForHwndSlot];
        Check(create(
            Pointer,
            window,
            isTopmost ? 1 : 0,
            out var target));
        return new DCompositionTarget(target);
    }

    public DCompositionVisual CreateVisual()
    {
        Check(((delegate* unmanaged<void*, out void*, int>)Methods[CreateVisualSlot])(Pointer, out var visual));
        return new DCompositionVisual(visual);
    }

    [LibraryImport("dcomp.dll")]
    private static partial int DCompositionCreateDevice(void* dxgiDevice, in Guid iid, out void* device);
}

/// <summary>IDCompositionTarget.</summary>
internal sealed unsafe class DCompositionTarget(void* pointer) : ComObject(pointer)
{
    private const int SetRootSlot = 3;

    public void SetRoot(DCompositionVisual visual) =>
        Check(((delegate* unmanaged<void*, void*, int>)Methods[SetRootSlot])(Pointer, visual.Pointer));
}

/// <summary>IDCompositionVisual.</summary>
internal sealed unsafe class DCompositionVisual(void* pointer) : ComObject(pointer)
{
    private const int SetContentSlot = 15;

    public void SetContent(ComObject content) =>
        Check(((delegate* unmanaged<void*, void*, int>)Methods[SetContentSlot])(Pointer, content.Pointer));
}
