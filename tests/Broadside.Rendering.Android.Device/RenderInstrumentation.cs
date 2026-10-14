using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Broadside.Rendering.Android;
using Broadside.TestSupport;
using AndroidBitmap = Android.Graphics.Bitmap;
using AndroidCanvas = Android.Graphics.Canvas;
using Matrix = Broadside.Graphics.Matrix;

namespace Broadside.Rendering.Android.Device;

/// <summary>
/// Renders the corpus files the host names (argument <see cref="DeviceProtocol.FilesArgument"/>) through
/// <see cref="AndroidCanvasBackend"/> at the golden images' 72 DPI, writes each page's pixels to the app's external files directory, and
/// measures what a warm replay of each page's display list onto a canvas allocates and how long it takes.
/// </summary>
[Instrumentation(Name = DeviceProtocol.Instrumentation, TargetPackage = DeviceProtocol.Package, Label = "Broadside rendering")]
public sealed class RenderInstrumentation : Instrumentation
{
    private string _files = string.Empty;
    private int _replays = 20;

    public RenderInstrumentation(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    public override void OnCreate(Bundle? arguments)
    {
        base.OnCreate(arguments);
        _files = arguments?.GetString(DeviceProtocol.FilesArgument) ?? string.Empty;
        if (int.TryParse(arguments?.GetString(DeviceProtocol.ReplaysArgument), NumberStyles.Integer, CultureInfo.InvariantCulture, out int replays) && replays > 0)
        {
            _replays = replays;
        }

        Start();
    }

    public override void OnStart()
    {
        base.OnStart();
        using var results = new Bundle();
        try
        {
            Run(results);
            Finish(Result.Ok, results);
        }
#pragma warning disable CA1031 // Every failure goes back to the host as the error result rather than crashing the process.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            results.PutString(DeviceProtocol.ErrorKey, exception.ToString());
            Finish(Result.Canceled, results);
        }
    }

    private void Run(Bundle results)
    {
        Context context = TargetContext ?? throw new InvalidOperationException("The instrumentation has no target context.");
        string output = Path.Combine(context.GetExternalFilesDir(null)!.AbsolutePath, DeviceProtocol.OutputDirectory);
        Directory.CreateDirectory(output);
        var engine = new PdfEngine(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null));
        var options = new RenderOptions { Dpi = RenderOptions.DefaultDpi };
        using var backend = new AndroidCanvasBackend();
        Action probe = () => GC.KeepAlive(new byte[64]);
        results.PutString(DeviceProtocol.AllocationProbeKey, Allocations.Measure(probe).ToString(CultureInfo.InvariantCulture));

        foreach (string fileName in _files.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using PdfDocument document = engine.Open(ReadAsset(context, "corpus/" + fileName));
            results.PutString(DeviceProtocol.PageCountKey(fileName), document.Pages.Count.ToString(CultureInfo.InvariantCulture));
            for (int index = 0; index < document.Pages.Count; index++)
            {
                PdfPage page = document.Pages[index];
                using AndroidBitmap bitmap = backend.Render(page, options);
                int width = bitmap.Width;
                int height = bitmap.Height;
                int[] argb = new int[width * height];
                bitmap.GetPixels(argb, 0, width, 0, 0, width, height);
                File.WriteAllBytes(Path.Combine(output, DeviceProtocol.PixelFile(fileName, index)), MemoryMarshal.AsBytes(argb.AsSpan()).ToArray());
                results.PutString(DeviceProtocol.SizeKey(fileName, index), string.Create(CultureInfo.InvariantCulture, $"{width} {height}"));

                Measure(results, backend, page, options, bitmap, fileName, index);
            }
        }
    }

    /// <summary>Replays the page's display list onto a canvas over the bitmap: the fewest bytes a warm replay allocates, and the mean time.</summary>
    private void Measure(Bundle results, AndroidCanvasBackend backend, PdfPage page, RenderOptions options, AndroidBitmap bitmap, string fileName, int index)
    {
        DisplayList list = page.CreateDisplayList();
        Matrix device = options.GetDeviceTransform(page);
        using var canvas = new AndroidCanvas(bitmap);
        Action replay = () => backend.Render(list, canvas, device);

        long allocated = Allocations.Measure(replay);

        var clock = Stopwatch.StartNew();
        for (int i = 0; i < _replays; i++)
        {
            replay();
        }

        double microseconds = clock.Elapsed.TotalMicroseconds / _replays;
        results.PutString(DeviceProtocol.AllocatedKey(fileName, index), allocated.ToString(CultureInfo.InvariantCulture));
        results.PutString(DeviceProtocol.ReplayTimeKey(fileName, index), microseconds.ToString("F1", CultureInfo.InvariantCulture));
    }

    private static byte[] ReadAsset(Context context, string name)
    {
        using Stream asset = context.Assets!.Open(name);
        using var memory = new MemoryStream();
        asset.CopyTo(memory);
        return memory.ToArray();
    }
}
