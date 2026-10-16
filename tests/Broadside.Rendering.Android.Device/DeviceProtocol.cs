using Broadside.TestSupport;

namespace Broadside.Rendering.Android.Device;

/// <summary>
/// What the host tests (Broadside.Rendering.Android.Tests, which link this file) and the device app agree on: the instrumentation to
/// start, its arguments, the result keys it reports and the files it leaves in the app's external files directory.
/// </summary>
internal static class DeviceProtocol
{
    /// <summary>The device app's package (application id).</summary>
    public const string Package = "com.broadside.rendering.android.device";

    /// <summary>The instrumentation's Java class name, as <c>am instrument</c> takes it after the package and a slash.</summary>
    public const string Instrumentation = Package + ".RenderInstrumentation";

    /// <summary>The argument that lists the corpus files to render, separated by commas.</summary>
    public const string FilesArgument = "files";

    /// <summary>The argument that sets how many replays the allocation and time measurement averages over.</summary>
    public const string ReplaysArgument = "replays";

    /// <summary>The result key of the error that stopped the run, when one did.</summary>
    public const string ErrorKey = "error";

    /// <summary>The directory, under the app's external files directory, that holds the rendered pixels.</summary>
    public const string OutputDirectory = "render";

    /// <summary>The app's external files directory as adb's shell sees it; readable without <c>run-as</c>, so release builds work.</summary>
    public const string DeviceFilesDirectory = "/sdcard/Android/data/" + Package + "/files";

    /// <summary>The render variant with native glyphs (the backend's default).</summary>
    public const string Native = "";

    /// <summary>The render variant with <c>RenderOptions.ForceOutlines</c> set.</summary>
    public const string Outlines = "outlines";

    /// <summary>The render variant drawn by the device app's independent path reference (glyph outlines filled as plain Android paths).</summary>
    public const string Paths = "paths";

    /// <summary>
    /// Returns the name of the file that holds a page's pixels in a render variant: 32-bit ARGB per pixel, little-endian (B, G, R, A
    /// bytes), rows top first, no padding.
    /// </summary>
    public static string PixelFile(string fileName, int page, string variant = Native) =>
        variant.Length == 0
            ? $"{Path.GetFileNameWithoutExtension(fileName)}.page{page}.bgra"
            : $"{Path.GetFileNameWithoutExtension(fileName)}.page{page}.{variant}.bgra";

    /// <summary>Returns the result key of how many glyphs a render variant of the page drew through Android's font engine.</summary>
    public static string NativeGlyphsKey(string fileName, int page, string variant = Native) => $"native-glyphs{Suffix(variant)}:{fileName}:{page}";

    /// <summary>Returns the result key of how many Android fonts a fresh backend built while rendering every page of a file twice.</summary>
    public static string FontsKey(string fileName) => "fonts:" + fileName;

    /// <summary>Returns the result key of the diagnostic codes a render variant of the page reported, separated by commas.</summary>
    public static string DiagnosticsKey(string fileName, int page, string variant = Native) => $"diagnostics{Suffix(variant)}:{fileName}:{page}";

    /// <summary>
    /// Returns the result key that says whether the path reference drew the page: <c>yes</c>, or why not (the page paints something
    /// the reference does not draw).
    /// </summary>
    public static string PathsKey(string fileName, int page) => $"paths:{fileName}:{page}";

    /// <summary>Returns the result key of a file's page count.</summary>
    public static string PageCountKey(string fileName) => "pages:" + fileName;

    /// <summary>Returns the result key of a page's pixel size, <c>width x height</c> as two integers separated by a space.</summary>
    public static string SizeKey(string fileName, int page) => $"size:{fileName}:{page}";

    /// <summary>Returns the result key of the fewest bytes one warm replay of the page allocated on the replaying thread.</summary>
    public static string AllocatedKey(string fileName, int page, string variant = Native) => $"allocated{Suffix(variant)}:{fileName}:{page}";

    /// <summary>
    /// The result key of what the same measurement reports for one small allocation: above zero, it shows that the runtime on the
    /// device counts allocations, so that a zero for a replay means something.
    /// </summary>
    public const string AllocationProbeKey = "allocation-probe";

    /// <summary>
    /// The result key of whether the app was built optimized (<see cref="Optimized"/>) or for debugging. Mono runs a debug build
    /// without JIT optimizations, and then its shared generic code over struct keys allocates (192 to 480 B per colour converter
    /// lookup, issue #314), so only an optimized build's allocation measurement says anything about the backend.
    /// </summary>
    public const string BuildKey = "build";

    /// <summary>The <see cref="BuildKey"/> value of an optimized (Release) build.</summary>
    public const string Optimized = "optimized";

    /// <summary>The <see cref="BuildKey"/> value of a debug build.</summary>
    public const string Debugging = "debug";

    /// <summary>Returns the result key of the mean time of one warm replay of the page, in microseconds.</summary>
    public static string ReplayTimeKey(string fileName, int page, string variant = Native) => $"replay-us{Suffix(variant)}:{fileName}:{page}";

    /// <summary>
    /// The argument that lists the files also rendered from a document opened with a zero image cache budget (variant
    /// <see cref="Uncached"/>), separated by commas.
    /// </summary>
    public const string UncachedArgument = "uncached";

    /// <summary>The render variant of a document opened with a zero image cache budget: every paint converts its image anew.</summary>
    public const string Uncached = "uncached";

    /// <summary>The prefix of a synthetic page's file name: the device generates the file with the test support's samples.</summary>
    public const string SamplePrefix = "sample-";

    /// <summary>The synthetic pages (issue #134): every image resampling path, every mask path, shading, tiling and mesh grids.</summary>
    public static readonly string[] Samples = ["sample-image-heavy.pdf", "sample-mask-heavy.pdf", "sample-shading-grid.pdf", "sample-tiling-grid.pdf", "sample-mesh-patches.pdf"];

    /// <summary>
    /// The synthetic transparency pages (issue #136): every kind of group, every soft-mask path, and a non-isolated group of Multiply
    /// paints. Kept apart from <see cref="Samples"/>: the groups the software rasterizer draws allocate their pixels per render.
    /// </summary>
    public static readonly string[] TransparencySamples = ["sample-group-grid.pdf", "sample-soft-mask-grid.pdf", "sample-non-isolated.pdf"];

    /// <summary>Returns the bytes of a synthetic page (a name in <see cref="Samples"/> or <see cref="TransparencySamples"/>).</summary>
    public static byte[] Sample(string fileName) => fileName switch
    {
        "sample-image-heavy.pdf" => ContentSamples.ImageHeavy(),
        "sample-mask-heavy.pdf" => MaskSamples.MaskHeavy(),
        "sample-shading-grid.pdf" => ShadingSamples.Grid(),
        "sample-tiling-grid.pdf" => TilingSamples.Grid(),
        "sample-mesh-patches.pdf" => MeshShadingSamples.Patches(),
        "sample-group-grid.pdf" => GroupSamples.Grid(),
        "sample-soft-mask-grid.pdf" => SoftMaskSamples.Grid(),
        "sample-non-isolated.pdf" => NonIsolated(),
        _ => throw new ArgumentOutOfRangeException(nameof(fileName)),
    };

    /// <summary>
    /// Returns the result key of how many Android bitmaps a fresh backend made from the file's images and masks after rendering every
    /// page once (<paramref name="pass"/> 1) and twice (2).
    /// </summary>
    public static string ImagesKey(string fileName, int pass, string variant = Native) => $"images{Suffix(variant)}-{pass}:{fileName}";

    /// <summary>Returns the result key of how many pattern cells a fresh backend rendered into tiles after <paramref name="pass"/> renders of every page.</summary>
    public static string CellsKey(string fileName, int pass) => $"cells-{pass}:{fileName}";

    /// <summary>Returns the result key of the bytes of converted pixels a backend keeps after rendering every page of the file in a variant.</summary>
    public static string CachedBytesKey(string fileName, string variant = Native) => $"cached-bytes{Suffix(variant)}:{fileName}";

    /// <summary>
    /// A non-isolated group of Multiply paints at <c>/ca 0.5</c> (ISO 32000-2 §11.4.8) over an opaque backdrop, and the same group
    /// inside an isolated group over a translucent backdrop of its own (a backdrop whose alpha is below 1).
    /// </summary>
    private static byte[] NonIsolated() => ContentSamples.OnePage(
        "0 0.8 0.2 rg 20 20 572 300 re f q /H gs 1 0 0 1 40 40 cm /N Do Q q 1 0 0 1 320 40 cm /I Do Q"u8.ToArray(),
        "/ExtGState << /H << /ca 0.5 >> >> /XObject << /N 5 0 R /I 6 0 R >>",
        ContentSamples.Stream(
            "/M gs 1 0 0 rg 20 20 120 120 re f 0 0 1 rg 70 70 120 120 re f",
            "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Resources << /ExtGState << /M << /BM /Multiply >> >> >> /Group << /S /Transparency >>"),
        ContentSamples.Stream(
            "/H gs 1 1 0 rg 0 0 200 200 re f /N Do",
            "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Resources << /ExtGState << /H << /ca 0.5 >> >> /XObject << /N 5 0 R >> >> /Group << /S /Transparency /I true >>"));

    private static string Suffix(string variant) => variant.Length == 0 ? string.Empty : "-" + variant;
}
