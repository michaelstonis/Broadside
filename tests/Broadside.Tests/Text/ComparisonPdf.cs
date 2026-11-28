using System.Text;
using Broadside.Tests.Content;
using Broadside.Tests.Document;

namespace Broadside.Tests.Text;

/// <summary>Builds the small documents the comparison tests compare (issue #644).</summary>
internal static class ComparisonPdf
{
    /// <summary>
    /// A file of Letter pages, each showing its lines in Helvetica 12 (WinAnsiEncoding) from (72, 700) down 14 points a line, and,
    /// when <paramref name="footers"/> has an entry for the page, that text as an <c>Artifact</c> at (300, 40).
    /// </summary>
    public static byte[] Pages(string[][] pages, string[]? footers = null)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pages.Select((_, i) => $"{4 + (2 * i)} 0 R"))}] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        for (int page = 0; page < pages.Length; page++)
        {
            string[] lines = pages[page];
            var content = new StringBuilder("BT /F1 12 Tf 14 TL 72 700 Td");
            for (int i = 0; i < lines.Length; i++)
            {
                content.Append(i == 0 ? " (" : " T* (").Append(lines[i]).Append(") Tj");
            }

            content.Append(" ET");
            if (footers is not null && page < footers.Length)
            {
                content.Append(" /Artifact BMC BT /F1 10 Tf 300 40 Td (").Append(footers[page]).Append(") Tj ET EMC");
            }

            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {objects.Count + 2} 0 R >>");
            objects.Add(ContentPdf.Stream(content.ToString()));
        }

        return new TestPdf().Build([.. objects]);
    }

    /// <summary>A one-page file of lines.</summary>
    public static byte[] Lines(params string[] lines) => Pages([lines]);
}
