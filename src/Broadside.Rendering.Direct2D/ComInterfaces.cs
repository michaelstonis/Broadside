#pragma warning disable CS0649 // _vtbl is the COM object's own first field, written by the native side.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Win32 = global::Windows.Win32;

namespace Broadside.Rendering.Direct2D;

// Hand-written, blittable COM vtable structs for the Direct2D, DirectWrite, Direct3D 11 and WIC interfaces the backend calls.
// Each struct is a COM object pointer's pointee: a single vtable pointer. A method is a function-pointer call through the
// slot given by the interface's declaration order in the Windows SDK headers (d2d1.h, d2d1_1.h, dwrite_3.h, wincodec.h,
// d3d11.h, dxgi.h); an inherited method keeps its base interface's slot. Only the methods the backend calls are declared, so
// the assembly does not carry the rest of each interface (and the callable wrappers CsWin32 emits for all of it). Value
// types (structures, enumerations, HRESULT, the GUIDs) still come from CsWin32; see NativeMethods.txt.

internal unsafe struct ID2D1Bitmap
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1Bitmap</c>.</summary>
    public static Guid IID_Guid { get; } = new("A2296057-EA42-4099-983B-539FB6505426");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Bitmap*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1Bitmap*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Bitmap*, uint>)_vtbl[1])((ID2D1Bitmap*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Bitmap*, uint>)_vtbl[2])((ID2D1Bitmap*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct ID2D1Bitmap1
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1Bitmap1</c>.</summary>
    public static Guid IID_Guid { get; } = new("A898A84C-3873-4588-B08B-EBBF978DF041");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Bitmap1*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1Bitmap1*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Bitmap1*, uint>)_vtbl[1])((ID2D1Bitmap1*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Bitmap1*, uint>)_vtbl[2])((ID2D1Bitmap1*)Unsafe.AsPointer(ref this));
    }

    public void CopyFromBitmap(Win32.Graphics.Direct2D.Common.D2D_POINT_2U* destPoint, ID2D1Bitmap* bitmap, Win32.Graphics.Direct2D.Common.D2D_RECT_U* srcRect)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Bitmap1*, Win32.Graphics.Direct2D.Common.D2D_POINT_2U*, ID2D1Bitmap*, Win32.Graphics.Direct2D.Common.D2D_RECT_U*, Win32.Foundation.HRESULT>)_vtbl[8])((ID2D1Bitmap1*)Unsafe.AsPointer(ref this), destPoint, bitmap, srcRect).ThrowOnFailure();
    }

    public void CopyFromMemory(Win32.Graphics.Direct2D.Common.D2D_RECT_U* dstRect, void* srcData, uint pitch)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Bitmap1*, Win32.Graphics.Direct2D.Common.D2D_RECT_U*, void*, uint, Win32.Foundation.HRESULT>)_vtbl[10])((ID2D1Bitmap1*)Unsafe.AsPointer(ref this), dstRect, srcData, pitch).ThrowOnFailure();
    }

    public void Map(Win32.Graphics.Direct2D.D2D1_MAP_OPTIONS options, Win32.Graphics.Direct2D.D2D1_MAPPED_RECT* mappedRect)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Bitmap1*, Win32.Graphics.Direct2D.D2D1_MAP_OPTIONS, Win32.Graphics.Direct2D.D2D1_MAPPED_RECT*, Win32.Foundation.HRESULT>)_vtbl[14])((ID2D1Bitmap1*)Unsafe.AsPointer(ref this), options, mappedRect).ThrowOnFailure();
    }

    public void Unmap()
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Bitmap1*, Win32.Foundation.HRESULT>)_vtbl[15])((ID2D1Bitmap1*)Unsafe.AsPointer(ref this)).ThrowOnFailure();
    }

}

internal unsafe struct ID2D1BitmapBrush
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1BitmapBrush</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD906AA-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1BitmapBrush*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush*, uint>)_vtbl[1])((ID2D1BitmapBrush*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush*, uint>)_vtbl[2])((ID2D1BitmapBrush*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct ID2D1BitmapBrush1
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1BitmapBrush1</c>.</summary>
    public static Guid IID_Guid { get; } = new("41343A53-E41A-49A2-91CD-21793BBB62E5");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush1*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1BitmapBrush1*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush1*, uint>)_vtbl[1])((ID2D1BitmapBrush1*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush1*, uint>)_vtbl[2])((ID2D1BitmapBrush1*)Unsafe.AsPointer(ref this));
    }

    public void SetOpacity(float opacity)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush1*, float, void>)_vtbl[4])((ID2D1BitmapBrush1*)Unsafe.AsPointer(ref this), opacity);
    }

    public void SetTransform(Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F* transform)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush1*, Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F*, void>)_vtbl[5])((ID2D1BitmapBrush1*)Unsafe.AsPointer(ref this), transform);
    }

    public void SetExtendModeX(Win32.Graphics.Direct2D.D2D1_EXTEND_MODE extendModeX)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush1*, Win32.Graphics.Direct2D.D2D1_EXTEND_MODE, void>)_vtbl[8])((ID2D1BitmapBrush1*)Unsafe.AsPointer(ref this), extendModeX);
    }

    public void SetExtendModeY(Win32.Graphics.Direct2D.D2D1_EXTEND_MODE extendModeY)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush1*, Win32.Graphics.Direct2D.D2D1_EXTEND_MODE, void>)_vtbl[9])((ID2D1BitmapBrush1*)Unsafe.AsPointer(ref this), extendModeY);
    }

    public void SetBitmap(ID2D1Bitmap* bitmap)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush1*, ID2D1Bitmap*, void>)_vtbl[11])((ID2D1BitmapBrush1*)Unsafe.AsPointer(ref this), bitmap);
    }

    public void SetInterpolationMode1(Win32.Graphics.Direct2D.D2D1_INTERPOLATION_MODE interpolationMode)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1BitmapBrush1*, Win32.Graphics.Direct2D.D2D1_INTERPOLATION_MODE, void>)_vtbl[16])((ID2D1BitmapBrush1*)Unsafe.AsPointer(ref this), interpolationMode);
    }

}

internal unsafe struct ID2D1Brush
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1Brush</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD906A8-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Brush*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1Brush*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Brush*, uint>)_vtbl[1])((ID2D1Brush*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Brush*, uint>)_vtbl[2])((ID2D1Brush*)Unsafe.AsPointer(ref this));
    }

    public void SetOpacity(float opacity)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Brush*, float, void>)_vtbl[4])((ID2D1Brush*)Unsafe.AsPointer(ref this), opacity);
    }

    public void SetTransform(Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F* transform)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Brush*, Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F*, void>)_vtbl[5])((ID2D1Brush*)Unsafe.AsPointer(ref this), transform);
    }

}

internal unsafe struct ID2D1Device
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1Device</c>.</summary>
    public static Guid IID_Guid { get; } = new("47DD575D-AC05-4CDD-8049-9B02CD16F44C");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Device*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1Device*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Device*, uint>)_vtbl[1])((ID2D1Device*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Device*, uint>)_vtbl[2])((ID2D1Device*)Unsafe.AsPointer(ref this));
    }

    public void CreateDeviceContext(Win32.Graphics.Direct2D.D2D1_DEVICE_CONTEXT_OPTIONS options, ID2D1DeviceContext** deviceContext)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Device*, Win32.Graphics.Direct2D.D2D1_DEVICE_CONTEXT_OPTIONS, ID2D1DeviceContext**, Win32.Foundation.HRESULT>)_vtbl[4])((ID2D1Device*)Unsafe.AsPointer(ref this), options, deviceContext).ThrowOnFailure();
    }

}

internal unsafe struct ID2D1DeviceContext
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1DeviceContext</c>.</summary>
    public static Guid IID_Guid { get; } = new("E8F7FE7A-191C-466D-AD95-975678BDA998");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, uint>)_vtbl[1])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, uint>)_vtbl[2])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this));
    }

    public void CreateBitmap(Win32.Graphics.Direct2D.Common.D2D_SIZE_U size, void* srcData, uint pitch, Win32.Graphics.Direct2D.D2D1_BITMAP_PROPERTIES* bitmapProperties, ID2D1Bitmap** bitmap)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D_SIZE_U, void*, uint, Win32.Graphics.Direct2D.D2D1_BITMAP_PROPERTIES*, ID2D1Bitmap**, Win32.Foundation.HRESULT>)_vtbl[4])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), size, srcData, pitch, bitmapProperties, bitmap).ThrowOnFailure();
    }

    public void CreateBitmapBrush(ID2D1Bitmap* bitmap, Win32.Graphics.Direct2D.D2D1_BITMAP_BRUSH_PROPERTIES* bitmapBrushProperties, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES* brushProperties, ID2D1BitmapBrush** bitmapBrush)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, ID2D1Bitmap*, Win32.Graphics.Direct2D.D2D1_BITMAP_BRUSH_PROPERTIES*, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES*, ID2D1BitmapBrush**, Win32.Foundation.HRESULT>)_vtbl[7])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), bitmap, bitmapBrushProperties, brushProperties, bitmapBrush).ThrowOnFailure();
    }

    public void CreateSolidColorBrush(Win32.Graphics.Direct2D.Common.D2D1_COLOR_F* color, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES* brushProperties, ID2D1SolidColorBrush** solidColorBrush)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D1_COLOR_F*, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES*, ID2D1SolidColorBrush**, Win32.Foundation.HRESULT>)_vtbl[8])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), color, brushProperties, solidColorBrush).ThrowOnFailure();
    }

    public void CreateGradientStopCollection(Win32.Graphics.Direct2D.Common.D2D1_GRADIENT_STOP* gradientStops, uint gradientStopsCount, Win32.Graphics.Direct2D.D2D1_GAMMA colorInterpolationGamma, Win32.Graphics.Direct2D.D2D1_EXTEND_MODE extendMode, ID2D1GradientStopCollection** gradientStopCollection)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D1_GRADIENT_STOP*, uint, Win32.Graphics.Direct2D.D2D1_GAMMA, Win32.Graphics.Direct2D.D2D1_EXTEND_MODE, ID2D1GradientStopCollection**, Win32.Foundation.HRESULT>)_vtbl[9])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), gradientStops, gradientStopsCount, colorInterpolationGamma, extendMode, gradientStopCollection).ThrowOnFailure();
    }

    public void CreateLinearGradientBrush(Win32.Graphics.Direct2D.D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES* linearGradientBrushProperties, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES* brushProperties, ID2D1GradientStopCollection* gradientStopCollection, ID2D1LinearGradientBrush** linearGradientBrush)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES*, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES*, ID2D1GradientStopCollection*, ID2D1LinearGradientBrush**, Win32.Foundation.HRESULT>)_vtbl[10])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), linearGradientBrushProperties, brushProperties, gradientStopCollection, linearGradientBrush).ThrowOnFailure();
    }

    public void CreateRadialGradientBrush(Win32.Graphics.Direct2D.D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES* radialGradientBrushProperties, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES* brushProperties, ID2D1GradientStopCollection* gradientStopCollection, ID2D1RadialGradientBrush** radialGradientBrush)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES*, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES*, ID2D1GradientStopCollection*, ID2D1RadialGradientBrush**, Win32.Foundation.HRESULT>)_vtbl[11])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), radialGradientBrushProperties, brushProperties, gradientStopCollection, radialGradientBrush).ThrowOnFailure();
    }

    public void FillRectangle(Win32.Graphics.Direct2D.Common.D2D_RECT_F* rect, ID2D1Brush* brush)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D_RECT_F*, ID2D1Brush*, void>)_vtbl[17])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), rect, brush);
    }

    public void DrawGeometry(ID2D1Geometry* geometry, ID2D1Brush* brush, float strokeWidth, ID2D1StrokeStyle* strokeStyle)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, ID2D1Geometry*, ID2D1Brush*, float, ID2D1StrokeStyle*, void>)_vtbl[22])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), geometry, brush, strokeWidth, strokeStyle);
    }

    public void FillGeometry(ID2D1Geometry* geometry, ID2D1Brush* brush, ID2D1Brush* opacityBrush)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, ID2D1Geometry*, ID2D1Brush*, ID2D1Brush*, void>)_vtbl[23])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), geometry, brush, opacityBrush);
    }

    public void DrawBitmap(ID2D1Bitmap* bitmap, Win32.Graphics.Direct2D.Common.D2D_RECT_F* destinationRectangle, float opacity, Win32.Graphics.Direct2D.D2D1_BITMAP_INTERPOLATION_MODE interpolationMode, Win32.Graphics.Direct2D.Common.D2D_RECT_F* sourceRectangle)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, ID2D1Bitmap*, Win32.Graphics.Direct2D.Common.D2D_RECT_F*, float, Win32.Graphics.Direct2D.D2D1_BITMAP_INTERPOLATION_MODE, Win32.Graphics.Direct2D.Common.D2D_RECT_F*, void>)_vtbl[26])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), bitmap, destinationRectangle, opacity, interpolationMode, sourceRectangle);
    }

    public void DrawGlyphRun(Win32.Graphics.Direct2D.Common.D2D_POINT_2F baselineOrigin, DWRITE_GLYPH_RUN* glyphRun, ID2D1Brush* foregroundBrush, Win32.Graphics.DirectWrite.DWRITE_MEASURING_MODE measuringMode)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D_POINT_2F, DWRITE_GLYPH_RUN*, ID2D1Brush*, Win32.Graphics.DirectWrite.DWRITE_MEASURING_MODE, void>)_vtbl[29])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), baselineOrigin, glyphRun, foregroundBrush, measuringMode);
    }

    public void SetTransform(Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F* transform)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F*, void>)_vtbl[30])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), transform);
    }

    public void SetAntialiasMode(Win32.Graphics.Direct2D.D2D1_ANTIALIAS_MODE antialiasMode)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.D2D1_ANTIALIAS_MODE, void>)_vtbl[32])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), antialiasMode);
    }

    public void SetTextAntialiasMode(Win32.Graphics.Direct2D.D2D1_TEXT_ANTIALIAS_MODE textAntialiasMode)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.D2D1_TEXT_ANTIALIAS_MODE, void>)_vtbl[34])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), textAntialiasMode);
    }

    public void SetTextRenderingParams(IDWriteRenderingParams* textRenderingParams)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, IDWriteRenderingParams*, void>)_vtbl[36])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), textRenderingParams);
    }

    public void PushLayer(D2D1_LAYER_PARAMETERS* layerParameters, ID2D1Layer* layer)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, D2D1_LAYER_PARAMETERS*, ID2D1Layer*, void>)_vtbl[40])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), layerParameters, layer);
    }

    public void PopLayer()
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, void>)_vtbl[41])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this));
    }

    public void PushAxisAlignedClip(Win32.Graphics.Direct2D.Common.D2D_RECT_F* clipRect, Win32.Graphics.Direct2D.D2D1_ANTIALIAS_MODE antialiasMode)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D_RECT_F*, Win32.Graphics.Direct2D.D2D1_ANTIALIAS_MODE, void>)_vtbl[45])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), clipRect, antialiasMode);
    }

    public void PopAxisAlignedClip()
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, void>)_vtbl[46])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this));
    }

    public void Clear(Win32.Graphics.Direct2D.Common.D2D1_COLOR_F* clearColor)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D1_COLOR_F*, void>)_vtbl[47])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), clearColor);
    }

    public void BeginDraw()
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, void>)_vtbl[48])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this));
    }

    public Win32.Foundation.HRESULT EndDraw(ulong* tag1, ulong* tag2)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, ulong*, ulong*, Win32.Foundation.HRESULT>)_vtbl[49])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), tag1, tag2);
    }

    public uint GetMaximumBitmapSize()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, uint>)_vtbl[55])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this));
    }

    public void CreateBitmap(Win32.Graphics.Direct2D.Common.D2D_SIZE_U size, void* sourceData, uint pitch, D2D1_BITMAP_PROPERTIES1* bitmapProperties, ID2D1Bitmap1** bitmap)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D_SIZE_U, void*, uint, D2D1_BITMAP_PROPERTIES1*, ID2D1Bitmap1**, Win32.Foundation.HRESULT>)_vtbl[57])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), size, sourceData, pitch, bitmapProperties, bitmap).ThrowOnFailure();
    }

    public void CreateGradientStopCollection(Win32.Graphics.Direct2D.Common.D2D1_GRADIENT_STOP* straightAlphaGradientStops, uint straightAlphaGradientStopsCount, Win32.Graphics.Direct2D.D2D1_COLOR_SPACE preInterpolationSpace, Win32.Graphics.Direct2D.D2D1_COLOR_SPACE postInterpolationSpace, Win32.Graphics.Direct2D.D2D1_BUFFER_PRECISION bufferPrecision, Win32.Graphics.Direct2D.D2D1_EXTEND_MODE extendMode, Win32.Graphics.Direct2D.D2D1_COLOR_INTERPOLATION_MODE colorInterpolationMode, ID2D1GradientStopCollection1** gradientStopCollection1)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D1_GRADIENT_STOP*, uint, Win32.Graphics.Direct2D.D2D1_COLOR_SPACE, Win32.Graphics.Direct2D.D2D1_COLOR_SPACE, Win32.Graphics.Direct2D.D2D1_BUFFER_PRECISION, Win32.Graphics.Direct2D.D2D1_EXTEND_MODE, Win32.Graphics.Direct2D.D2D1_COLOR_INTERPOLATION_MODE, ID2D1GradientStopCollection1**, Win32.Foundation.HRESULT>)_vtbl[64])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), straightAlphaGradientStops, straightAlphaGradientStopsCount, preInterpolationSpace, postInterpolationSpace, bufferPrecision, extendMode, colorInterpolationMode, gradientStopCollection1).ThrowOnFailure();
    }

    public void CreateBitmapBrush(ID2D1Bitmap* bitmap, Win32.Graphics.Direct2D.D2D1_BITMAP_BRUSH_PROPERTIES1* bitmapBrushProperties, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES* brushProperties, ID2D1BitmapBrush1** bitmapBrush)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, ID2D1Bitmap*, Win32.Graphics.Direct2D.D2D1_BITMAP_BRUSH_PROPERTIES1*, Win32.Graphics.Direct2D.D2D1_BRUSH_PROPERTIES*, ID2D1BitmapBrush1**, Win32.Foundation.HRESULT>)_vtbl[66])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), bitmap, bitmapBrushProperties, brushProperties, bitmapBrush).ThrowOnFailure();
    }

    public void SetTarget(ID2D1Image* image)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, ID2D1Image*, void>)_vtbl[74])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), image);
    }

    public void SetUnitMode(Win32.Graphics.Direct2D.D2D1_UNIT_MODE unitMode)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.D2D1_UNIT_MODE, void>)_vtbl[80])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), unitMode);
    }

    public void DrawGlyphRun(Win32.Graphics.Direct2D.Common.D2D_POINT_2F baselineOrigin, DWRITE_GLYPH_RUN* glyphRun, Win32.Graphics.DirectWrite.DWRITE_GLYPH_RUN_DESCRIPTION* glyphRunDescription, ID2D1Brush* foregroundBrush, Win32.Graphics.DirectWrite.DWRITE_MEASURING_MODE measuringMode)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, Win32.Graphics.Direct2D.Common.D2D_POINT_2F, DWRITE_GLYPH_RUN*, Win32.Graphics.DirectWrite.DWRITE_GLYPH_RUN_DESCRIPTION*, ID2D1Brush*, Win32.Graphics.DirectWrite.DWRITE_MEASURING_MODE, void>)_vtbl[82])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), baselineOrigin, glyphRun, glyphRunDescription, foregroundBrush, measuringMode);
    }

    public void DrawBitmap(ID2D1Bitmap* bitmap, Win32.Graphics.Direct2D.Common.D2D_RECT_F* destinationRectangle, float opacity, Win32.Graphics.Direct2D.D2D1_INTERPOLATION_MODE interpolationMode, Win32.Graphics.Direct2D.Common.D2D_RECT_F* sourceRectangle, Win32.Graphics.Direct2D.Common.D2D_MATRIX_4X4_F* perspectiveTransform)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, ID2D1Bitmap*, Win32.Graphics.Direct2D.Common.D2D_RECT_F*, float, Win32.Graphics.Direct2D.D2D1_INTERPOLATION_MODE, Win32.Graphics.Direct2D.Common.D2D_RECT_F*, Win32.Graphics.Direct2D.Common.D2D_MATRIX_4X4_F*, void>)_vtbl[85])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), bitmap, destinationRectangle, opacity, interpolationMode, sourceRectangle, perspectiveTransform);
    }

    public void PushLayer(D2D1_LAYER_PARAMETERS1* layerParameters, ID2D1Layer* layer)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1DeviceContext*, D2D1_LAYER_PARAMETERS1*, ID2D1Layer*, void>)_vtbl[86])((ID2D1DeviceContext*)Unsafe.AsPointer(ref this), layerParameters, layer);
    }

}

internal unsafe struct ID2D1Factory
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1Factory</c>.</summary>
    public static Guid IID_Guid { get; } = new("06152247-6F50-465A-9245-118BFD3B6007");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Factory*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1Factory*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Factory*, uint>)_vtbl[1])((ID2D1Factory*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Factory*, uint>)_vtbl[2])((ID2D1Factory*)Unsafe.AsPointer(ref this));
    }

    public void CreatePathGeometry(ID2D1PathGeometry** pathGeometry)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Factory*, ID2D1PathGeometry**, Win32.Foundation.HRESULT>)_vtbl[10])((ID2D1Factory*)Unsafe.AsPointer(ref this), pathGeometry).ThrowOnFailure();
    }

    public void CreateStrokeStyle(Win32.Graphics.Direct2D.D2D1_STROKE_STYLE_PROPERTIES* strokeStyleProperties, float* dashes, uint dashesCount, ID2D1StrokeStyle** strokeStyle)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Factory*, Win32.Graphics.Direct2D.D2D1_STROKE_STYLE_PROPERTIES*, float*, uint, ID2D1StrokeStyle**, Win32.Foundation.HRESULT>)_vtbl[11])((ID2D1Factory*)Unsafe.AsPointer(ref this), strokeStyleProperties, dashes, dashesCount, strokeStyle).ThrowOnFailure();
    }

}

internal unsafe struct ID2D1Factory1
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1Factory1</c>.</summary>
    public static Guid IID_Guid { get; } = new("BB12D362-DAEE-4B9A-AA1D-14BA401CFA1F");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Factory1*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1Factory1*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Factory1*, uint>)_vtbl[1])((ID2D1Factory1*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Factory1*, uint>)_vtbl[2])((ID2D1Factory1*)Unsafe.AsPointer(ref this));
    }

    public void CreatePathGeometry(ID2D1PathGeometry** pathGeometry)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Factory1*, ID2D1PathGeometry**, Win32.Foundation.HRESULT>)_vtbl[10])((ID2D1Factory1*)Unsafe.AsPointer(ref this), pathGeometry).ThrowOnFailure();
    }

    public void CreateStrokeStyle(Win32.Graphics.Direct2D.D2D1_STROKE_STYLE_PROPERTIES* strokeStyleProperties, float* dashes, uint dashesCount, ID2D1StrokeStyle** strokeStyle)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Factory1*, Win32.Graphics.Direct2D.D2D1_STROKE_STYLE_PROPERTIES*, float*, uint, ID2D1StrokeStyle**, Win32.Foundation.HRESULT>)_vtbl[11])((ID2D1Factory1*)Unsafe.AsPointer(ref this), strokeStyleProperties, dashes, dashesCount, strokeStyle).ThrowOnFailure();
    }

    public void CreateDevice(IDXGIDevice* dxgiDevice, ID2D1Device** d2dDevice)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Factory1*, IDXGIDevice*, ID2D1Device**, Win32.Foundation.HRESULT>)_vtbl[17])((ID2D1Factory1*)Unsafe.AsPointer(ref this), dxgiDevice, d2dDevice).ThrowOnFailure();
    }

    public void CreateStrokeStyle(Win32.Graphics.Direct2D.D2D1_STROKE_STYLE_PROPERTIES1* strokeStyleProperties, float* dashes, uint dashesCount, ID2D1StrokeStyle1** strokeStyle)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Factory1*, Win32.Graphics.Direct2D.D2D1_STROKE_STYLE_PROPERTIES1*, float*, uint, ID2D1StrokeStyle1**, Win32.Foundation.HRESULT>)_vtbl[18])((ID2D1Factory1*)Unsafe.AsPointer(ref this), strokeStyleProperties, dashes, dashesCount, strokeStyle).ThrowOnFailure();
    }

    public void CreatePathGeometry(ID2D1PathGeometry1** pathGeometry)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Factory1*, ID2D1PathGeometry1**, Win32.Foundation.HRESULT>)_vtbl[19])((ID2D1Factory1*)Unsafe.AsPointer(ref this), pathGeometry).ThrowOnFailure();
    }

}

internal unsafe struct ID2D1Geometry
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1Geometry</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD906A1-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Geometry*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1Geometry*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Geometry*, uint>)_vtbl[1])((ID2D1Geometry*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Geometry*, uint>)_vtbl[2])((ID2D1Geometry*)Unsafe.AsPointer(ref this));
    }

    public void GetBounds(Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F* worldTransform, Win32.Graphics.Direct2D.Common.D2D_RECT_F* bounds)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1Geometry*, Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F*, Win32.Graphics.Direct2D.Common.D2D_RECT_F*, Win32.Foundation.HRESULT>)_vtbl[4])((ID2D1Geometry*)Unsafe.AsPointer(ref this), worldTransform, bounds).ThrowOnFailure();
    }

}

internal unsafe struct ID2D1GeometrySink
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1GeometrySink</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD9069F-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, uint>)_vtbl[1])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, uint>)_vtbl[2])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this));
    }

    public void SetFillMode(Win32.Graphics.Direct2D.Common.D2D1_FILL_MODE fillMode)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, Win32.Graphics.Direct2D.Common.D2D1_FILL_MODE, void>)_vtbl[3])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this), fillMode);
    }

    public void BeginFigure(Win32.Graphics.Direct2D.Common.D2D_POINT_2F startPoint, Win32.Graphics.Direct2D.Common.D2D1_FIGURE_BEGIN figureBegin)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, Win32.Graphics.Direct2D.Common.D2D_POINT_2F, Win32.Graphics.Direct2D.Common.D2D1_FIGURE_BEGIN, void>)_vtbl[5])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this), startPoint, figureBegin);
    }

    public void AddLines(Win32.Graphics.Direct2D.Common.D2D_POINT_2F* points, uint pointsCount)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, Win32.Graphics.Direct2D.Common.D2D_POINT_2F*, uint, void>)_vtbl[6])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this), points, pointsCount);
    }

    public void EndFigure(Win32.Graphics.Direct2D.Common.D2D1_FIGURE_END figureEnd)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, Win32.Graphics.Direct2D.Common.D2D1_FIGURE_END, void>)_vtbl[8])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this), figureEnd);
    }

    public void Close()
    {
        ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, Win32.Foundation.HRESULT>)_vtbl[9])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this)).ThrowOnFailure();
    }

    public void AddLine(Win32.Graphics.Direct2D.Common.D2D_POINT_2F point)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, Win32.Graphics.Direct2D.Common.D2D_POINT_2F, void>)_vtbl[10])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this), point);
    }

    public void AddBezier(Win32.Graphics.Direct2D.Common.D2D1_BEZIER_SEGMENT* bezier)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, Win32.Graphics.Direct2D.Common.D2D1_BEZIER_SEGMENT*, void>)_vtbl[11])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this), bezier);
    }

    public void AddQuadraticBezier(Win32.Graphics.Direct2D.D2D1_QUADRATIC_BEZIER_SEGMENT* bezier)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1GeometrySink*, Win32.Graphics.Direct2D.D2D1_QUADRATIC_BEZIER_SEGMENT*, void>)_vtbl[12])((ID2D1GeometrySink*)Unsafe.AsPointer(ref this), bezier);
    }

}

internal unsafe struct ID2D1GradientStopCollection
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1GradientStopCollection</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD906A7-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1GradientStopCollection*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1GradientStopCollection*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1GradientStopCollection*, uint>)_vtbl[1])((ID2D1GradientStopCollection*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1GradientStopCollection*, uint>)_vtbl[2])((ID2D1GradientStopCollection*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct ID2D1GradientStopCollection1
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1GradientStopCollection1</c>.</summary>
    public static Guid IID_Guid { get; } = new("AE1572F4-5DD0-4777-998B-9279472AE63B");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1GradientStopCollection1*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1GradientStopCollection1*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1GradientStopCollection1*, uint>)_vtbl[1])((ID2D1GradientStopCollection1*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1GradientStopCollection1*, uint>)_vtbl[2])((ID2D1GradientStopCollection1*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct ID2D1Image
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1Image</c>.</summary>
    public static Guid IID_Guid { get; } = new("65019F75-8DA2-497C-B32C-DFA34E48EDE6");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Image*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1Image*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Image*, uint>)_vtbl[1])((ID2D1Image*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Image*, uint>)_vtbl[2])((ID2D1Image*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct ID2D1Layer
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1Layer</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD9069B-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Layer*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1Layer*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Layer*, uint>)_vtbl[1])((ID2D1Layer*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1Layer*, uint>)_vtbl[2])((ID2D1Layer*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct ID2D1LinearGradientBrush
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1LinearGradientBrush</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD906AB-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1LinearGradientBrush*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1LinearGradientBrush*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1LinearGradientBrush*, uint>)_vtbl[1])((ID2D1LinearGradientBrush*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1LinearGradientBrush*, uint>)_vtbl[2])((ID2D1LinearGradientBrush*)Unsafe.AsPointer(ref this));
    }

    public void SetOpacity(float opacity)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1LinearGradientBrush*, float, void>)_vtbl[4])((ID2D1LinearGradientBrush*)Unsafe.AsPointer(ref this), opacity);
    }

    public void SetTransform(Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F* transform)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1LinearGradientBrush*, Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F*, void>)_vtbl[5])((ID2D1LinearGradientBrush*)Unsafe.AsPointer(ref this), transform);
    }

}

internal unsafe struct ID2D1PathGeometry
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1PathGeometry</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD906A5-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1PathGeometry*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1PathGeometry*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1PathGeometry*, uint>)_vtbl[1])((ID2D1PathGeometry*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1PathGeometry*, uint>)_vtbl[2])((ID2D1PathGeometry*)Unsafe.AsPointer(ref this));
    }

    public void GetBounds(Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F* worldTransform, Win32.Graphics.Direct2D.Common.D2D_RECT_F* bounds)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1PathGeometry*, Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F*, Win32.Graphics.Direct2D.Common.D2D_RECT_F*, Win32.Foundation.HRESULT>)_vtbl[4])((ID2D1PathGeometry*)Unsafe.AsPointer(ref this), worldTransform, bounds).ThrowOnFailure();
    }

    public void Open(ID2D1GeometrySink** geometrySink)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1PathGeometry*, ID2D1GeometrySink**, Win32.Foundation.HRESULT>)_vtbl[17])((ID2D1PathGeometry*)Unsafe.AsPointer(ref this), geometrySink).ThrowOnFailure();
    }

}

internal unsafe struct ID2D1PathGeometry1
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1PathGeometry1</c>.</summary>
    public static Guid IID_Guid { get; } = new("62BAA2D2-AB54-41B7-B872-787E0106A421");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1PathGeometry1*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1PathGeometry1*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1PathGeometry1*, uint>)_vtbl[1])((ID2D1PathGeometry1*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1PathGeometry1*, uint>)_vtbl[2])((ID2D1PathGeometry1*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct ID2D1RadialGradientBrush
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1RadialGradientBrush</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD906AC-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1RadialGradientBrush*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1RadialGradientBrush*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1RadialGradientBrush*, uint>)_vtbl[1])((ID2D1RadialGradientBrush*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1RadialGradientBrush*, uint>)_vtbl[2])((ID2D1RadialGradientBrush*)Unsafe.AsPointer(ref this));
    }

    public void SetOpacity(float opacity)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1RadialGradientBrush*, float, void>)_vtbl[4])((ID2D1RadialGradientBrush*)Unsafe.AsPointer(ref this), opacity);
    }

    public void SetTransform(Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F* transform)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1RadialGradientBrush*, Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F*, void>)_vtbl[5])((ID2D1RadialGradientBrush*)Unsafe.AsPointer(ref this), transform);
    }

}

internal unsafe struct ID2D1SolidColorBrush
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1SolidColorBrush</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD906A9-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1SolidColorBrush*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1SolidColorBrush*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1SolidColorBrush*, uint>)_vtbl[1])((ID2D1SolidColorBrush*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1SolidColorBrush*, uint>)_vtbl[2])((ID2D1SolidColorBrush*)Unsafe.AsPointer(ref this));
    }

    public void SetOpacity(float opacity)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1SolidColorBrush*, float, void>)_vtbl[4])((ID2D1SolidColorBrush*)Unsafe.AsPointer(ref this), opacity);
    }

    public void SetTransform(Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F* transform)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1SolidColorBrush*, Win32.Graphics.Direct2D.Common.D2D_MATRIX_3X2_F*, void>)_vtbl[5])((ID2D1SolidColorBrush*)Unsafe.AsPointer(ref this), transform);
    }

    public void SetColor(Win32.Graphics.Direct2D.Common.D2D1_COLOR_F* color)
    {
        ((delegate* unmanaged[Stdcall]<ID2D1SolidColorBrush*, Win32.Graphics.Direct2D.Common.D2D1_COLOR_F*, void>)_vtbl[8])((ID2D1SolidColorBrush*)Unsafe.AsPointer(ref this), color);
    }

}

internal unsafe struct ID2D1StrokeStyle
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1StrokeStyle</c>.</summary>
    public static Guid IID_Guid { get; } = new("2CD9069D-12E2-11DC-9FED-001143A055F9");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1StrokeStyle*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1StrokeStyle*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1StrokeStyle*, uint>)_vtbl[1])((ID2D1StrokeStyle*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1StrokeStyle*, uint>)_vtbl[2])((ID2D1StrokeStyle*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct ID2D1StrokeStyle1
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID2D1StrokeStyle1</c>.</summary>
    public static Guid IID_Guid { get; } = new("10A72A66-E91C-43F4-993F-DDF4B82B0B4A");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1StrokeStyle1*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID2D1StrokeStyle1*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1StrokeStyle1*, uint>)_vtbl[1])((ID2D1StrokeStyle1*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID2D1StrokeStyle1*, uint>)_vtbl[2])((ID2D1StrokeStyle1*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct ID3D11Device
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>ID3D11Device</c>.</summary>
    public static Guid IID_Guid { get; } = new("DB6F6DDB-AC77-4E88-8253-819DF9BBF140");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<ID3D11Device*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((ID3D11Device*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<ID3D11Device*, uint>)_vtbl[1])((ID3D11Device*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<ID3D11Device*, uint>)_vtbl[2])((ID3D11Device*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IDWriteFactory
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteFactory</c>.</summary>
    public static Guid IID_Guid { get; } = new("B859EE5A-D838-4B5B-A2E8-1ADC7D93DB48");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFactory*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteFactory*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFactory*, uint>)_vtbl[1])((IDWriteFactory*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFactory*, uint>)_vtbl[2])((IDWriteFactory*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IDWriteFactory5
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteFactory5</c>.</summary>
    public static Guid IID_Guid { get; } = new("958DB99A-BE2A-4F09-AF7D-65189803D1D3");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteFactory5*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, uint>)_vtbl[1])((IDWriteFactory5*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, uint>)_vtbl[2])((IDWriteFactory5*)Unsafe.AsPointer(ref this));
    }

    public void CreateFontFace(Win32.Graphics.DirectWrite.DWRITE_FONT_FACE_TYPE fontFaceType, uint numberOfFiles, IDWriteFontFile** fontFiles, uint faceIndex, Win32.Graphics.DirectWrite.DWRITE_FONT_SIMULATIONS fontFaceSimulationFlags, IDWriteFontFace** fontFace)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, Win32.Graphics.DirectWrite.DWRITE_FONT_FACE_TYPE, uint, IDWriteFontFile**, uint, Win32.Graphics.DirectWrite.DWRITE_FONT_SIMULATIONS, IDWriteFontFace**, Win32.Foundation.HRESULT>)_vtbl[9])((IDWriteFactory5*)Unsafe.AsPointer(ref this), fontFaceType, numberOfFiles, fontFiles, faceIndex, fontFaceSimulationFlags, fontFace).ThrowOnFailure();
    }

    public void CreateCustomRenderingParams(float gamma, float enhancedContrast, float clearTypeLevel, Win32.Graphics.DirectWrite.DWRITE_PIXEL_GEOMETRY pixelGeometry, Win32.Graphics.DirectWrite.DWRITE_RENDERING_MODE renderingMode, IDWriteRenderingParams** renderingParams)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, float, float, float, Win32.Graphics.DirectWrite.DWRITE_PIXEL_GEOMETRY, Win32.Graphics.DirectWrite.DWRITE_RENDERING_MODE, IDWriteRenderingParams**, Win32.Foundation.HRESULT>)_vtbl[12])((IDWriteFactory5*)Unsafe.AsPointer(ref this), gamma, enhancedContrast, clearTypeLevel, pixelGeometry, renderingMode, renderingParams).ThrowOnFailure();
    }

    public void RegisterFontFileLoader(IDWriteFontFileLoader* fontFileLoader)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, IDWriteFontFileLoader*, Win32.Foundation.HRESULT>)_vtbl[13])((IDWriteFactory5*)Unsafe.AsPointer(ref this), fontFileLoader).ThrowOnFailure();
    }

    public void UnregisterFontFileLoader(IDWriteFontFileLoader* fontFileLoader)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, IDWriteFontFileLoader*, Win32.Foundation.HRESULT>)_vtbl[14])((IDWriteFactory5*)Unsafe.AsPointer(ref this), fontFileLoader).ThrowOnFailure();
    }

    public void CreateCustomRenderingParams(float gamma, float enhancedContrast, float enhancedContrastGrayscale, float clearTypeLevel, Win32.Graphics.DirectWrite.DWRITE_PIXEL_GEOMETRY pixelGeometry, Win32.Graphics.DirectWrite.DWRITE_RENDERING_MODE renderingMode, IDWriteRenderingParams1** renderingParams)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, float, float, float, float, Win32.Graphics.DirectWrite.DWRITE_PIXEL_GEOMETRY, Win32.Graphics.DirectWrite.DWRITE_RENDERING_MODE, IDWriteRenderingParams1**, Win32.Foundation.HRESULT>)_vtbl[25])((IDWriteFactory5*)Unsafe.AsPointer(ref this), gamma, enhancedContrast, enhancedContrastGrayscale, clearTypeLevel, pixelGeometry, renderingMode, renderingParams).ThrowOnFailure();
    }

    public void CreateCustomRenderingParams(float gamma, float enhancedContrast, float grayscaleEnhancedContrast, float clearTypeLevel, Win32.Graphics.DirectWrite.DWRITE_PIXEL_GEOMETRY pixelGeometry, Win32.Graphics.DirectWrite.DWRITE_RENDERING_MODE renderingMode, Win32.Graphics.DirectWrite.DWRITE_GRID_FIT_MODE gridFitMode, IDWriteRenderingParams2** renderingParams)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, float, float, float, float, Win32.Graphics.DirectWrite.DWRITE_PIXEL_GEOMETRY, Win32.Graphics.DirectWrite.DWRITE_RENDERING_MODE, Win32.Graphics.DirectWrite.DWRITE_GRID_FIT_MODE, IDWriteRenderingParams2**, Win32.Foundation.HRESULT>)_vtbl[29])((IDWriteFactory5*)Unsafe.AsPointer(ref this), gamma, enhancedContrast, grayscaleEnhancedContrast, clearTypeLevel, pixelGeometry, renderingMode, gridFitMode, renderingParams).ThrowOnFailure();
    }

    public void CreateCustomRenderingParams(float gamma, float enhancedContrast, float grayscaleEnhancedContrast, float clearTypeLevel, Win32.Graphics.DirectWrite.DWRITE_PIXEL_GEOMETRY pixelGeometry, Win32.Graphics.DirectWrite.DWRITE_RENDERING_MODE1 renderingMode, Win32.Graphics.DirectWrite.DWRITE_GRID_FIT_MODE gridFitMode, IDWriteRenderingParams3** renderingParams)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, float, float, float, float, Win32.Graphics.DirectWrite.DWRITE_PIXEL_GEOMETRY, Win32.Graphics.DirectWrite.DWRITE_RENDERING_MODE1, Win32.Graphics.DirectWrite.DWRITE_GRID_FIT_MODE, IDWriteRenderingParams3**, Win32.Foundation.HRESULT>)_vtbl[32])((IDWriteFactory5*)Unsafe.AsPointer(ref this), gamma, enhancedContrast, grayscaleEnhancedContrast, clearTypeLevel, pixelGeometry, renderingMode, gridFitMode, renderingParams).ThrowOnFailure();
    }

    public void CreateInMemoryFontFileLoader(IDWriteInMemoryFontFileLoader** newLoader)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFactory5*, IDWriteInMemoryFontFileLoader**, Win32.Foundation.HRESULT>)_vtbl[44])((IDWriteFactory5*)Unsafe.AsPointer(ref this), newLoader).ThrowOnFailure();
    }

}

internal unsafe struct IDWriteFontFace
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteFontFace</c>.</summary>
    public static Guid IID_Guid { get; } = new("5F49804D-7024-4D43-BFA9-D25984F53849");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFace*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteFontFace*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFace*, uint>)_vtbl[1])((IDWriteFontFace*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFace*, uint>)_vtbl[2])((IDWriteFontFace*)Unsafe.AsPointer(ref this));
    }

    public void GetMetrics(Win32.Graphics.DirectWrite.DWRITE_FONT_METRICS* fontFaceMetrics)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFontFace*, Win32.Graphics.DirectWrite.DWRITE_FONT_METRICS*, void>)_vtbl[8])((IDWriteFontFace*)Unsafe.AsPointer(ref this), fontFaceMetrics);
    }

    public ushort GetGlyphCount()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFace*, ushort>)_vtbl[9])((IDWriteFontFace*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IDWriteFontFile
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteFontFile</c>.</summary>
    public static Guid IID_Guid { get; } = new("739D886A-CEF5-47DC-8769-1A8B41BEBBB0");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFile*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteFontFile*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFile*, uint>)_vtbl[1])((IDWriteFontFile*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFile*, uint>)_vtbl[2])((IDWriteFontFile*)Unsafe.AsPointer(ref this));
    }

    public void Analyze(Win32.Foundation.BOOL* isSupportedFontType, Win32.Graphics.DirectWrite.DWRITE_FONT_FILE_TYPE* fontFileType, Win32.Graphics.DirectWrite.DWRITE_FONT_FACE_TYPE* fontFaceType, uint* numberOfFaces)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteFontFile*, Win32.Foundation.BOOL*, Win32.Graphics.DirectWrite.DWRITE_FONT_FILE_TYPE*, Win32.Graphics.DirectWrite.DWRITE_FONT_FACE_TYPE*, uint*, Win32.Foundation.HRESULT>)_vtbl[5])((IDWriteFontFile*)Unsafe.AsPointer(ref this), isSupportedFontType, fontFileType, fontFaceType, numberOfFaces).ThrowOnFailure();
    }

}

internal unsafe struct IDWriteFontFileLoader
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteFontFileLoader</c>.</summary>
    public static Guid IID_Guid { get; } = new("727CAD4E-D6AF-4C9E-8A08-D695B11CAA49");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFileLoader*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteFontFileLoader*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFileLoader*, uint>)_vtbl[1])((IDWriteFontFileLoader*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteFontFileLoader*, uint>)_vtbl[2])((IDWriteFontFileLoader*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IDWriteInMemoryFontFileLoader
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteInMemoryFontFileLoader</c>.</summary>
    public static Guid IID_Guid { get; } = new("DC102F47-A12D-4B1C-822D-9E117E33043F");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteInMemoryFontFileLoader*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteInMemoryFontFileLoader*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteInMemoryFontFileLoader*, uint>)_vtbl[1])((IDWriteInMemoryFontFileLoader*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteInMemoryFontFileLoader*, uint>)_vtbl[2])((IDWriteInMemoryFontFileLoader*)Unsafe.AsPointer(ref this));
    }

    public void CreateInMemoryFontFileReference(IDWriteFactory* factory, void* fontData, uint fontDataSize, IUnknown* ownerObject, IDWriteFontFile** fontFile)
    {
        ((delegate* unmanaged[Stdcall]<IDWriteInMemoryFontFileLoader*, IDWriteFactory*, void*, uint, IUnknown*, IDWriteFontFile**, Win32.Foundation.HRESULT>)_vtbl[4])((IDWriteInMemoryFontFileLoader*)Unsafe.AsPointer(ref this), factory, fontData, fontDataSize, ownerObject, fontFile).ThrowOnFailure();
    }

}

internal unsafe struct IDWriteRenderingParams
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteRenderingParams</c>.</summary>
    public static Guid IID_Guid { get; } = new("2F0DA53A-2ADD-47CD-82EE-D9EC34688E75");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteRenderingParams*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams*, uint>)_vtbl[1])((IDWriteRenderingParams*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams*, uint>)_vtbl[2])((IDWriteRenderingParams*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IDWriteRenderingParams1
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteRenderingParams1</c>.</summary>
    public static Guid IID_Guid { get; } = new("94413CF4-A6FC-4248-8B50-6674348FCAD3");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams1*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteRenderingParams1*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams1*, uint>)_vtbl[1])((IDWriteRenderingParams1*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams1*, uint>)_vtbl[2])((IDWriteRenderingParams1*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IDWriteRenderingParams2
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteRenderingParams2</c>.</summary>
    public static Guid IID_Guid { get; } = new("F9D711C3-9777-40AE-87E8-3E5AF9BF0948");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams2*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteRenderingParams2*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams2*, uint>)_vtbl[1])((IDWriteRenderingParams2*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams2*, uint>)_vtbl[2])((IDWriteRenderingParams2*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IDWriteRenderingParams3
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDWriteRenderingParams3</c>.</summary>
    public static Guid IID_Guid { get; } = new("B7924BAA-391B-412A-8C5C-E44CC2D867DC");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams3*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDWriteRenderingParams3*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams3*, uint>)_vtbl[1])((IDWriteRenderingParams3*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDWriteRenderingParams3*, uint>)_vtbl[2])((IDWriteRenderingParams3*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IDXGIDevice
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IDXGIDevice</c>.</summary>
    public static Guid IID_Guid { get; } = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IDXGIDevice*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IDXGIDevice*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IDXGIDevice*, uint>)_vtbl[1])((IDXGIDevice*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IDXGIDevice*, uint>)_vtbl[2])((IDXGIDevice*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IStream
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IStream</c>.</summary>
    public static Guid IID_Guid { get; } = new("0000000C-0000-0000-C000-000000000046");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IStream*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IStream*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IStream*, uint>)_vtbl[1])((IStream*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IStream*, uint>)_vtbl[2])((IStream*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IUnknown
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IUnknown</c>.</summary>
    public static Guid IID_Guid { get; } = new("00000000-0000-0000-C000-000000000046");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IUnknown*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IUnknown*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IUnknown*, uint>)_vtbl[1])((IUnknown*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IUnknown*, uint>)_vtbl[2])((IUnknown*)Unsafe.AsPointer(ref this));
    }

}

internal unsafe struct IWICBitmapDecoder
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IWICBitmapDecoder</c>.</summary>
    public static Guid IID_Guid { get; } = new("9EDDE9E7-8DEE-47EA-99DF-E6FAF2ED44BF");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IWICBitmapDecoder*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IWICBitmapDecoder*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IWICBitmapDecoder*, uint>)_vtbl[1])((IWICBitmapDecoder*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IWICBitmapDecoder*, uint>)_vtbl[2])((IWICBitmapDecoder*)Unsafe.AsPointer(ref this));
    }

    public void GetContainerFormat(global::System.Guid* pguidContainerFormat)
    {
        ((delegate* unmanaged[Stdcall]<IWICBitmapDecoder*, global::System.Guid*, Win32.Foundation.HRESULT>)_vtbl[5])((IWICBitmapDecoder*)Unsafe.AsPointer(ref this), pguidContainerFormat).ThrowOnFailure();
    }

    public void GetFrame(uint index, IWICBitmapFrameDecode** ppIBitmapFrame)
    {
        ((delegate* unmanaged[Stdcall]<IWICBitmapDecoder*, uint, IWICBitmapFrameDecode**, Win32.Foundation.HRESULT>)_vtbl[13])((IWICBitmapDecoder*)Unsafe.AsPointer(ref this), index, ppIBitmapFrame).ThrowOnFailure();
    }

}

internal unsafe struct IWICBitmapFrameDecode
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IWICBitmapFrameDecode</c>.</summary>
    public static Guid IID_Guid { get; } = new("3B16811B-6A43-4EC9-A813-3D930C13B940");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IWICBitmapFrameDecode*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IWICBitmapFrameDecode*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IWICBitmapFrameDecode*, uint>)_vtbl[1])((IWICBitmapFrameDecode*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IWICBitmapFrameDecode*, uint>)_vtbl[2])((IWICBitmapFrameDecode*)Unsafe.AsPointer(ref this));
    }

    public void GetSize(uint* puiWidth, uint* puiHeight)
    {
        ((delegate* unmanaged[Stdcall]<IWICBitmapFrameDecode*, uint*, uint*, Win32.Foundation.HRESULT>)_vtbl[3])((IWICBitmapFrameDecode*)Unsafe.AsPointer(ref this), puiWidth, puiHeight).ThrowOnFailure();
    }

}

internal unsafe struct IWICBitmapSourceTransform
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IWICBitmapSourceTransform</c>.</summary>
    public static Guid IID_Guid { get; } = new("3B16811B-6A43-4EC9-B713-3D5A0C13B940");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IWICBitmapSourceTransform*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IWICBitmapSourceTransform*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IWICBitmapSourceTransform*, uint>)_vtbl[1])((IWICBitmapSourceTransform*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IWICBitmapSourceTransform*, uint>)_vtbl[2])((IWICBitmapSourceTransform*)Unsafe.AsPointer(ref this));
    }

    public void CopyPixels(Win32.Graphics.Imaging.WICRect* prc, uint uiWidth, uint uiHeight, global::System.Guid* pguidDstFormat, Win32.Graphics.Imaging.WICBitmapTransformOptions dstTransform, uint nStride, uint cbBufferSize, byte* pbBuffer)
    {
        ((delegate* unmanaged[Stdcall]<IWICBitmapSourceTransform*, Win32.Graphics.Imaging.WICRect*, uint, uint, global::System.Guid*, Win32.Graphics.Imaging.WICBitmapTransformOptions, uint, uint, byte*, Win32.Foundation.HRESULT>)_vtbl[3])((IWICBitmapSourceTransform*)Unsafe.AsPointer(ref this), prc, uiWidth, uiHeight, pguidDstFormat, dstTransform, nStride, cbBufferSize, pbBuffer).ThrowOnFailure();
    }

    public void GetClosestSize(uint* puiWidth, uint* puiHeight)
    {
        ((delegate* unmanaged[Stdcall]<IWICBitmapSourceTransform*, uint*, uint*, Win32.Foundation.HRESULT>)_vtbl[4])((IWICBitmapSourceTransform*)Unsafe.AsPointer(ref this), puiWidth, puiHeight).ThrowOnFailure();
    }

    public void GetClosestPixelFormat(global::System.Guid* pguidDstFormat)
    {
        ((delegate* unmanaged[Stdcall]<IWICBitmapSourceTransform*, global::System.Guid*, Win32.Foundation.HRESULT>)_vtbl[5])((IWICBitmapSourceTransform*)Unsafe.AsPointer(ref this), pguidDstFormat).ThrowOnFailure();
    }

}

internal unsafe struct IWICImagingFactory
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IWICImagingFactory</c>.</summary>
    public static Guid IID_Guid { get; } = new("EC5EC8A9-C395-4314-9C77-54D7A935FF70");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IWICImagingFactory*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IWICImagingFactory*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IWICImagingFactory*, uint>)_vtbl[1])((IWICImagingFactory*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IWICImagingFactory*, uint>)_vtbl[2])((IWICImagingFactory*)Unsafe.AsPointer(ref this));
    }

    public IWICBitmapDecoder* CreateDecoderFromStream(IStream* pIStream, global::System.Guid* pguidVendor, Win32.Graphics.Imaging.WICDecodeOptions metadataOptions)
    {
        IWICBitmapDecoder* result = null;
        ((delegate* unmanaged[Stdcall]<IWICImagingFactory*, IStream*, global::System.Guid*, Win32.Graphics.Imaging.WICDecodeOptions, IWICBitmapDecoder**, Win32.Foundation.HRESULT>)_vtbl[4])((IWICImagingFactory*)Unsafe.AsPointer(ref this), pIStream, pguidVendor, metadataOptions, &result).ThrowOnFailure();
        return result;
    }

    public void CreateStream(IWICStream** ppIWICStream)
    {
        ((delegate* unmanaged[Stdcall]<IWICImagingFactory*, IWICStream**, Win32.Foundation.HRESULT>)_vtbl[14])((IWICImagingFactory*)Unsafe.AsPointer(ref this), ppIWICStream).ThrowOnFailure();
    }

}

internal unsafe struct IWICPlanarBitmapSourceTransform
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IWICPlanarBitmapSourceTransform</c>.</summary>
    public static Guid IID_Guid { get; } = new("3AFF9CCE-BE95-4303-B927-E7D16FF4A613");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IWICPlanarBitmapSourceTransform*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IWICPlanarBitmapSourceTransform*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IWICPlanarBitmapSourceTransform*, uint>)_vtbl[1])((IWICPlanarBitmapSourceTransform*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IWICPlanarBitmapSourceTransform*, uint>)_vtbl[2])((IWICPlanarBitmapSourceTransform*)Unsafe.AsPointer(ref this));
    }

    public void DoesSupportTransform(uint* puiWidth, uint* puiHeight, Win32.Graphics.Imaging.WICBitmapTransformOptions dstTransform, Win32.Graphics.Imaging.WICPlanarOptions dstPlanarOptions, global::System.Guid* pguidDstFormats, Win32.Graphics.Imaging.WICBitmapPlaneDescription* pPlaneDescriptions, uint cPlanes, Win32.Foundation.BOOL* pfIsSupported)
    {
        ((delegate* unmanaged[Stdcall]<IWICPlanarBitmapSourceTransform*, uint*, uint*, Win32.Graphics.Imaging.WICBitmapTransformOptions, Win32.Graphics.Imaging.WICPlanarOptions, global::System.Guid*, Win32.Graphics.Imaging.WICBitmapPlaneDescription*, uint, Win32.Foundation.BOOL*, Win32.Foundation.HRESULT>)_vtbl[3])((IWICPlanarBitmapSourceTransform*)Unsafe.AsPointer(ref this), puiWidth, puiHeight, dstTransform, dstPlanarOptions, pguidDstFormats, pPlaneDescriptions, cPlanes, pfIsSupported).ThrowOnFailure();
    }

    public void CopyPixels(Win32.Graphics.Imaging.WICRect* prcSource, uint uiWidth, uint uiHeight, Win32.Graphics.Imaging.WICBitmapTransformOptions dstTransform, Win32.Graphics.Imaging.WICPlanarOptions dstPlanarOptions, Win32.Graphics.Imaging.WICBitmapPlane* pDstPlanes, uint cPlanes)
    {
        ((delegate* unmanaged[Stdcall]<IWICPlanarBitmapSourceTransform*, Win32.Graphics.Imaging.WICRect*, uint, uint, Win32.Graphics.Imaging.WICBitmapTransformOptions, Win32.Graphics.Imaging.WICPlanarOptions, Win32.Graphics.Imaging.WICBitmapPlane*, uint, Win32.Foundation.HRESULT>)_vtbl[4])((IWICPlanarBitmapSourceTransform*)Unsafe.AsPointer(ref this), prcSource, uiWidth, uiHeight, dstTransform, dstPlanarOptions, pDstPlanes, cPlanes).ThrowOnFailure();
    }

}

internal unsafe struct IWICStream
{
    private void** _vtbl;

    /// <summary>The interface identifier of <c>IWICStream</c>.</summary>
    public static Guid IID_Guid { get; } = new("135FF860-22B7-4DDF-B0F6-218F4F299A43");

    public Win32.Foundation.HRESULT QueryInterface(global::System.Guid* riid, void** ppvObject)
    {
        return ((delegate* unmanaged[Stdcall]<IWICStream*, global::System.Guid*, void**, Win32.Foundation.HRESULT>)_vtbl[0])((IWICStream*)Unsafe.AsPointer(ref this), riid, ppvObject);
    }

    public uint AddRef()
    {
        return ((delegate* unmanaged[Stdcall]<IWICStream*, uint>)_vtbl[1])((IWICStream*)Unsafe.AsPointer(ref this));
    }

    public uint Release()
    {
        return ((delegate* unmanaged[Stdcall]<IWICStream*, uint>)_vtbl[2])((IWICStream*)Unsafe.AsPointer(ref this));
    }

    public void InitializeFromMemory(byte* pbBuffer, uint cbBufferSize)
    {
        ((delegate* unmanaged[Stdcall]<IWICStream*, byte*, uint, Win32.Foundation.HRESULT>)_vtbl[16])((IWICStream*)Unsafe.AsPointer(ref this), pbBuffer, cbBufferSize).ThrowOnFailure();
    }

}

