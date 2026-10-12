using Broadside.Objects;

namespace Broadside.Tests.Document;

/// <summary>What a reader exposes about a document, as a value: two opens of the same file must project equal.</summary>
internal sealed record DocumentProjection(string Version, string Pages, string Diagnostics)
{
    public static DocumentProjection Of(PdfDocument document)
    {
        IEnumerable<string> pages = document.Pages.Select(static page =>
            $"{Describe(page.Reference)} media {page.MediaBox} crop {page.CropBox} bleed {page.BleedBox} trim {page.TrimBox} art {page.ArtBox} "
            + $"rotate {page.Rotation} unit {page.UserUnit} resources {page.Resources}");
        return new DocumentProjection(
            document.Version.ToString(),
            string.Join('\n', pages),
            string.Join('\n', document.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    private static string Describe(CosReference? reference) => reference is null ? "direct" : $"{reference.ObjectNumber} {reference.Generation} R";
}
