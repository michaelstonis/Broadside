using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.Com;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// The device resources of one <see cref="Direct2DRenderer"/>: a Direct2D factory, a Direct3D 11 device (hardware or WARP) with the
/// Direct2D device and device context over it, one solid colour brush, and a target bitmap with a CPU-readable copy of the size last
/// drawn. Created once and reused for every render; released on <see cref="Dispose"/>.
/// </summary>
internal sealed unsafe class Direct2DSurface : IDisposable
{
    private ID2D1Factory1* _factory;
    private ID2D1Device* _device;
    private ID2D1DeviceContext* _context;
    private ID2D1SolidColorBrush* _brush;
    private ID2D1Bitmap1* _target;
    private ID2D1Bitmap1* _readback;
    private int _width;
    private int _height;

    public Direct2DSurface(Direct2DDriver driver)
    {
        try
        {
            Create(driver);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the driver the Direct3D device was created with.</summary>
    public Direct2DDriver Driver { get; private set; }

    public ID2D1Factory1* Factory => _factory;

    public ID2D1DeviceContext* Context => _context;

    public ID2D1SolidColorBrush* Brush => _brush;

    /// <summary>Gets the largest width and height, in pixels, of a bitmap the device can draw into.</summary>
    public int MaximumSize { get; private set; }

    /// <summary>
    /// Starts drawing a <paramref name="width"/> × <paramref name="height"/> area: white, or a copy of the top-left area of
    /// <paramref name="background"/> when <paramref name="white"/> is <see langword="false"/>.
    /// </summary>
    public void Begin(int width, int height, Bitmap background, bool white)
    {
        EnsureTarget(width, height);
        if (!white)
        {
            fixed (byte* pixels = background.Pixels)
            {
                var area = new D2D_RECT_U { left = 0, top = 0, right = (uint)width, bottom = (uint)height };
                _target->CopyFromMemory(&area, pixels, (uint)background.Stride);
            }
        }

        _context->SetTarget((ID2D1Image*)_target);
        _context->BeginDraw();
        D2D_MATRIX_3X2_F identity = Interop.Matrix(Broadside.Graphics.Matrix.Identity);
        _context->SetTransform(&identity);
        if (white)
        {
            var paper = new D2D1_COLOR_F { r = 1, g = 1, b = 1, a = 1 };
            _context->Clear(&paper);
        }
    }

    /// <summary>Ends drawing and returns what Direct2D reports: an error in any drawing call of the session surfaces here.</summary>
    public HRESULT End()
    {
        HRESULT result = _context->EndDraw(null, null);
        _context->SetTarget(null);
        return result;
    }

    /// <summary>Copies the drawn <paramref name="width"/> × <paramref name="height"/> area into the top-left of <paramref name="destination"/>.</summary>
    public void CopyTo(Bitmap destination, int width, int height)
    {
        _readback->CopyFromBitmap(null, (ID2D1Bitmap*)_target, null);
        D2D1_MAPPED_RECT mapped;
        _readback->Map(D2D1_MAP_OPTIONS.D2D1_MAP_OPTIONS_READ, &mapped);
        try
        {
            Span<byte> pixels = destination.Pixels;
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                new ReadOnlySpan<byte>(mapped.bits + ((long)y * mapped.pitch), rowBytes).CopyTo(pixels.Slice(y * destination.Stride, rowBytes));
            }
        }
        finally
        {
            _readback->Unmap();
        }
    }

    public void Dispose()
    {
        ReleaseTargets();
        Release(ref _brush);
        Release(ref _context);
        Release(ref _device);
        Release(ref _factory);
    }

    private static void Release<T>(ref T* pointer)
        where T : unmanaged
    {
        if (pointer is not null)
        {
            ((IUnknown*)pointer)->Release();
            pointer = null;
        }
    }

    private static HRESULT CreateDevice(D3D_DRIVER_TYPE type, ID3D11Device** device) =>
        PInvoke.D3D11CreateDevice(
            null,
            type,
            default,
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            null,
            0,
            PInvoke.D3D11_SDK_VERSION,
            device,
            null,
            null);

    private void Create(Direct2DDriver driver)
    {
        var options = default(D2D1_FACTORY_OPTIONS);
        Guid factoryId = ID2D1Factory1.IID_Guid;
        void* factory;
        PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, &factoryId, &options, &factory).ThrowOnFailure();
        _factory = (ID2D1Factory1*)factory;

        ID3D11Device* direct3D = null;
        if (driver == Direct2DDriver.Hardware && CreateDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, &direct3D).Failed)
        {
            // No Direct3D 11 hardware device (a server, a virtual machine, a remote session): WARP draws the same in software.
            direct3D = null;
            driver = Direct2DDriver.Warp;
        }

        if (driver == Direct2DDriver.Warp)
        {
            CreateDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, &direct3D).ThrowOnFailure();
        }

        Driver = driver;
        IDXGIDevice* dxgi = null;
        try
        {
            Guid dxgiId = IDXGIDevice.IID_Guid;
            direct3D->QueryInterface(&dxgiId, (void**)&dxgi).ThrowOnFailure();
            ID2D1Device* device;
            _factory->CreateDevice(dxgi, &device);
            _device = device;
            ID2D1DeviceContext* context;
            _device->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS.D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &context);
            _context = context;
        }
        finally
        {
            Release(ref dxgi);
            Release(ref direct3D);
        }

        // One unit is one pixel whatever the DPI, and every edge is anti-aliased like the software rasterizer's (§10.7.4).
        _context->SetUnitMode(D2D1_UNIT_MODE.D2D1_UNIT_MODE_PIXELS);
        _context->SetAntialiasMode(D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        MaximumSize = (int)Math.Min(_context->GetMaximumBitmapSize(), int.MaxValue);
        var black = new D2D1_COLOR_F { a = 1 };
        ID2D1SolidColorBrush* brush;
        _context->CreateSolidColorBrush(&black, null, &brush);
        _brush = brush;
    }

    private void EnsureTarget(int width, int height)
    {
        if (_target is not null && width == _width && height == _height)
        {
            return;
        }

        ReleaseTargets();
        var size = new D2D_SIZE_U { width = (uint)width, height = (uint)height };
        var properties = new D2D1_BITMAP_PROPERTIES1
        {
            // Premultiplied BGRA, eight bits a channel: the layout of Bitmap, so the pixels copy row by row.
            pixelFormat = new D2D1_PIXEL_FORMAT { format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED },
            dpiX = 96,
            dpiY = 96,
            bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET,
        };
        ID2D1Bitmap1* target;
        _context->CreateBitmap(size, null, 0, &properties, &target);
        _target = target;

        properties.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_CPU_READ | D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_CANNOT_DRAW;
        ID2D1Bitmap1* readback;
        _context->CreateBitmap(size, null, 0, &properties, &readback);
        _readback = readback;
        (_width, _height) = (width, height);
    }

    private void ReleaseTargets()
    {
        Release(ref _readback);
        Release(ref _target);
        (_width, _height) = (0, 0);
    }
}
