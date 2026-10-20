using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Broadside.Viewer.Maui;

/// <summary>
/// The view's GPU canvas on iOS: a Metal-backed Skia surface (SkiaSharp's <c>SKMetalView</c>), where SkiaSharp's own
/// <see cref="SkiaSharp.Views.Maui.Controls.SKGLView"/> is OpenGL ES (deprecated on iOS and slowly emulated on the simulator). Mac
/// Catalyst's <see cref="SkiaSharp.Views.Maui.Controls.SKGLView"/> is Metal already and Android's is OpenGL ES, so only iOS uses this.
/// </summary>
/// <remarks>#357. Painted on demand (<see cref="InvalidateSurface"/>); touches arrive as SkiaSharp touch events in canvas pixels.</remarks>
internal sealed class MetalCanvas : View
{
    /// <summary>Gets whether this platform draws the view through Metal (iOS with a Metal device; never elsewhere).</summary>
    public static bool IsSupported =>
#if IOS
        !OperatingSystem.IsMacCatalyst() && MetalCanvasHandler.HasDevice;
#else
        false;
#endif

    /// <summary>Raised on the UI thread with the frame's surface and its width in pixels.</summary>
    public event Action<SKSurface, int>? Painted;

    /// <summary>Raised for each touch, in canvas pixels.</summary>
    public event EventHandler<SKTouchEventArgs>? Touch;

    /// <summary>Asks for a paint.</summary>
    public void InvalidateSurface() => Handler?.Invoke(nameof(InvalidateSurface));

    internal void OnPaint(SKSurface surface, int pixelWidth) => Painted?.Invoke(surface, pixelWidth);

    internal void OnTouch(SKTouchEventArgs e) => Touch?.Invoke(this, e);
}
