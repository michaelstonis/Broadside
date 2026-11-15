using System.Text;
using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.Layout.Flow;
using Broadside.Structure;
using Broadside.Text;

namespace Broadside.Layout.Tests.Builder;

/// <summary>Shared pieces of the document builder tests (issue #397): fixed options, saving, and structure-tree and text summaries for snapshots.</summary>
internal static class BuilderTestSupport
{
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Options with a fixed clock, so the same input gives the same bytes.</summary>
    public static PdfOptions Options() => new PdfOptions().UseTimeProvider(new FixedTimeProvider(Now));

    /// <summary>A new document with no pages, as the builder starts from, for hand-built twins.</summary>
    public static PdfDocument NewTwin()
    {
        PdfDocument document = PdfDocument.Create(Options());
        document.Pages.RemoveAt(0);
        return document;
    }

    public static byte[] Save(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>The Liberation program standing in for a Standard 14 font, as the default theme embeds it.</summary>
    public static PdfCanvasFont Embed(PdfDocument document, Standard14Font font) => document.EmbedFont(Standard14FontResolver.GetProgram(font));

    /// <summary>The structure tree as indented text: type, then <c>Alt</c>, <c>Lang</c> and table attributes where present.</summary>
    public static string StructureText(PdfDocument document)
    {
        var text = new StringBuilder();
        if (document.StructureTree is not { } tree)
        {
            return "(untagged)";
        }

        foreach (PdfStructureElement element in tree.Children)
        {
            Append(text, element, 0);
        }

        return text.ToString();
    }

    /// <summary>The extracted text of every page, each under a page heading.</summary>
    public static string PagesText(PdfDocument document)
    {
        var text = new StringBuilder();
        for (int index = 0; index < document.Pages.Count; index++)
        {
            text.Append("--- page ").Append(index + 1).AppendLine(" ---");
            text.AppendLine(document.Pages[index].ExtractText(new TextExtractionOptions().WithArtifacts()).PlainText.TrimEnd());
        }

        return text.ToString();
    }

    /// <summary>A shape of the structure tree on one line, such as <c>Document(H1,P)</c>.</summary>
    public static string Shape(IEnumerable<PdfStructureElement> elements) =>
        string.Join(",", elements.Select(element =>
        {
            PdfStructureElement[] children = [.. element.Children.OfType<PdfStructureElement>()];
            return children.Length == 0 ? element.StructureType!.Value : $"{element.StructureType!.Value}({Shape(children)})";
        }));

    private static void Append(StringBuilder text, PdfStructureElement element, int depth)
    {
        text.Append(' ', depth * 2).Append(element.StructureType?.Value ?? "?");
        if (element.AlternateDescription is { } alternate)
        {
            text.Append(" Alt=\"").Append(alternate).Append('"');
        }

        if (element.Language is { } language)
        {
            text.Append(" Lang=").Append(language);
        }

        text.AppendLine();
        foreach (PdfStructureElement child in element.Children.OfType<PdfStructureElement>())
        {
            Append(text, child, depth + 1);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
