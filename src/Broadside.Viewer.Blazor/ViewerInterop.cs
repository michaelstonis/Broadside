using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// The whole JavaScript boundary of the viewer: imports from <c>broadside-viewer.js</c> (canvas blits, frame pacing, focus and the
/// clipboard) and exports the DOM events call synchronously, so a key handler can decide on <c>preventDefault</c>. Spans cross as
/// memory views over the WebAssembly heap: a tile's pixels are copied once, by the browser, into its <c>ImageData</c>.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class ViewerInterop
{
    private const string Module = "broadside-viewer";

    private static readonly Dictionary<int, ViewerController> Viewers = [];
    private static Task? _import;
    private static int _next;

    /// <summary>Loads the module once per application.</summary>
    public static Task ImportAsync() =>
        _import ??= JSHost.ImportAsync(Module, "../_content/Broadside.Viewer.Blazor/broadside-viewer.js");

    /// <summary>Registers a controller and returns its id.</summary>
    public static int Register(ViewerController controller)
    {
        int id = ++_next;
        Viewers[id] = controller;
        return id;
    }

    /// <summary>Forgets a controller.</summary>
    public static void Unregister(int id) => Viewers.Remove(id);

    [JSImport("attach", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    public static partial Task Attach(int id, string elementId);

    [JSImport("detach", Module)]
    public static partial void Detach(int id);

    [JSImport("nextFrame", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    public static partial Task NextFrame(int id);

    [JSImport("putTile", Module)]
    public static partial void PutTile(int id, int key, int width, int height, [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);

    [JSImport("putTileFromCanvas", Module)]
    public static partial void PutTileFromCanvas(int id, int key, int width, int height, JSObject source);

    [JSImport("putTileFromImage", Module)]
    public static partial void PutTileFromImage(int id, int key, JSObject image);

    [JSImport("markWorker", Module)]
    public static partial void MarkWorker(int id, bool on);

    [JSImport("startWorker", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string> StartWorker(int timeoutMs);

    [JSImport("workerOpen", Module)]
    public static partial void WorkerOpen(int document, int intent, [JSMarshalAs<JSType.MemoryView>] Span<byte> bytes);

    [JSImport("workerOpenAddress", Module)]
    public static partial void WorkerOpenAddress(int document, int intent, string address);

    [JSImport("workerClose", Module)]
    public static partial void WorkerClose(int document);

    [JSImport("workerRender", Module)]
    public static partial void WorkerRender(int job, int document, int page, double scale, int rotation, int x, int y, int width, int height, int layers);

    [JSImport("workerCancel", Module)]
    public static partial void WorkerCancel(int job);

    [JSImport("dropTile", Module)]
    public static partial void DropTile(int id, int key);

    [JSImport("draw", Module)]
    public static partial void Draw(int id, [JSMarshalAs<JSType.MemoryView>] Span<double> commands);

    [JSImport("focusNode", Module)]
    public static partial bool FocusNode(int id, string elementId, bool follow);

    [JSImport("writeClipboard", Module)]
    public static partial void WriteClipboard(string text);

    [JSExport]
    internal static void OnResize(int id, double width, double height, double pixelRatio) =>
        Get(id)?.Resize(width, height, pixelRatio);

    [JSExport]
    internal static void OnPointerDown(int id, int pointerId, int kind, double x, double y, int clicks, int modifiers) =>
        Get(id)?.PointerDown(pointerId, (PointerKind)kind, x, y, clicks, (InputModifiers)modifiers);

    [JSExport]
    internal static int OnPointerMove(int id, int pointerId, double x, double y) => Get(id)?.PointerMove(pointerId, x, y) ?? 0;

    [JSExport]
    internal static void OnPointerUp(int id, int pointerId, double x, double y) => Get(id)?.PointerUp(pointerId, x, y);

    [JSExport]
    internal static void OnPointerCancel(int id, int pointerId) => Get(id)?.PointerCancel(pointerId);

    [JSExport]
    internal static void OnWheel(int id, double deltaX, double deltaY, int mode, int modifiers, double x, double y) =>
        Get(id)?.Wheel(deltaX, deltaY, (WheelDeltaMode)mode, (InputModifiers)modifiers, x, y);

    [JSExport]
    internal static bool OnKeyDown(int id, string key, int modifiers) => Get(id)?.KeyDown(key, (InputModifiers)modifiers) ?? false;

    [JSExport]
    internal static string? OnCopy(int id) => Get(id)?.Copy();

    [JSExport]
    internal static void OnNodeFocused(int id, string elementId) => Get(id)?.NodeFocused(elementId);

    private static ViewerController? Get(int id) => Viewers.GetValueOrDefault(id);
}
