using System.Globalization;
using System.Text;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>A text run of a <see cref="TaggedSample"/>: shown in one font resource, as one marked-content sequence of its element.</summary>
/// <param name="Text">The text, in WinAnsi characters.</param>
/// <param name="Font">The font resource: <c>F1</c> to <c>F8</c> (see <see cref="TaggedSample"/>).</param>
internal sealed record Run(string Text, string Font = "F1");

/// <summary>A structure element of a <see cref="TaggedSample"/>: its type, extra dictionary entries, and its kids (<see cref="Run"/>s, elements, or <see cref="TaggedSample.PageBreak"/>).</summary>
/// <param name="Type">The structure type.</param>
/// <param name="Kids">The kids, in order.</param>
internal sealed record Element(string Type, params object[] Kids)
{
    /// <summary>Gets the extra entries of the element's dictionary, such as <c>/Alt (...)</c> or <c>/A &lt;&lt; ... &gt;&gt;</c>.</summary>
    public string Entries { get; init; } = string.Empty;

    /// <summary>Gets the action of the link annotation the element owns (a <c>Link</c>), such as <c>&lt;&lt; /S /URI /URI (...) &gt;&gt;</c>; none when <see langword="null"/>.</summary>
    public string? LinkAction { get; init; }
}

/// <summary>
/// Builds a tagged file (ISO 32000-2 §14.7) from an element tree: each run is a line of text, or part of one, in its element's
/// marked-content sequence, block elements start a new line, and a link element owns a link annotation over its line.
/// </summary>
/// <remarks>
/// Fonts: <c>F1</c> Helvetica, <c>F2</c> Helvetica-Bold, <c>F3</c> Helvetica-Oblique, <c>F4</c> Helvetica-BoldOblique; and Helvetica
/// with a font descriptor that gives <c>F5</c> the <c>ForceBold</c> flag, <c>F6</c> a <c>FontWeight</c> of 700, <c>F7</c> an
/// <c>ItalicAngle</c> of -12, <c>F8</c> the <c>Italic</c> flag (Tables 120 and 121).
/// </remarks>
internal static class TaggedSample
{
    /// <summary>A kid that ends the page: the content after it is on the next page.</summary>
    public static readonly object PageBreak = new();

    private static readonly HashSet<string> InlineTypes = new(StringComparer.Ordinal) { "Span", "Link", "Note", "Lbl", "Em", "Strong", "Code" };

    /// <summary>Builds the file.</summary>
    /// <param name="root">The top-level element, usually a <c>Document</c>.</param>
    /// <param name="artifact">Text shown at the top of every page as an artifact (§14.8.2.2), or <see langword="null"/>.</param>
    /// <returns>The file.</returns>
    public static byte[] Build(Element root, string? artifact = null)
    {
        int pageCount = 1 + Count(root);
        var objects = new List<string?> { null, null, null, null };

        // 1 catalog, 2 pages, 3 structure tree root, 4 font resources; then the pages and their contents.
        int[] pages = [.. Enumerable.Range(0, pageCount).Select(_ => Reserve(objects))];
        int[] contents = [.. Enumerable.Range(0, pageCount).Select(_ => Reserve(objects))];
        var streams = Enumerable.Range(0, pageCount).Select(static _ => new StringBuilder()).ToArray();
        var parents = Enumerable.Range(0, pageCount).Select(static _ => new List<int>()).ToArray();
        var annotations = Enumerable.Range(0, pageCount).Select(static _ => new List<int>()).ToArray();
        var annotationParents = new List<int>();
        int page = 0;
        double y = 760;
        bool inLine = false;

        if (artifact is not null)
        {
            foreach (StringBuilder stream in streams)
            {
                stream.Append(CultureInfo.InvariantCulture, $"/Artifact << /Type /Pagination >> BDC BT /F1 8 Tf 72 780 Td ({Escape(artifact)}) Tj ET EMC\n");
            }
        }

        int top = Walk(root, 3);
        objects[0] = "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 3 0 R /Lang (en) >>";
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', pages.Select(static number => $"{number} 0 R"))}] /Count {pageCount} >>";
        var nums = new StringBuilder();
        for (int index = 0; index < pageCount; index++)
        {
            nums.Append(CultureInfo.InvariantCulture, $"{index} [{string.Join(' ', parents[index].Select(static number => $"{number} 0 R"))}] ");
        }

        for (int index = 0; index < annotationParents.Count; index++)
        {
            nums.Append(CultureInfo.InvariantCulture, $"{pageCount + index} {annotationParents[index]} 0 R ");
        }

        objects[2] = $"<< /Type /StructTreeRoot /K [{top} 0 R] /ParentTree << /Nums [{nums}] >> >>";
        objects[3] = "<< /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >> "
            + "/F2 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >> "
            + "/F3 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >> "
            + "/F4 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-BoldOblique /Encoding /WinAnsiEncoding >> "
            + Described("F5", "/Flags 262176 /ItalicAngle 0") + Described("F6", "/Flags 32 /FontWeight 700 /ItalicAngle 0")
            + Described("F7", "/Flags 32 /ItalicAngle -12") + Described("F8", "/Flags 96 /ItalicAngle 0") + ">>";
        for (int index = 0; index < pageCount; index++)
        {
            Line();
            string annots = annotations[index].Count == 0 ? string.Empty : $" /Annots [{string.Join(' ', annotations[index].Select(static number => $"{number} 0 R"))}]";
            objects[pages[index] - 1] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font 4 0 R >> /Contents {contents[index]} 0 R /StructParents {index}{annots} >>";
            objects[contents[index] - 1] = ContentSamples.Stream(streams[index].ToString());
        }

        return ContentSamples.File([.. objects.Select(static entry => entry!)]);

        int Walk(Element element, int parent)
        {
            int number = Reserve(objects);
            int startPage = page;
            bool block = !InlineTypes.Contains(element.Type);
            if (block)
            {
                Line();
            }

            var kids = new List<string>();
            foreach (object kid in element.Kids)
            {
                switch (kid)
                {
                    case Run run:
                        if (!inLine)
                        {
                            streams[page].Append(CultureInfo.InvariantCulture, $"BT 72 {y} Td ");
                            inLine = true;
                        }

                        int mcid = parents[page].Count;
                        parents[page].Add(number);
                        streams[page].Append(CultureInfo.InvariantCulture, $"/{element.Type} <</MCID {mcid}>> BDC /{run.Font} 10 Tf ({Escape(run.Text)}) Tj EMC ");
                        kids.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Type /MCR /Pg {pages[page]} 0 R /MCID {mcid} >>"));
                        break;
                    case Element child:
                        kids.Add(string.Create(CultureInfo.InvariantCulture, $"{Walk(child, number)} 0 R"));
                        break;
                    default:
                        Line();
                        page++;
                        y = 760;
                        break;
                }
            }

            if (element.LinkAction is { } action)
            {
                int annotation = Reserve(objects);
                int structParent = pageCount + annotationParents.Count;
                annotationParents.Add(number);
                annotations[page].Add(annotation);
                objects[annotation - 1] = string.Create(CultureInfo.InvariantCulture, $"<< /Type /Annot /Subtype /Link /Rect [72 {y - 3} 540 {y + 10}] /Border [0 0 0] /StructParent {structParent} /A {action} >>");
                kids.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Type /OBJR /Pg {pages[page]} 0 R /Obj {annotation} 0 R >>"));
            }

            if (block)
            {
                Line();
            }

            objects[number - 1] = string.Create(CultureInfo.InvariantCulture, $"<< /Type /StructElem /S /{element.Type} /P {parent} 0 R /Pg {pages[startPage]} 0 R /K [{string.Join(' ', kids)}] {element.Entries} >>");
            return number;
        }

        void Line()
        {
            if (inLine)
            {
                streams[page].Append("ET\n");
                inLine = false;
                y -= 16;
            }
        }
    }

    private static string Described(string name, string entries) =>
        $"/{name} << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 126 /Widths [{string.Join(' ', Enumerable.Repeat("556", 95))}] "
        + $"/FontDescriptor << /Type /FontDescriptor /FontName /Helvetica {entries} /FontBBox [-166 -225 1000 931] /Ascent 718 /Descent -207 /CapHeight 718 /StemV 88 >> >> ";

    private static int Count(Element element) =>
        element.Kids.Sum(static kid => kid switch { Element child => Count(child), Run => 0, _ => 1 });

    private static int Reserve(List<string?> objects)
    {
        objects.Add(null);
        return objects.Count;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
}
