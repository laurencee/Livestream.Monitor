using System.Runtime.ExceptionServices;
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

    private DeviceResources? resources;

    private D2DFactory D2DFactory { get; } = D2DFactory.Create();

    // Created on first use. The work before the first device call, e.g. window creation and layout, can then run while a
    // creation started by StartHardwareDeviceCreation is still in progress.
    private DeviceResources Resources => resources ??= DeviceResources.Create(D2DFactory, HardwareDeviceCreation.TakeStarted());

    /// <summary>A new device-independent path geometry.</summary>
    public D2DPathGeometry CreatePathGeometry() => D2DFactory.CreatePathGeometry();

    public DWriteFactory WriteFactory { get; } = DWriteFactory.Create();

    public WicFactory ImagingFactory { get; } = WicFactory.Create();

    public D3D11Device Direct3DDevice => Resources.Direct3DDevice!;

    public DxgiFactory2 DxgiFactory => Resources.DxgiFactory!;

    public D2DDeviceContext Context => Resources.Context!;

    public DCompositionDevice Composition => Resources.Composition!;

    /// <summary>Incremented by every device recreation; device-dependent resources compare it to know when to rebuild.</summary>
    public int Generation { get; private set; }

    /// <summary>
    /// Starts creating the hardware D3D11 device on a background thread. The first device objects take that device instead of
    /// loading the driver on the UI thread.
    /// </summary>
    public static void StartHardwareDeviceCreation() => HardwareDeviceCreation.Start();

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
        var replacement = DeviceResources.Create(D2DFactory, startedHardware: null);
        resources?.Dispose();
        resources = replacement;
        Generation++;
    }

    public void Dispose()
    {
        resources?.Dispose();
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

        /// <summary>
        /// Creates every device object, taking the hardware device from a started creation when one is given, and
        /// releases those already created when a later one fails.
        /// </summary>
        public static DeviceResources Create(D2DFactory factory, HardwareDeviceCreation? startedHardware)
        {
            var resources = new DeviceResources();
            try
            {
                resources.Initialize(factory, startedHardware);
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

        private unsafe void Initialize(D2DFactory factory, HardwareDeviceCreation? startedHardware)
        {
            // WARP keeps the UI usable on machines without a usable hardware adapter.
            var hardwareResult = startedHardware?.Take(out Direct3DDevice) ??
                D3D11Device.Create(HardwareDriver, CreationFlags, FeatureLevels, out Direct3DDevice);
            if (hardwareResult < 0) ComObject.Check(D3D11Device.Create(WarpDriver, CreationFlags, FeatureLevels, out Direct3DDevice));

            using var dxgiDevice = new DxgiDevice(Direct3DDevice!.QueryInterface(DxgiDevice.Iid));
            using var adapter = dxgiDevice.GetAdapter();
            DxgiFactory = new DxgiFactory2(adapter.GetParent(DxgiFactory2.Iid));
            direct2DDevice = factory.CreateDevice(dxgiDevice);
            Context = direct2DDevice.CreateDeviceContext();
            Context.UseClearTypeTextAntialiasing();
            Composition = DCompositionDevice.Create(dxgiDevice);
        }
    }

    /// <summary>
    /// The hardware device created on a background thread during startup, taken once by the first device objects. If nothing
    /// takes the device, because startup failed or exited first, it is left for process exit to release.
    /// </summary>
    private sealed class HardwareDeviceCreation
    {
        // Set and taken on the UI thread only, so it needs no synchronization; the creation thread never reads it.
        private static HardwareDeviceCreation? started;

        private readonly Thread thread;
        private D3D11Device? device;
        private int result;
        private ExceptionDispatchInfo? failure;

        private HardwareDeviceCreation()
        {
            // A background thread does not keep the process alive when startup exits before the device is taken.
            thread = new Thread(Create) { IsBackground = true, Name = "Direct3D device creation" };
        }

        public static void Start()
        {
            var creation = new HardwareDeviceCreation();
            started = creation;
            creation.thread.Start();
        }

        /// <summary>The started creation, once; null when none was started or it was already taken.</summary>
        public static HardwareDeviceCreation? TakeStarted()
        {
            var creation = started;
            started = null;
            return creation;
        }

        /// <summary>
        /// Waits for the creation and returns its HRESULT, with the device on success. If the creation threw, rethrows that
        /// exception, as creating the device on the calling thread would have thrown it.
        /// </summary>
        public int Take(out D3D11Device? created)
        {
            // Join also makes the thread's writes visible here. On this STA thread it pumps COM calls and sent messages meanwhile.
            thread.Join();
            failure?.Throw();

            created = device;
            return result;
        }

        private void Create()
        {
            // An exception escaping this thread would end the process; it is rethrown where the device is taken instead.
            try
            {
                result = D3D11Device.Create(HardwareDriver, CreationFlags, FeatureLevels, out device);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        }
    }
}
