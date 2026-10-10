using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// <c>functions.pdf</c>: the tint transforms of six Separation spaces (ISO 32000-2 §8.6.6.4), one per function type plus a shared
/// indirect function, read from a file and evaluated at the tints the page paints with. The expected values are what poppler renders
/// (tests/Corpus/README.md).
/// </summary>
public sealed class CorpusFunctionTests
{
    public static TheoryData<string, PdfFunctionType, float, float[]> TintTransforms => new()
    {
        { "CS0", PdfFunctionType.Sampled, 0.5f, [0.2f, 0.4f, 0f, 0f] },
        { "CS0", PdfFunctionType.Sampled, 0.25f, [0.1f, 0.2f, 0f, 0f] },
        { "CS1", PdfFunctionType.Exponential, 0.5f, [0.5f, 0.5f, 1f] },
        { "CS2", PdfFunctionType.Exponential, 0.5f, [0.75f] },
        { "CS3", PdfFunctionType.Stitching, 0.75f, [0.25f] },
        { "CS3", PdfFunctionType.Stitching, 0.25f, [0.5f] },
        { "CS4", PdfFunctionType.PostScriptCalculator, 0.5f, [0.42f, 0f, 0.22f, 0.105f] },
        { "CS5", PdfFunctionType.Exponential, 0.25f, [0.75f, 0.75f, 1f] },
    };

    [Theory]
    [MemberData(nameof(TintTransforms))]
    public void Each_tint_transform_evaluates_to_the_colour_the_page_shows(string space, PdfFunctionType type, float tint, float[] expected)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("functions.pdf"));

        PdfFunction function = document.Function(TintTransform(document, space));

        Assert.Equal(type, function.FunctionType);
        Assert.NotNull(function.Reference);
        Near(expected, function.At(tint), 1e-6);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Two_spaces_that_share_an_indirect_function_share_its_view()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("functions.pdf"));

        PdfFunction linear = document.Function(TintTransform(document, "CS1"));
        PdfFunction shared = document.Function(TintTransform(document, "CS5"));

        Assert.Same(linear, shared);
        Assert.Equal(new CosReference(12, 0), shared.Reference);
    }

    /// <summary>The fourth element of the Separation array named <paramref name="space"/> in the page's resources.</summary>
    private static CosObject TintTransform(PdfDocument document, string space)
    {
        var resources = (CosDictionary)document.Resolve(document.Pages[0].Resources);
        var spaces = (CosDictionary)document.Resolve(resources[new CosName("ColorSpace")]);
        var separation = (CosArray)document.Resolve(spaces[new CosName(space)]);
        return separation[3];
    }
}
