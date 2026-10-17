using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.WebGL;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>
/// The demo's renderer choice (View panel): the software rasterizer (default) or WebGL 2 (#353). The demo's sessions use
/// <see cref="DemoTileBackend"/>, which asks this class per tile, so switching redraws the open document without reopening it.
/// </summary>
[SupportedOSPlatform("browser")]
public static class Renderers
{
    /// <summary>Gets a value indicating whether tiles are drawn with WebGL.</summary>
    public static bool UseWebGL { get; private set; }

    /// <summary>Gets the GPU the WebGL context reports, or why WebGL is not used.</summary>
    public static string Status =>
        UseWebGL ? WebGLRenderer.Description ?? "WebGL 2" : WebGLRenderer.IsAvailable ? "Software rasterizer (WebGL 2 available)" : "Software rasterizer";

    /// <summary>Selects the renderer; false when WebGL was asked for and is not available.</summary>
    public static async Task<bool> SetAsync(bool webgl)
    {
        if (webgl && !await WebGLTileBackend.InitializeAsync().ConfigureAwait(true))
        {
            UseWebGL = false;
            return false;
        }

        UseWebGL = webgl;
        RenderStats.Reset();
        return true;
    }
}

/// <summary>The demo's tile backend: draws each tile with the backend <see cref="Renderers"/> selects, and times it.</summary>
[SupportedOSPlatform("browser")]
public sealed class DemoTileBackend : ITileBackend
{
    private readonly SoftwareTileBackend _software = new();
    private WebGLTileBackend? _webgl;

    /// <inheritdoc />
    public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken)
    {
        bool gpu = Renderers.UseWebGL && WebGLTileBackend.IsAvailable;
        ITileBackend backend = gpu ? _webgl ??= new WebGLTileBackend() : _software;
        long start = Stopwatch.GetTimestamp();
        ITileRender render = backend.Begin(list, target, deviceTransform, cancellationToken);
        return new Timed(render, gpu ? _webgl : null, Stopwatch.GetElapsedTime(start));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _software.Dispose();
        _webgl?.Dispose();
    }

    private sealed class Timed(ITileRender render, WebGLTileBackend? webgl, TimeSpan spent) : ITileRender
    {
        private TimeSpan _spent = spent;

        public bool RenderSlice(TimeSpan budget)
        {
            long start = Stopwatch.GetTimestamp();
            bool done = render.RenderSlice(budget);
            _spent += Stopwatch.GetElapsedTime(start);
            if (done)
            {
                string? fallback = null;
                if (webgl is not null)
                {
                    // Any diagnostic means the software rasterizer drew the tile or part of it (a group).
                    foreach (Diagnostics.Diagnostic diagnostic in webgl.Diagnostics)
                    {
                        fallback = diagnostic.Code;
                    }
                }

                RenderStats.RecordTile(_spent.TotalMilliseconds, fallback);
            }

            return done;
        }

        public void Dispose() => render.Dispose();
    }
}

/// <summary>Tile render times, fallbacks and the time to the first sharp page, for the View panel and the browser benchmarks.</summary>
[SupportedOSPlatform("browser")]
public static partial class RenderStats
{
    private static readonly List<double> Tiles = [];
    private static readonly Dictionary<string, int> Fallbacks = new(StringComparer.Ordinal);
    private static long _opened = Stopwatch.GetTimestamp();
    private static double _firstSharp = -1;
    private static double _lastVisible = -1;

    /// <summary>Gets the number of tiles drawn since the last reset.</summary>
    public static int TileCount => Tiles.Count;

    /// <summary>Gets the mean tile render time, in milliseconds.</summary>
    public static double MeanTile => Tiles.Count == 0 ? 0 : Tiles.Average();

    /// <summary>Gets the time from opening to the first sharp page, in milliseconds (-1 while not reached).</summary>
    public static double FirstSharp => _firstSharp >= 0 ? _firstSharp : _lastVisible;

    /// <summary>Gets the number of tiles the software rasterizer drew, whole or in part (a transparency group), because WebGL could not.</summary>
    public static int FallbackTiles => Fallbacks.Values.Sum();

    public static void Reset()
    {
        Tiles.Clear();
        Fallbacks.Clear();
    }

    /// <summary>Starts timing an open: the first sharp page is measured from here.</summary>
    public static void MarkOpen()
    {
        _opened = Stopwatch.GetTimestamp();
        _firstSharp = -1;
        _lastVisible = -1;
        Reset();
    }

    public static void RecordTile(double milliseconds, string? fallback)
    {
        Tiles.Add(milliseconds);
        if (fallback is not null)
        {
            Fallbacks[fallback] = Fallbacks.GetValueOrDefault(fallback) + 1;
        }
    }

    /// <summary>
    /// The viewer's tile observer: the scheduler starts a near or ahead tile only once every visible tile is resident, so the first
    /// such start (or the last visible tile, when nothing else is wanted) is the first sharp page.
    /// </summary>
    public static void Observe(TileSchedulerEvent e)
    {
        if (_firstSharp >= 0)
        {
            return;
        }

        double now = Stopwatch.GetElapsedTime(_opened).TotalMilliseconds;
        if (e.Kind == TileEventKind.Presented && e.Priority == TilePriority.Visible && !e.IsPlaceholder)
        {
            _lastVisible = now;
        }
        else if (e.Kind == TileEventKind.Started && e.Priority != TilePriority.Visible && _lastVisible >= 0)
        {
            _firstSharp = _lastVisible;
        }
    }

    /// <summary>The statistics as JSON (for the browser benchmarks).</summary>
    [JSExport]
    public static string Snapshot()
    {
        double[] sorted = [.. Tiles];
        Array.Sort(sorted);
        var json = new StringBuilder();
        json.Append(CultureInfo.InvariantCulture, $"{{\"renderer\":\"{(Renderers.UseWebGL ? "webgl" : "software")}\",\"tiles\":{sorted.Length}");
        json.Append(CultureInfo.InvariantCulture, $",\"meanTile\":{(sorted.Length == 0 ? 0 : sorted.Average()):0.###}");
        json.Append(CultureInfo.InvariantCulture, $",\"p95Tile\":{Percentile(sorted, 0.95):0.###},\"maxTile\":{(sorted.Length == 0 ? 0 : sorted[^1]):0.###}");
        json.Append(CultureInfo.InvariantCulture, $",\"firstSharp\":{FirstSharp:0.###},\"fallbackTiles\":{FallbackTiles}");
        json.Append(CultureInfo.InvariantCulture, $",\"managedBytes\":{GC.GetTotalMemory(false)},\"textureBytes\":{WebGLRenderer.TextureBytes}");
        json.Append(",\"fallbacks\":{");
        json.AppendJoin(',', Fallbacks.Select(static pair => $"\"{pair.Key}\":{pair.Value.ToString(CultureInfo.InvariantCulture)}"));
        json.Append(CultureInfo.InvariantCulture, $"}},\"gpu\":\"{(WebGLRenderer.Description ?? "").Replace("\"", "'", StringComparison.Ordinal)}\"}}");
        return json.ToString();
    }

    [JSExport]
    public static void ResetStatistics() => Reset();

    /// <summary>Selects the renderer and redraws the open document (for the browser benchmarks).</summary>
    [JSExport]
    public static async Task<bool> UseRendererAsync(bool webgl)
    {
        bool ok = await Renderers.SetAsync(webgl).ConfigureAwait(true);
        Demo.Current?.Viewer?.Redraw();
        return ok;
    }

    /// <summary>
    /// Renders one page of a document at <paramref name="dpi"/> with either backend, outside the viewer; returns
    /// "milliseconds;diagnostic codes;base64 PNG" (for pixel agreement and whole-page timings in the browser tests).
    /// </summary>
    [JSExport]
    public static async Task<string> RenderPageAsync(string address, int page, double dpi, bool webgl)
    {
        using var http = new HttpClient();
        byte[] bytes = await http.GetByteArrayAsync(new Uri(address)).ConfigureAwait(true);
        if (webgl)
        {
            await WebGLRenderer.InitializeAsync().ConfigureAwait(true);
        }

        using PdfDocument document = new PdfEngine(new PdfOptions().UseStandard14Fonts()).Open(bytes);
        PdfPage pdfPage = document.Pages[page];
        var options = new RenderOptions { Dpi = dpi };
        pdfPage.CreateDisplayList();
        long start = Stopwatch.GetTimestamp();
        Bitmap bitmap;
        string codes = "";
        if (webgl)
        {
            using var renderer = new WebGLRenderer();
            bitmap = renderer.Render(pdfPage, options);
            codes = string.Join(',', renderer.Diagnostics.Select(static d => d.Code));
        }
        else
        {
            using var rasterizer = new SoftwareRasterizer();
            bitmap = rasterizer.Render(pdfPage, options);
        }

        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        using (bitmap)
        {
            using var png = new MemoryStream();
            bitmap.SavePng(png);
            return string.Create(CultureInfo.InvariantCulture, $"{milliseconds:0.###};{codes};{Convert.ToBase64String(png.ToArray())}");
        }
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
}
