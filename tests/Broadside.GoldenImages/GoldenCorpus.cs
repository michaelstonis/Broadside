using Broadside.Rendering;
using Broadside.Security;
using Broadside.TestSupport;
using VerifyTests;

namespace Broadside.GoldenImages;

/// <summary>
/// The golden images: every page of every hand-written corpus file (<c>tests/Corpus/</c>) rendered by the software rasterizer at
/// <see cref="Dpi"/> and reviewed as a Verify snapshot in <c>tests/Golden/</c>, and the assertion backend test projects use to compare
/// their own render of a page with it.
/// </summary>
/// <remarks>
/// <para>
/// The rasterizer's renders enter through <see cref="VerifyAsync"/> (the normal Verify flow: a new or changed page fails and leaves a
/// <c>.received.png</c> to review and accept). Backends call <see cref="AssertMatchesReference"/> with their own render, reduced to a
/// <see cref="GoldenImage"/>; they need only public API, never the rasterizer's internals. Either way a failing comparison writes a
/// <c>.diff.png</c> beside the received image (see <see cref="GoldenComparison.CreateDiffImage"/>); both are ignored by git.
/// </para>
/// <para>
/// Setting <see cref="AcceptVariable"/> to <c>1</c> accepts every new or changed rasterizer render in place, for re-baselining after a
/// rendering change; review the changed images before committing them.
/// </para>
/// <para>Spec #33 story 13 and Testing Decisions (golden-image tests with per-page thresholds).</para>
/// </remarks>
public static class GoldenCorpus
{
    /// <summary>The resolution of every golden image: 72 DPI, one pixel per point.</summary>
    public const double Dpi = RenderOptions.DefaultDpi;

    /// <summary>The environment variable that, set to <c>1</c>, accepts new and changed rasterizer renders as golden images.</summary>
    public const string AcceptVariable = "BROADSIDE_ACCEPT_GOLDEN";

    private const string ThresholdKey = "Broadside.GoldenImages.Threshold";
    private const string DiffPathKey = "Broadside.GoldenImages.DiffPath";

    /// <summary>Gets the directory that holds the golden images, <c>tests/Golden/</c>.</summary>
    public static string Directory { get; } = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "Golden"));

    /// <summary>Gets theory data over every page of every hand-written corpus file: the file name and the zero-based page.</summary>
    public static TheoryData<string, int> Pages => PagesOf([.. Corpus.AllFileNames]);

    /// <summary>Returns theory data over every page of the given corpus files, for a backend test that covers some of them.</summary>
    /// <param name="fileNames">The corpus files.</param>
    /// <returns>The file name and zero-based page of each page, in file order.</returns>
    public static TheoryData<string, int> PagesOf(params string[] fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        var data = new TheoryData<string, int>();
        foreach (string fileName in fileNames)
        {
            using PdfDocument document = Open(fileName);
            for (int page = 0; page < document.Pages.Count; page++)
            {
                data.Add(fileName, page);
            }
        }

        return data;
    }

    /// <summary>Returns fresh render options at <see cref="Dpi"/>, the options every golden image was drawn with.</summary>
    /// <returns>The options; a backend maps pages to pixels with their <c>GetPixelSize</c> and <c>GetDeviceTransform</c>.</returns>
    public static RenderOptions CreateRenderOptions() => new() { Dpi = Dpi };

    /// <summary>Opens a corpus file the way the golden images were drawn: leniently, with its user password when it has one.</summary>
    /// <param name="fileName">The corpus file.</param>
    /// <returns>The document; the caller disposes it.</returns>
    public static PdfDocument Open(string fileName)
    {
        string? password = Corpus.UserPassword(fileName);
        return password is null
            ? PdfDocument.Open(Corpus.Bytes(fileName))
            : new PdfEngine().Open(Corpus.Bytes(fileName), new PdfPassword(password));
    }

    /// <summary>Returns the snapshot name of a page: the file name without <c>.pdf</c>, then <c>.page</c> and the zero-based page.</summary>
    /// <param name="fileName">The corpus file.</param>
    /// <param name="page">The zero-based page.</param>
    /// <returns>The name, for example <c>paths-fill-rules.page0</c>.</returns>
    public static string SnapshotName(string fileName, int page)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        return $"{Path.GetFileNameWithoutExtension(fileName)}.page{page}";
    }

    /// <summary>Returns the path of a page's golden image.</summary>
    /// <param name="fileName">The corpus file.</param>
    /// <param name="page">The zero-based page.</param>
    /// <returns>The <c>.verified.png</c> path in <see cref="Directory"/>.</returns>
    public static string ReferencePath(string fileName, int page) => Path.Combine(Directory, SnapshotName(fileName, page) + ".verified.png");

    /// <summary>Loads a page's golden image.</summary>
    /// <param name="fileName">The corpus file.</param>
    /// <param name="page">The zero-based page.</param>
    /// <returns>The image.</returns>
    /// <exception cref="FileNotFoundException">The page has no reviewed golden image yet.</exception>
    public static GoldenImage LoadReference(string fileName, int page)
    {
        string path = ReferencePath(fileName, page);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Page {page} of {fileName} has no golden image. Run Broadside.Rendering.Tests and review and accept its snapshot first.",
                path);
        }

        return GoldenImage.LoadPng(path);
    }

    /// <summary>
    /// Verifies the software rasterizer's render of a page as its golden image through Verify, compared perceptually under the
    /// <see cref="GoldenThresholds.SoftwareRasterizer"/> threshold for the page.
    /// </summary>
    /// <param name="fileName">The corpus file.</param>
    /// <param name="page">The zero-based page.</param>
    /// <param name="render">The rasterizer's render at <see cref="Dpi"/>.</param>
    /// <returns>The verification; await it from the test.</returns>
    public static async Task VerifyAsync(string fileName, int page, GoldenImage render)
    {
        ArgumentNullException.ThrowIfNull(render);
        string name = SnapshotName(fileName, page);
        var settings = new VerifySettings();
        settings.UseDirectory(Directory);
        settings.UseFileName(name);
        settings.UseStreamComparer(CompareSnapshot);
        settings.Context[ThresholdKey] = GoldenThresholds.For(GoldenThresholds.SoftwareRasterizer, fileName, page);
        settings.Context[DiffPathKey] = Path.Combine(Directory, name + ".diff.png");
        if (Environment.GetEnvironmentVariable(AcceptVariable) == "1")
        {
            settings.AutoVerify();
        }

        using var png = new MemoryStream();
        render.SavePng(png);
        png.Position = 0;
        await VerifyXunit.Verifier.Verify(png, "png", settings);
    }

    /// <summary>
    /// Asserts that a backend's render of a page matches the page's golden image under the threshold <see cref="GoldenThresholds"/>
    /// gives that backend and page. On failure it writes <c>&lt;page&gt;.&lt;backend&gt;.received.png</c> and
    /// <c>&lt;page&gt;.&lt;backend&gt;.diff.png</c> to <see cref="Directory"/> and throws; on success it deletes them.
    /// </summary>
    /// <param name="backend">The backend's name, as in the threshold table (<c>skia</c>, <c>coregraphics</c>, ...).</param>
    /// <param name="fileName">The corpus file.</param>
    /// <param name="page">The zero-based page.</param>
    /// <param name="render">The backend's render at <see cref="Dpi"/> through <see cref="CreateRenderOptions"/>.</param>
    /// <exception cref="GoldenImageMismatchException">The render does not match.</exception>
    public static void AssertMatchesReference(string backend, string fileName, int page, GoldenImage render)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backend);
        ArgumentNullException.ThrowIfNull(render);
        GoldenImage expected = LoadReference(fileName, page);
        GoldenComparison comparison = GoldenComparison.Compare(expected, render, GoldenThresholds.For(backend, fileName, page));
        string stem = Path.Combine(Directory, $"{SnapshotName(fileName, page)}.{backend}");
        string received = stem + ".received.png";
        string diff = stem + ".diff.png";
        if (comparison.Matches)
        {
            File.Delete(received);
            File.Delete(diff);
            return;
        }

        render.SavePng(received);
        comparison.CreateDiffImage().SavePng(diff);
        throw new GoldenImageMismatchException(
            $"The {backend} render of page {page} of {fileName} does not match its golden image. {comparison.Describe()} " +
            $"See {received} and {diff}.");
    }

    private static Task<CompareResult> CompareSnapshot(Stream received, Stream verified, IReadOnlyDictionary<string, object> context)
    {
        var threshold = (GoldenThreshold)context[ThresholdKey];
        string diff = (string)context[DiffPathKey];
        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromPng(verified), GoldenImage.FromPng(received), threshold);
        if (comparison.Matches)
        {
            File.Delete(diff);
            return Task.FromResult(CompareResult.Equal);
        }

        comparison.CreateDiffImage().SavePng(diff);
        return Task.FromResult(CompareResult.NotEqual($"{comparison.Describe()} The diff image is {diff}."));
    }
}
