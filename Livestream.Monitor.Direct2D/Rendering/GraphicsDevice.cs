using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Rendering;

/// <summary>
/// The device objects shared by every overlay window: one D3D11 device, one Direct2D device context that each window
/// retargets to its own swap chain, and one DirectComposition device that presents the swap chains.
/// </summary>
/// <remarks>
/// Direct2D requires a BGRA-capable D3D11 device (https://learn.microsoft.com/windows/win32/direct2d/devices-and-device-contexts).
/// A device-removed error recreates everything through <see cref="Recreate"/>.
/// </remarks>
internal sealed class GraphicsDevice : IDisposable
{
    private const uint HardwareDriver = 1;
    private const uint WarpDriver = 5;
    private const uint BgraSupport = 0x20;

    // The overlay submits little work from one thread; driver worker threads (dozens on many-core machines) only add
    // memory and scheduling (https://learn.microsoft.com/windows/win32/api/d3d11/ne-d3d11-d3d11_create_device_flag).
    private const uint PreventInternalThreadingOptimizations = 0x8;
    private const uint CreationFlags = BgraSupport | PreventInternalThreadingOptimizations;

    private const uint FeatureLevel11Point1 = 0xB100;
    private const uint FeatureLevel11Point0 = 0xB000;
    private const uint FeatureLevel10Point1 = 0xA100;
    private const uint FeatureLevel10Point0 = 0xA000;
    private const uint FeatureLevel9Point3 = 0x9300;

    private static readonly uint[] FeatureLevels =
        [FeatureLevel11Point1, FeatureLevel11Point0, FeatureLevel10Point1, FeatureLevel10Point0, FeatureLevel9Point3];

    private DeviceResources resources;

    public GraphicsDevice()
    {
        D2DFactory = D2DFactory.Create();
        WriteFactory = DWriteFactory.Create();
        ImagingFactory = WicFactory.Create();
        resources = DeviceResources.Create(D2DFactory);
    }

    private D2DFactory D2DFactory { get; }

    /// <summary>A new device-independent path geometry.</summary>
    public D2DPathGeometry CreatePathGeometry() => D2DFactory.CreatePathGeometry();

    public DWriteFactory WriteFactory { get; }

    public WicFactory ImagingFactory { get; }

    public D3D11Device Direct3DDevice => resources.Direct3DDevice!;

    public DxgiFactory2 DxgiFactory => resources.DxgiFactory!;

    public D2DDeviceContext Context => resources.Context!;

    public DCompositionDevice Composition => resources.Composition!;

    /// <summary>Incremented by every device recreation; device-dependent resources compare it to rebuild.</summary>
    public int Generation { get; private set; }

    /// <summary>True for the HRESULTs reporting a removed or reset device, which any device call can return.</summary>
    public static bool IsDeviceLost(int result)
    {
        const int DeviceRemoved = unchecked((int)0x887A0005);
        const int DeviceReset = unchecked((int)0x887A0007);
        const int RecreateTarget = unchecked((int)0x8899000C);
        return result is DeviceRemoved or DeviceReset or RecreateTarget;
    }

    public void Recreate()
    {
        // Create the replacement first: if that fails, the lost objects stay valid to call (and fail again) instead of
        // being released wrappers.
        var replacement = DeviceResources.Create(D2DFactory);
        resources.Dispose();
        resources = replacement;
        Generation++;
    }

    public void Dispose()
    {
        resources.Dispose();
        ImagingFactory.Dispose();
        WriteFactory.Dispose();
        D2DFactory.Dispose();
    }

    private sealed class DeviceResources : IDisposable
    {
        public D3D11Device? Direct3DDevice;
        public DxgiFactory2? DxgiFactory;
        private D2DDevice? direct2DDevice;
        public D2DDeviceContext? Context;
        public DCompositionDevice? Composition;

        /// <summary>Creates every device object, releasing those already created when a later one fails.</summary>
        public static DeviceResources Create(D2DFactory factory)
        {
            var resources = new DeviceResources();
            try
            {
                resources.Initialize(factory);
                return resources;
            }
            catch
            {
                resources.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Composition?.Dispose();
            Context?.Dispose();
            direct2DDevice?.Dispose();
            DxgiFactory?.Dispose();
            Direct3DDevice?.Dispose();
        }

        private unsafe void Initialize(D2DFactory factory)
        {
            // WARP keeps the UI usable on machines without a usable hardware adapter.
            if (D3D11Device.Create(HardwareDriver, CreationFlags, FeatureLevels, out Direct3DDevice) < 0)
                ComObject.Check(D3D11Device.Create(WarpDriver, CreationFlags, FeatureLevels, out Direct3DDevice));

            using var dxgiDevice = new DxgiDevice(Direct3DDevice!.QueryInterface(DxgiDevice.Iid));
            using var adapter = dxgiDevice.GetAdapter();
            DxgiFactory = new DxgiFactory2(adapter.GetParent(DxgiFactory2.Iid));
            direct2DDevice = factory.CreateDevice(dxgiDevice);
            Context = direct2DDevice.CreateDeviceContext();
            Context.UseClearTypeTextAntialiasing();
            Composition = DCompositionDevice.Create(dxgiDevice);
        }
    }
}
