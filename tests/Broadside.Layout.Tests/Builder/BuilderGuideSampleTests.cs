using System.Security.Cryptography;
using Broadside.BuilderSamples;
using Broadside.Diagnostics;
using Broadside.Layout.Flow;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Builder;

/// <summary>
/// The document builder guide's samples as shipped in <c>samples/Broadside.BuilderSamples</c> (issue #397, test plan "Snapshot tests"
/// and "Conformance"): each builds without a problem, passes <c>qpdf --check</c>, matches its snapshot (structure tree, text per page, and a
/// SHA-256 of the bytes with a fixed clock) and passes veraPDF under the profile it declares.
/// </summary>
public sealed class BuilderGuideSampleTests
{
    static BuilderGuideSampleTests() => VerifyConfiguration.Initialize();

    private static readonly ImageSource Logo = ImageSource.FromFile(Path.Combine(AppContext.BaseDirectory, "BuilderSamples", "logo.png"));

    public static TheoryData<string> Names => [.. Samples.All.Select(sample => sample.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public Task Each_sample_builds_cleanly_and_matches_its_snapshot(string name) => Verify(Summary(name)).UseParameters(name);

    [Fact]
    public void Invoices_built_on_many_threads_from_one_static_theme_are_identical()
    {
        byte[] expected;
        using (PdfDocument first = Build("invoice"))
        {
            expected = Save(first);
        }

        byte[][] results = new byte[64][];
        Parallel.For(0, results.Length, index =>
        {
            using PdfDocument invoice = Build("invoice");
            results[index] = Save(invoice);
        });

        Assert.All(results, bytes => Assert.Equal(expected, bytes));
    }

    [Theory]
    [Trait("Category", "VeraPdf")]
    [InlineData("field-report", "ua1")]
    [InlineData("results-table", "ua2")]
    [InlineData("images-and-drawing", "2u")]
    [InlineData("invoice", "3u")]
    [InlineData("guide", "ua1")]
    [InlineData("form", "ua1")]
    public void Each_sample_passes_verapdf_under_the_profile_it_declares(string name, string flavour)
    {
        using PdfDocument document = Build(name);
        byte[] bytes = Save(document);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(bytes, flavour));
    }

    private static string Summary(string name)
    {
        using PdfDocument document = Build(name);
        byte[] bytes = Save(document);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        FlowBuildingBlockTests.AssertQpdfPasses(bytes);
        using PdfDocument reopened = PdfDocument.Open(bytes);
        return $"""
            == structure ==
            {StructureText(reopened)}
            == text ==
            {PagesText(reopened)}
            == sha256 ==
            {Convert.ToHexString(SHA256.HashData(bytes))}
            """;
    }

    private static PdfDocument Build(string name) => Samples.All.Single(sample => sample.Name == name).Build(Options(), Logo);
}
