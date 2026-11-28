using System.Text;
using Broadside.Css;
using Broadside.Graphics;
using Broadside.Graphics.Svg;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>svg</c> and <c>svg-css</c> targets (issue #643). <c>svg</c>: input that does not look like SVG is wrapped as the content of an
/// <c>svg</c> element; the importer runs in lenient mode, where only <see cref="ArgumentException"/> (malformed XML, no <c>svg</c> root)
/// may escape. Invariants: an imported form has a finite, positive bounding box; the document saves and reopens; the path data parser
/// over the same text yields finite coordinates only. <c>svg-css</c>: the input is a style sheet, parsed twice to the same rules, each
/// selector matched against a small fixed tree, and also read as a declaration list.
/// </summary>
/// <remarks>ISO 32000-2 §8.10; SVG 1.1 §6.3, §8.3; CSS Syntax Module Level 3 §5; Selectors Level 3 §9.</remarks>
internal static class SvgTargets
{
    private static readonly byte[] SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\">"u8.ToArray();
    private static readonly byte[] SvgClose = "</svg>"u8.ToArray();

    public static void Svg(ReadOnlySpan<byte> data)
    {
        byte[] input = SvgDocument.Sniff(data) ? data.ToArray() : [.. SvgOpen, .. data, .. SvgClose];
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form;
        try
        {
            form = document.CreateFormXObjectFromSvg(input, new SvgImportOptions().WithResourceResolver(static _ => null));
        }
        catch (ArgumentException)
        {
            CheckPath(Encoding.UTF8.GetString(data));
            return;
        }

        if (form.BoundingBox is not { } box || !double.IsFinite(box.Width) || !double.IsFinite(box.Height) || box.Width <= 0 || box.Height <= 0)
        {
            throw new InvalidOperationException($"An imported form has the bounding box {form.BoundingBox}.");
        }

        using var saved = new MemoryStream();
        document.Save(saved);
        using PdfDocument reopened = PdfDocument.Open(saved.ToArray());
        if (reopened.Pages.Count != 1)
        {
            throw new InvalidOperationException("The saved document lost its page.");
        }

        CheckPath(Encoding.UTF8.GetString(data));
    }

    public static void Css(ReadOnlySpan<byte> data)
    {
        string text = Encoding.UTF8.GetString(data);
        var sheet = new CssStyleSheet();
        sheet.Add(text, null);
        var again = new CssStyleSheet();
        again.Add(text, null);
        if (sheet.Rules.Count != again.Rules.Count)
        {
            throw new InvalidOperationException("Parsing the same style sheet twice gave different rules.");
        }

        Element tree = Tree();
        foreach (CssRule rule in sheet.Rules)
        {
            if (rule.Selector.Specificity < 0)
            {
                throw new InvalidOperationException("A selector has a negative specificity.");
            }

            for (Element? element = tree; element is not null; element = element.Child)
            {
                _ = rule.Selector.Matches(element);
            }
        }

        var declarations = new List<CssDeclaration>();
        CssStyleSheet.ParseDeclarations(text, declarations, null);
        foreach (CssDeclaration declaration in declarations)
        {
            if (declaration.Property.Length == 0 || declaration.Value.Length == 0)
            {
                throw new InvalidOperationException("A declaration was read with an empty name or value.");
            }

            _ = SvgColorParser.TryParse(declaration.Value, out _);
            _ = SvgLength.TryParse(declaration.Value, out _);
            _ = SvgTransformParser.TryParse(declaration.Value, out Matrix matrix);
            if (!double.IsFinite(matrix.A + matrix.B + matrix.C + matrix.D + matrix.E + matrix.F) && !double.IsNaN(matrix.A))
            {
                throw new InvalidOperationException("A transform list was read as an infinite matrix.");
            }
        }
    }

    private static void CheckPath(string text)
    {
        var path = new SvgPath();
        _ = SvgPathParser.TryParse(text, path);
        foreach (SvgSegment segment in path.Segments)
        {
            if (!double.IsFinite(segment.X) || !double.IsFinite(segment.Y) || !double.IsFinite(segment.X1) || !double.IsFinite(segment.Y1)
                || !double.IsFinite(segment.X2) || !double.IsFinite(segment.Y2))
            {
                throw new InvalidOperationException($"Path data gave a segment that is not finite: {segment}.");
            }
        }
    }

    /// <summary>A chain svg &gt; g.row#main &gt; g &gt; rect[data-state=on] for selectors to match against.</summary>
    private static Element Tree()
    {
        var svg = new Element("svg", null, []);
        var row = new Element("g", svg, new Dictionary<string, string> { ["class"] = "row a", ["id"] = "main" });
        var inner = new Element("g", row, []);
        var rect = new Element("rect", inner, new Dictionary<string, string> { ["data-state"] = "on", ["class"] = "b" });
        svg.Child = row;
        row.Child = inner;
        inner.Child = rect;
        return svg;
    }

    private sealed class Element(string name, Element? parent, Dictionary<string, string> attributes) : ICssElement
    {
        public string LocalName { get; } = name;

        public ICssElement? ParentElement { get; } = parent;

        public ICssElement? PreviousElementSibling => null;

        public Element? Child { get; set; }

        public string? GetAttribute(string name) => attributes.TryGetValue(name, out string? value) ? value : null;
    }
}
