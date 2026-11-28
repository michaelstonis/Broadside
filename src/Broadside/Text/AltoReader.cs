using System.Xml;

namespace Broadside.Text;

/// <summary>Reads ALTO 2.0 to 4.4 (https://www.loc.gov/standards/alto/) into <see cref="OcrPage"/>s, streaming, by local element name.</summary>
/// <remarks>See <see cref="OcrPage.FromAlto(Stream)"/>. DTDs are ignored and no external entity is resolved.</remarks>
internal static class AltoReader
{
    private const string Format = "ALTO";

    public static IReadOnlyList<OcrPage> Read(byte[] data)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            MaxCharactersFromEntities = 1024,
        };

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return Read(reader);
        }
        catch (XmlException exception)
        {
            throw new FormatException($"The ALTO input is not well-formed XML: {exception.Message}", exception);
        }
    }

    private static List<OcrPage> Read(XmlReader reader)
    {
        var pages = new List<OcrPage>();
        OcrPageBuilder? page = null;
        int pageDepth = -1;

        // The rotation and language each open block or line gives, inherited by what it holds.
        var scopes = new Stack<(int Depth, double Rotation, string? Language)>();
        List<OcrWord>? line = null;
        int lineDepth = -1;
        double lineRotation = 0;
        string? lineLanguage = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                int depth = reader.Depth;
                if (line is not null && depth == lineDepth)
                {
                    page?.AddLine(line, lineRotation, lineLanguage);
                    line = null;
                }

                while (scopes.Count > 0 && scopes.Peek().Depth >= depth)
                {
                    scopes.Pop();
                }

                if (page is not null && depth == pageDepth)
                {
                    pages.Add(page.Build());
                    page = null;
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            bool empty = reader.IsEmptyElement;
            switch (reader.LocalName)
            {
                case "Page":
                    if (page is not null)
                    {
                        pages.Add(page.Build());
                    }

                    var diagnostics = new List<Diagnostics.Diagnostic>();
                    page = new OcrPageBuilder(Format, OcrPageBuilder.Size(reader.GetAttribute("WIDTH")), OcrPageBuilder.Size(reader.GetAttribute("HEIGHT")), diagnostics);
                    pageDepth = reader.Depth;
                    if (empty)
                    {
                        pages.Add(page.Build());
                        page = null;
                    }

                    break;

                case "TextBlock" or "ComposedBlock":
                    if (!empty)
                    {
                        (double rotation, string? language) = Inherit(reader, scopes);
                        scopes.Push((reader.Depth, rotation, language));
                    }

                    break;

                case "TextLine":
                    if (!empty && page is not null)
                    {
                        (lineRotation, lineLanguage) = Inherit(reader, scopes);
                        scopes.Push((reader.Depth, lineRotation, lineLanguage));
                        line = [];
                        lineDepth = reader.Depth;
                    }

                    break;

                case "String":
                    if (line is not null && page is not null)
                    {
                        ReadString(reader, page, line, lineRotation);
                    }

                    break;
            }
        }

        if (page is not null)
        {
            if (line is not null)
            {
                page.AddLine(line, lineRotation, lineLanguage);
            }

            pages.Add(page.Build());
        }

        return pages;
    }

    /// <summary>The element's <c>ROTATION</c> and <c>LANG</c>, else those of the scope it is in.</summary>
    private static (double Rotation, string? Language) Inherit(XmlReader reader, Stack<(int Depth, double Rotation, string? Language)> scopes)
    {
        (double rotation, string? language) = scopes.Count > 0 ? (scopes.Peek().Rotation, scopes.Peek().Language) : (0, null);
        if (OcrPageBuilder.TryNumber(reader.GetAttribute("ROTATION"), out double own))
        {
            rotation = own;
        }

        string? lang = reader.GetAttribute("LANG");
        return (rotation, string.IsNullOrWhiteSpace(lang) ? language : lang.Trim());
    }

    private static void ReadString(XmlReader reader, OcrPageBuilder page, List<OcrWord> line, double lineRotation)
    {
        string text = reader.GetAttribute("CONTENT") ?? string.Empty;
        string? hpos = reader.GetAttribute("HPOS");
        string? vpos = reader.GetAttribute("VPOS");
        string? width = reader.GetAttribute("WIDTH");
        string? height = reader.GetAttribute("HEIGHT");
        if (!OcrPageBuilder.TryNumber(hpos, out double x) || !OcrPageBuilder.TryNumber(vpos, out double y)
            || !OcrPageBuilder.TryNumber(width, out double w) || !OcrPageBuilder.TryNumber(height, out double h) || w < 0 || h < 0)
        {
            page.BoundsInvalid($"String '{OcrPageBuilder.Shorten(text)}' has HPOS '{hpos}', VPOS '{vpos}', WIDTH '{width}' and HEIGHT '{height}'");
            return;
        }

        double confidence = 1;
        if (reader.GetAttribute("WC") is { } wc && (!OcrPageBuilder.TryNumber(wc, out confidence) || confidence is < 0 or > 1))
        {
            page.ConfidenceInvalid(wc);
            confidence = 1;
        }

        double angle = OcrPageBuilder.TryNumber(reader.GetAttribute("ROTATION"), out double rotation) ? rotation - lineRotation : 0;
        line.Add(new OcrWord(text, new PdfRectangle(x, y, x + w, y + h), confidence, angle));
    }
}
