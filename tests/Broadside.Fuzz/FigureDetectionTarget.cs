using System.Globalization;
using System.Text;
using Broadside.Layout;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>figure-detection</c> target (issue #190): the input read as drawing commands (filled boxes, curves, lines, small images,
/// visible and invisible words including caption labels) on up to three untagged pages, which are then laid out from their
/// geometry.
/// </summary>
/// <remarks>
/// Spec story 36; ISO 32000-2 §14.8.4.8.4 and §14.8.4.8.5. Each command is five bytes: an operation and four operands. Byte 0xFF
/// starts a new page. Invariants: detection never throws; the tree passes the layout checks of the <c>structure-tree</c> target
/// and every page holds its content's glyphs exactly once (the <c>layout-detection</c> checks); every figure has a finite bounding
/// box; a figure or table has at most one caption, its first or last child, inside the owner's extent; and a second run gives the
/// same figures (detection is deterministic).
/// </remarks>
internal static class FigureDetectionTarget
{
    private const int MaxCommands = 400;
    private const int MaxPages = 3;

    private static readonly string[] Words =
    [
        "Figure 1:", "Fig. 2.", "Table 3", "FIGURE IV.", "Chart A Sales", "(a)", "(b)", "Q1", "Sales", "Figure 7 shows",
        "running text of a paragraph", "x", "12", "Label", "Exhibit 3 -",
    ];

    public static void Target(ReadOnlySpan<byte> data)
    {
        byte[] file = Build(data);
        using PdfDocument? document = FuzzTargets.OpenOrNull(file);
        if (document is null)
        {
            return;
        }

        LayoutOptions options = new LayoutOptions().WithDetection(LayoutDetection.AllPages);
        LayoutTree tree = document.ExtractLayout(options);
        StructureTree.CheckLayout(tree);
        foreach (LayoutPage page in tree.Pages)
        {
            LayoutDetectionTarget.CheckPartition(page);
        }

        string figures = Describe(tree);
        if (figures != Describe(document.ExtractLayout(options)))
        {
            throw new InvalidOperationException("Detecting the same document twice gave different figures.");
        }
    }

    /// <summary>Checks every figure and caption of the tree and describes them.</summary>
    private static string Describe(LayoutTree tree)
    {
        var text = new StringBuilder();
        foreach (LayoutNode node in tree.Root.Children)
        {
            if (node is LayoutFigure figure)
            {
                if (figure.BoundingBox is not { } box || !double.IsFinite(box.Left) || !double.IsFinite(box.Top) || box.Width < 0 || box.Height < 0)
                {
                    throw new InvalidOperationException("A detected figure has no finite bounding box.");
                }

                text.Append(CultureInfo.InvariantCulture, $"F{box}:{figure.Text}");
            }

            if (node.Kind is LayoutNodeKind.Figure or LayoutNodeKind.Table)
            {
                CheckCaption(node);
            }

            text.Append(node.Kind).Append('\n');
        }

        return text.ToString();
    }

    private static void CheckCaption(LayoutNode owner)
    {
        int captions = 0;
        for (int index = 0; index < owner.Children.Count; index++)
        {
            LayoutNode child = owner.Children[index];
            if (child.Kind != LayoutNodeKind.Caption)
            {
                continue;
            }

            captions++;
            LayoutExtent caption = child.Extents[0];
            LayoutExtent whole = owner.Extents[0];
            if ((index != 0 && index != owner.Children.Count - 1) || caption.GlyphStart < whole.GlyphStart
                || caption.GlyphStart + caption.GlyphCount > whole.GlyphStart + whole.GlyphCount)
            {
                throw new InvalidOperationException("A caption is not at an end of its owner or lies outside its extent.");
            }
        }

        if (captions > 1)
        {
            throw new InvalidOperationException("A figure or table has more than one caption.");
        }
    }

    private static byte[] Build(ReadOnlySpan<byte> data)
    {
        var pages = new List<StringBuilder> { new() };
        Span<byte> operands = stackalloc byte[5];
        int commands = 0;
        for (int index = 0; index < data.Length && commands < MaxCommands; commands++)
        {
            if (data[index] == 0xFF)
            {
                index++;
                if (pages.Count < MaxPages)
                {
                    pages.Add(new StringBuilder());
                }

                continue;
            }

            operands.Clear();
            int count = Math.Min(5, data.Length - index);
            data.Slice(index, count).CopyTo(operands);
            index += count;
            Draw(pages[^1], operands);
        }

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pages.Count).Select(static page => string.Create(CultureInfo.InvariantCulture, $"{3 + (2 * page)} 0 R")))}] /Count {pages.Count} >>",
        };
        int fonts = 3 + (2 * pages.Count);
        foreach (StringBuilder content in pages)
        {
            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fonts} 0 R /F2 {fonts + 1} 0 R >> >> /Contents {objects.Count + 2} 0 R >>"));
            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        }

        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(text.Length);
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    private static void Draw(StringBuilder content, ReadOnlySpan<byte> command)
    {
        double x = command[1] * 2.4;
        double y = command[2] * 3.1;
        double a = command[3];
        double b = command[4];
        switch (command[0] % 6)
        {
            case 0:
                content.Append(CultureInfo.InvariantCulture, $"{a / 255} 0.4 0.6 rg {x} {y} {a + 1} {b + 1} re f 0 g\n");
                break;
            case 1:
                content.Append(CultureInfo.InvariantCulture, $"{x} {y} m {x + a} {y + b} {x + (2 * a)} {y - b} {x + b} {y} c {((command[0] & 8) != 0 ? "f" : "S")}\n");
                break;
            case 2:
                if ((command[0] & 8) != 0)
                {
                    content.Append(CultureInfo.InvariantCulture, $"{x} {y} m {x + (a * 2)} {y} l S\n");
                }
                else
                {
                    content.Append(CultureInfo.InvariantCulture, $"{x} {y} m {x} {y + (b * 2)} l S\n");
                }

                break;
            case 3:
                content.Append(CultureInfo.InvariantCulture, $"q {a + 1} 0 0 {b + 1} {x} {y} cm BI /W 2 /H 2 /CS /G /BPC 8 /F /AHx ID {command[3]:X2}{command[4]:X2}{command[1]:X2}{command[2]:X2}> EI Q\n");
                break;
            default:
                string word = Words[command[3] % Words.Length];
                string mode = command[0] % 6 == 5 ? "3 Tr " : string.Empty;
                content.Append(CultureInfo.InvariantCulture, $"q BT {mode}/F{1 + (command[4] & 1)} {6 + (command[4] % 12)} Tf {x} {y} Td ({word}) Tj ET Q\n");
                break;
        }
    }
}
