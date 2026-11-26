using Broadside.GoldenImages;

namespace Broadside.Samples;

/// <summary>
/// The rendering samples' check against the golden images (tests/Golden): a render of a corpus page must match the page's reviewed
/// golden image within the threshold the golden-image harness sets for the backend. The harness's comparison files are linked in
/// (Golden.props), not referenced, so the samples depend on no test framework.
/// </summary>
internal static class GoldenCheck
{
    /// <summary>Throws when <paramref name="render"/> does not match the page's golden image, which the sample copies to <c>golden/</c>.</summary>
    public static void Expect(string backend, string fileName, int page, GoldenImage render)
    {
        string path = Path.Combine("golden", $"{Path.GetFileNameWithoutExtension(fileName)}.page{page}.verified.png");
        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.LoadPng(path), render, GoldenThresholds.For(backend, fileName, page));
        Sample.Expect(comparison.Matches, $"the {backend} render of page {page} of {fileName} matches its golden image: {comparison.Describe()}");
        Console.WriteLine($"{fileName} page {page} matches its golden image within the {backend} threshold");
    }
}
