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

    /// <summary>Returns the result key of the mean time of one warm replay of the page, in microseconds.</summary>
    public static string ReplayTimeKey(string fileName, int page, string variant = Native) => $"replay-us{Suffix(variant)}:{fileName}:{page}";

    private static string Suffix(string variant) => variant.Length == 0 ? string.Empty : "-" + variant;
}
