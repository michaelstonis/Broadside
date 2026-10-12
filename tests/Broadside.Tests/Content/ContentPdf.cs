using System.Text;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Tests.Document;

namespace Broadside.Tests.Content;

/// <summary>
/// Builds one-page documents whose page content is the given bytes, for content-stream vectors the corpus does not have
/// (ISO 32000-2 §7.8.2). One part is written as a single stream; several parts as a <c>Contents</c> array of streams, the case
/// Table 31 says is read as their concatenation.
/// </summary>
internal static class ContentPdf
{
    /// <summary>A one-page document whose content stream is <paramref name="parts"/>, each part a separate stream when there are several.</summary>
    public static byte[] Build(params string[] parts)
    {
        string contents = parts.Length == 1 ? "/Contents 4 0 R" : $"/Contents [{string.Join(' ', parts.Select((_, index) => $"{index + 4} 0 R"))}]";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> {contents} >>",
            .. parts.Select(part => $"<< /Length {Encoding.Latin1.GetByteCount(part)} >>\nstream\n{part}\nendstream"),
        ];
        return new TestPdf().Build(objects);
    }

    /// <summary>
    /// A one-page document whose content stream (object 4) is <paramref name="content"/>, whose page resources are
    /// <paramref name="resources"/> (the text between <c>&lt;&lt;</c> and <c>&gt;&gt;</c>) and whose further objects, numbered from 5,
    /// are <paramref name="objects"/>.
    /// </summary>
    public static byte[] BuildWith(string resources, string content, params string[] objects) =>
        BuildWithCatalog(string.Empty, resources, content, objects);

    /// <summary>As <see cref="BuildWith"/>, with extra catalog entries.</summary>
    public static byte[] BuildWithCatalog(string catalogEntries, string resources, string content, params string[] objects) => new TestPdf().Build(
    [
        $"<< /Type /Catalog /Pages 2 0 R {catalogEntries} >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << {resources} >> /Contents 4 0 R >>",
        Stream(content),
        .. objects,
    ]);

    /// <summary>A stream object whose data is <paramref name="data"/>, with <paramref name="entries"/> in its dictionary.</summary>
    public static string Stream(string data, string entries = "") =>
        $"<< {entries} /Length {Encoding.Latin1.GetByteCount(data)} >>\nstream\n{data}\nendstream";

    /// <summary>The page resources that name Helvetica (object 5, <see cref="Helvetica"/>) as /F1.</summary>
    public const string HelveticaResources = "/Font << /F1 5 0 R >>";

    /// <summary>A non-embedded Helvetica with WinAnsiEncoding.</summary>
    public const string Helvetica = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

    /// <summary>Opens a one-page document with <paramref name="content"/> and records the events of its page.</summary>
    public static (RecordingProcessor Events, IReadOnlyList<Diagnostic> Diagnostics) Run(string content, ContentEvents events = ContentEvents.All) =>
        Run([content], events);

    /// <summary>Opens a one-page document with the content parts and records the events of its page.</summary>
    public static (RecordingProcessor Events, IReadOnlyList<Diagnostic> Diagnostics) Run(string[] parts, ContentEvents events = ContentEvents.All)
    {
        using PdfDocument document = PdfDocument.Open(Build(parts));
        var processor = new RecordingProcessor(events);
        document.Pages[0].ProcessContent(processor);
        return (processor, document.Diagnostics);
    }

    /// <summary>The codes of the diagnostics, in order.</summary>
    public static string[] Codes(IReadOnlyList<Diagnostic> diagnostics) => [.. diagnostics.Select(diagnostic => diagnostic.Code)];
}
