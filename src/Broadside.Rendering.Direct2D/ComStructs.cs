using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Broadside.Rendering.Direct2D;

// The few structures whose fields point at COM interfaces. CsWin32 would bind those interfaces (and their whole inheritance
// chains) to type the fields, so the structures are declared here with opaque pointers instead. Field order and types match the
// Windows SDK declarations (d2d1.h, d2d1_1.h, dwrite.h), so the layouts are identical.

/// <summary><c>D2D1_LAYER_PARAMETERS</c>; <see cref="geometricMask"/> is an <c>ID2D1Geometry*</c> and <see cref="opacityBrush"/> an <c>ID2D1Brush*</c>.</summary>
internal unsafe struct D2D1_LAYER_PARAMETERS
{
    public D2D_RECT_F contentBounds;
    public void* geometricMask;
    public D2D1_ANTIALIAS_MODE maskAntialiasMode;
    public D2D_MATRIX_3X2_F maskTransform;
    public float opacity;
    public void* opacityBrush;
    public D2D1_LAYER_OPTIONS layerOptions;
}

/// <summary><c>D2D1_LAYER_PARAMETERS1</c>; <see cref="geometricMask"/> is an <c>ID2D1Geometry*</c> and <see cref="opacityBrush"/> an <c>ID2D1Brush*</c>.</summary>
internal unsafe struct D2D1_LAYER_PARAMETERS1
{
    public D2D_RECT_F contentBounds;
    public void* geometricMask;
    public D2D1_ANTIALIAS_MODE maskAntialiasMode;
    public D2D_MATRIX_3X2_F maskTransform;
    public float opacity;
    public void* opacityBrush;
    public D2D1_LAYER_OPTIONS1 layerOptions;
}

/// <summary><c>D2D1_BITMAP_PROPERTIES1</c>; <see cref="colorContext"/> is an <c>ID2D1ColorContext*</c>.</summary>
internal unsafe struct D2D1_BITMAP_PROPERTIES1
{
    public D2D1_PIXEL_FORMAT pixelFormat;
    public float dpiX;
    public float dpiY;
    public D2D1_BITMAP_OPTIONS bitmapOptions;
    public void* colorContext;
}

/// <summary><c>DWRITE_GLYPH_RUN</c>; <see cref="fontFace"/> is an <c>IDWriteFontFace*</c>.</summary>
internal unsafe struct DWRITE_GLYPH_RUN
{
    public void* fontFace;
    public float fontEmSize;
    public uint glyphCount;
    public ushort* glyphIndices;
    public float* glyphAdvances;
    public DWRITE_GLYPH_OFFSET* glyphOffsets;
    public BOOL isSideways;
    public uint bidiLevel;
}
