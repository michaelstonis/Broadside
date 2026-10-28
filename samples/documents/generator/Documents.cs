using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.SampleDocuments;

/// <summary>Where the sources are and where the PDFs go.</summary>
/// <param name="Root">The samples/documents folder.</param>
/// <param name="Output">The folder the PDFs are written to.</param>
internal sealed record GeneratorContext(string Root, string Output)
{
    /// <summary>Embeds an OpenType or TrueType font from samples/documents/fonts (subset when saved).</summary>
    public PdfCanvasFont Font(PdfDocument document, string relativePath) =>
        document.EmbedFont(File.ReadAllBytes(Path.Combine(Root, "fonts", relativePath)));

    public string OutputPath(string name) => Path.Combine(Output, name);
}

/// <summary>The documents this tool writes.</summary>
internal static class Documents
{
    private static readonly Dictionary<string, Action<GeneratorContext>> All = new(StringComparer.Ordinal)
    {
        ["invoice-conservative"] = InvoiceConservative.Write,
    };

    public static IEnumerable<string> Names => All.Keys;

    public static bool TryRun(string name, GeneratorContext context)
    {
        if (!All.TryGetValue(name, out Action<GeneratorContext>? write))
        {
            return false;
        }

        write(context);
        return true;
    }

    /// <summary>The fixed creation date every generated document records, so rebuilding changes nothing that matters.</summary>
    public static readonly DateTimeOffset Created = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    public static void SetProperties(PdfDocument document, string title, string subject)
    {
        document.Properties.Title = title;
        document.Properties.Subject = subject;
        document.Properties.Author = "Broadside sample documents";
        document.Properties.Creator = "Broadside.SampleDocuments";
        document.Properties.Producer = "Broadside";
        document.Properties.CreationDate = Created;
        document.Properties.ModificationDate = Created;
    }

    public static PdfColor Rgb(double r, double g, double b) => new(PdfDeviceRgbColorSpace.Instance, [(float)r, (float)g, (float)b]);

    public static PdfColor Gray(double level) => new(PdfDeviceGrayColorSpace.Instance, [(float)level]);

    public static PdfColor Cmyk(double c, double m, double y, double k) => new(PdfDeviceCmykColorSpace.Instance, [(float)c, (float)m, (float)y, (float)k]);

    public static double Mm(double millimetres) => millimetres * 72 / 25.4;
}
