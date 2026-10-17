using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Broadside.Rendering.WebGL;

/// <summary>The calls into <c>wwwroot/broadside-webgl.js</c>, the backend's only JavaScript (no dependencies).</summary>
[SupportedOSPlatform("browser")]
internal static partial class WebGLInterop
{
    public const string Module = "broadside-webgl";

    [JSImport("initialize", Module)]
    public static partial string Initialize();

    [JSImport("getMaxTextureSize", Module)]
    public static partial int GetMaxTextureSize();

    [JSImport("getCanvas", Module)]
    public static partial JSObject GetCanvas();

    [JSImport("upload", Module)]
    public static partial void Upload(int id, int width, int height, int format, [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels, bool interpolate);

    [JSImport("release", Module)]
    public static partial void Release(int id);

    [JSImport("readLast", Module)]
    public static partial void ReadLast(int width, int height, [JSMarshalAs<JSType.MemoryView>] Span<byte> target);

    [JSImport("render", Module)]
    public static partial void Render(
        int width,
        int height,
        double red,
        double green,
        double blue,
        double alpha,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> vertices,
        [JSMarshalAs<JSType.MemoryView>] Span<int> commands,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> target);
}
