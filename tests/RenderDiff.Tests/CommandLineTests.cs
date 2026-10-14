namespace Broadside.Tools.RenderDiff.Tests;

/// <summary>The command line: <c>dotnet run --project tools/RenderDiff -- [options] &lt;pdf-or-directory&gt;...</c>.</summary>
public sealed class CommandLineTests : IDisposable
{
    private readonly Workspace _workspace = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        _out.Dispose();
        _error.Dispose();
        _workspace.Dispose();
    }

    [Fact]
    public async Task With_no_reference_renderer_installed_it_says_how_to_get_one_and_exits_3()
    {
        string empty = _workspace.ReferenceDirectory("none");

        int exit = await RunAsync("--engines", "pdfium,pdfjs", "--reference-dir", empty, "--out", _workspace.Output, Workspace.CorpusFile("paths-fill-rules.pdf"));

        Assert.Equal(3, exit);
        string output = _out.ToString() + _error.ToString();
        Assert.Contains("Skipping pdfium:", output, StringComparison.Ordinal);
        Assert.Contains("Skipping pdfjs:", output, StringComparison.Ordinal);
        Assert.Contains("acquire-references.sh", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_directory_of_earlier_renders_is_a_reference_and_the_report_is_written()
    {
        string reference = _workspace.ReferenceDirectory("earlier");
        Workspace.WriteBroadsideRenders(reference, "paths-fill-rules.pdf");

        int exit = await RunAsync("--engines", "", "--reference", $"earlier={reference}", "--out", _workspace.Output, Workspace.CorpusFile("paths-fill-rules.pdf"));

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(_workspace.Output, "index.html")));
        Assert.Contains("| 1 | paths-fill-rules.pdf | 0 | 0 % |", await File.ReadAllTextAsync(Path.Combine(_workspace.Output, "triage.md"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--engines", "ghostscript", "x.pdf")]
    [InlineData("--dpi", "zero", "x.pdf")]
    [InlineData("--edge", "2", "x.pdf")]
    [InlineData("--out", "somewhere")]
    public async Task Bad_arguments_print_the_usage_and_exit_2(params string[] arguments)
    {
        int exit = await RunAsync(arguments);

        Assert.Equal(2, exit);
        Assert.Contains("Usage:", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Poppler_draws_rotated_pages_as_Broadside_does()
    {
        Assert.SkipWhen(ReferenceEngine.Create("poppler", _workspace.Root, Workspace.ScriptDirectory).CheckAvailable() is not null, "pdftoppm is not installed.");

        int exit = await RunAsync("--engines", "poppler", "--max-shift", "0", "--out", _workspace.Output, Workspace.CorpusFile("page-rotation.pdf"));

        Assert.Equal(0, exit);
        string triage = await File.ReadAllTextAsync(Path.Combine(_workspace.Output, "triage.md"), TestContext.Current.CancellationToken);

        // Page 3 (UserUnit 2) is left out: pdftoppm ignores UserUnit, as PDFium does; pdf.js agrees with Broadside.
        for (int page = 0; page < 3; page++)
        {
            Assert.Contains($"| page-rotation.pdf | {page} | 0 % | 0 % (0 px) |", triage, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("pdfium")]
    [InlineData("pdfjs")]
    public async Task An_acquired_reference_engine_draws_rotated_pages_as_Broadside_does(string engine)
    {
        string installed = Environment.GetEnvironmentVariable("BROADSIDE_REFERENCE_DIR") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : Path.GetFullPath(Path.Combine(Workspace.CorpusFile(string.Empty), "..", "..", "artifacts", "reference-renderers"));
        string? missing = ReferenceEngine.Create(engine, installed, Workspace.ScriptDirectory).CheckAvailable();
        Assert.SkipWhen(missing is not null, $"{engine} is not installed: {missing}");

        int exit = await RunAsync("--engines", engine, "--reference-dir", installed, "--max-shift", "0", "--out", _workspace.Output, Workspace.CorpusFile("page-rotation.pdf"));

        Assert.Equal(0, exit);
        string triage = await File.ReadAllTextAsync(Path.Combine(_workspace.Output, "triage.md"), TestContext.Current.CancellationToken);
        for (int page = 0; page < 3; page++)
        {
            Assert.Contains($"| page-rotation.pdf | {page} | 0 % | 0 % (0 px) |", triage, StringComparison.Ordinal);
        }
    }

    private Task<int> RunAsync(params string[] arguments) => RenderDiffCommand.RunAsync(arguments, _out, _error, TestContext.Current.CancellationToken);
}
