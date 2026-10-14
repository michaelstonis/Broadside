using Broadside.GoldenImages;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tools.RenderDiff.Tests;

/// <summary>A temporary directory holding a run's inputs (copies of corpus files), fixture reference renders and its output.</summary>
internal sealed class Workspace : IDisposable
{
    public Workspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "renderdiff-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Inputs);
    }

    public string Root { get; }

    public string Inputs => Path.Combine(Root, "inputs");

    public string Output => Path.Combine(Root, "out");

    public StringWriter Log { get; } = new();

    /// <summary>Gets the engines' render scripts, copied next to the test assembly from the tool's <c>reference</c> folder.</summary>
    public static string ScriptDirectory => Path.Combine(AppContext.BaseDirectory, "reference");

    public static string CorpusFile(string fileName) => Path.Combine(Corpus.Directory, fileName);

    /// <summary>Returns an image with a black square of the given size painted where the image and a one-pixel margin are white.</summary>
    public static GoldenImage PaintBlackSquare(GoldenImage image, int size)
    {
        for (int top = 1; top + size + 1 < image.Height; top++)
        {
            for (int left = 1; left + size + 1 < image.Width; left++)
            {
                if (IsWhite(image, left - 1, top - 1, size + 2))
                {
                    return Paint(image, left, top, size);
                }
            }
        }

        throw new InvalidOperationException("The image has no white area to paint on.");
    }

    /// <summary>Returns the image moved right by <paramref name="dx"/> and down by <paramref name="dy"/> pixels, white where nothing moved in.</summary>
    public static GoldenImage Shift(GoldenImage image, int dx, int dy) =>
        Build(image.Width, image.Height, (x, y) =>
            x - dx >= 0 && x - dx < image.Width && y - dy >= 0 && y - dy < image.Height ? image[x - dx, y - dy] : ((byte)255, (byte)255, (byte)255));

    /// <summary>Returns an image of the same size in one colour.</summary>
    public static GoldenImage Fill(GoldenImage image, (byte R, byte G, byte B) colour) => Build(image.Width, image.Height, (_, _) => colour);

    /// <summary>Returns the image with <paramref name="columns"/> white columns added on the right.</summary>
    public static GoldenImage Widen(GoldenImage image, int columns) =>
        Build(image.Width + columns, image.Height, (x, y) => x < image.Width ? image[x, y] : ((byte)255, (byte)255, (byte)255));

    public string ReferenceDirectory(string engine)
    {
        string directory = Path.Combine(Root, "references", engine);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Renders every page of a corpus file with Broadside at 72 DPI into <c>&lt;directory&gt;/&lt;stem&gt;/page&lt;N&gt;.png</c>.</summary>
    public static void WriteBroadsideRenders(string directory, string fileName, Func<int, GoldenImage, GoldenImage>? transform = null)
    {
        using PdfDocument document = PdfDocument.Open(CorpusFile(fileName));
        using var rasterizer = new SoftwareRasterizer();
        for (int page = 0; page < document.Pages.Count; page++)
        {
            using Bitmap bitmap = rasterizer.Render(document.Pages[page], new RenderOptions { Dpi = 72 });
            GoldenImage image = GoldenImage.FromBitmap(bitmap);
            image = transform?.Invoke(page, image) ?? image;
            image.SavePng(Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName), $"page{page}.png"));
        }
    }

    /// <summary>Copies the corpus files into <see cref="Inputs"/> and runs RenderDiff over them.</summary>
    public Task<RenderDiffReport> RunAsync(IEnumerable<string> fileNames, Func<RenderDiffOptions, RenderDiffOptions> configure)
    {
        var inputs = new List<string>();
        foreach (string fileName in fileNames)
        {
            string copy = Path.Combine(Inputs, fileName);
            File.Copy(CorpusFile(fileName), copy, overwrite: true);
            inputs.Add(copy);
        }

        var options = new RenderDiffOptions { Inputs = inputs, OutputDirectory = Output };
        return RenderDiffRun.RunAsync(configure(options), Log, TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        Log.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is harmless.
        }
    }

    private static bool IsWhite(GoldenImage image, int left, int top, int size)
    {
        for (int y = top; y < top + size; y++)
        {
            for (int x = left; x < left + size; x++)
            {
                if (image[x, y] != (255, 255, 255))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static GoldenImage Paint(GoldenImage image, int left, int top, int size) =>
        Build(image.Width, image.Height, (x, y) =>
            x >= left && x < left + size && y >= top && y < top + size ? ((byte)0, (byte)0, (byte)0) : image[x, y]);

    private static GoldenImage Build(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (byte r, byte g, byte b) = pixel(x, y);
                int offset = ((y * width) + x) * 4;
                rgba[offset] = r;
                rgba[offset + 1] = g;
                rgba[offset + 2] = b;
                rgba[offset + 3] = 255;
            }
        }

        return GoldenImage.FromPixels(rgba, width, height, width * 4, GoldenPixelFormat.Rgba8);
    }
}
