using System.Globalization;
using System.Text;
using Broadside.Layout;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>table-detection</c> target (issue #189): the input read as drawing commands (ruling lines, rectangles, ruled grids with
/// lines left out, and short words and numbers in two fonts) on up to three untagged pages, which are then laid out from their
/// geometry.
/// </summary>
/// <remarks>
/// Spec story 36; ISO 32000-2 §14.8.4.8.3 and §14.8.5.7 (Table 384). Each command is five bytes: an operation and four operands.
/// Byte 0xFF starts a new page. Invariants: detection never throws; the tree passes the layout checks of the <c>structure-tree</c>
/// target and every page holds its content's glyphs exactly once (the <c>layout-detection</c> checks); every detected table's
/// cells tile its grid exactly (each position covered by one cell, a cell in the row it starts in), header rows come first, header
/// links stay in the table, continued parts link both ways; and a second run gives the same tables (detection is deterministic).
/// </remarks>
internal static class TableDetectionTarget
{
    private const int MaxCommands = 400;
    private const int MaxPages = 3;

    private static readonly string[] Words =
        ["12", "3.5", "Total", "Name", "x", "-", "(4)", "Alpha beta", "2,048", "Q1", "Region", "a b c d e f", "99%", "Item", "7"];

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

        string tables = Describe(tree);
        if (tables != Describe(document.ExtractLayout(options)))
        {
            throw new InvalidOperationException("Detecting the same document twice gave different tables.");
        }
    }

    /// <summary>Checks every table of the tree and describes their grids.</summary>
    private static string Describe(LayoutTree tree)
    {
        var text = new StringBuilder();
        foreach (LayoutTable table in tree.Root.Children.OfType<LayoutTable>())
        {
            Check(table);
            text.Append(CultureInfo.InvariantCulture, $"{table.RowCount}x{table.ColumnCount}:");
            foreach (LayoutTableCell cell in table.Children.SelectMany(static row => row.Children).Cast<LayoutTableCell>())
            {
                text.Append(CultureInfo.InvariantCulture, $"[{cell.Row},{cell.Column},{cell.RowSpan},{cell.ColumnSpan},{cell.IsHeader}]{cell.Text}");
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static void Check(LayoutTable table)
    {
        // Issue #190: a caption is the first or last child; the rest are rows.
        List<LayoutNode> rows = [.. table.Children.Where(static child => child.Kind != LayoutNodeKind.Caption)];
        if (table.RowCount != rows.Count || table.ColumnCount < 1)
        {
            throw new InvalidOperationException("A detected table's row count is not its rows.");
        }

        bool[] covered = new bool[table.RowCount * table.ColumnCount];
        int headerRows = -1;
        for (int row = 0; row < rows.Count; row++)
        {
            foreach (LayoutNode node in rows[row].Children)
            {
                if (node is not LayoutTableCell cell || cell.Row != row || cell.Column < 0 || cell.RowSpan < 1 || cell.ColumnSpan < 1
                    || cell.Row + cell.RowSpan > table.RowCount || cell.Column + cell.ColumnSpan > table.ColumnCount)
                {
                    throw new InvalidOperationException("A detected cell lies outside its table's grid.");
                }

                for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
                {
                    for (int c = cell.Column; c < cell.Column + cell.ColumnSpan; c++)
                    {
                        if (covered[(r * table.ColumnCount) + c])
                        {
                            throw new InvalidOperationException("Two detected cells cover one grid position.");
                        }

                        covered[(r * table.ColumnCount) + c] = true;
                    }
                }

                if (!cell.IsHeader && headerRows < 0)
                {
                    headerRows = row;
                }
                else if (cell.IsHeader && headerRows >= 0 && row >= headerRows)
                {
                    throw new InvalidOperationException("A header row comes after a data row.");
                }

                if (cell.Headers.Any(header => !ReferenceEquals(header.Parent?.Parent, table) || !header.IsHeader))
                {
                    throw new InvalidOperationException("A detected cell's header is not a header cell of its table.");
                }
            }
        }

        foreach (bool position in covered)
        {
            if (!position)
            {
                throw new InvalidOperationException("A grid position of a detected table has no cell.");
            }
        }

        if ((table.NextPart is { } next && !ReferenceEquals(next.PreviousPart, table)) || (table.PreviousPart is { } previous && !ReferenceEquals(previous.NextPart, table)))
        {
            throw new InvalidOperationException("Continued table parts do not link both ways.");
        }
    }

    /// <summary>The file the commands draw.</summary>
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

    /// <summary>Appends one command: a line, a word, a rectangle or a ruled grid with words in its cells.</summary>
    private static void Draw(StringBuilder content, ReadOnlySpan<byte> command)
    {
        double x = command[1] * 2.4;
        double y = command[2] * 3.1;
        switch (command[0] % 5)
        {
            case 0:
                content.Append(CultureInfo.InvariantCulture, $"{x} {y} m {x + (command[3] * 2) + 1} {y} l S\n");
                break;
            case 1:
                content.Append(CultureInfo.InvariantCulture, $"{x} {y} m {x} {y + (command[3] * 2) + 1} l S\n");
                break;
            case 2:
                content.Append(CultureInfo.InvariantCulture, $"BT /F{(command[4] & 1) + 1} {6 + (command[4] % 8)} Tf {x} {y} Td ({Words[command[3] % Words.Length]}) Tj ET\n");
                break;
            case 3:
                content.Append(CultureInfo.InvariantCulture, $"{x} {y} {command[3] + 1} {(command[4] % 40) + 1} re {((command[4] & 0x80) != 0 ? 'f' : 'S')}\n");
                break;
            default:
                Grid(content, x, y, command);
                break;
        }
    }

    /// <summary>A ruled grid at (x, y) going down, inner lines left out by the operand bits, a word in most cells.</summary>
    private static void Grid(StringBuilder content, double x, double y, ReadOnlySpan<byte> command)
    {
        int rows = (command[3] % 6) + 2;
        int columns = (command[4] % 5) + 2;
        double width = 30 + (command[3] % 50);
        double height = 12 + (command[4] % 10);
        y = Math.Max(y, rows * height);
        int bits = command[3] ^ (command[4] << 8) ^ (command[0] << 3);
        for (int row = 0; row <= rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                if (row == 0 || row == rows || ((bits >> ((row + column) % 16)) & 1) == 0)
                {
                    content.Append(CultureInfo.InvariantCulture, $"{x + (column * width)} {y - (row * height)} m {x + ((column + 1) * width)} {y - (row * height)} l S\n");
                }
            }
        }

        for (int column = 0; column <= columns; column++)
        {
            for (int row = 0; row < rows; row++)
            {
                if (column == 0 || column == columns || ((bits >> ((row * 3 + column) % 16)) & 2) == 0)
                {
                    content.Append(CultureInfo.InvariantCulture, $"{x + (column * width)} {y - (row * height)} m {x + (column * width)} {y - ((row + 1) * height)} l S\n");
                }
            }
        }

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                if (((bits >> ((row + (column * 5)) % 16)) & 4) == 0)
                {
                    content.Append(CultureInfo.InvariantCulture, $"BT /F{(row == 0 ? 2 : 1)} 8 Tf {x + (column * width) + 3} {y - (row * height) - height + 3} Td ({Words[(row * 7 + column + command[1]) % Words.Length]}) Tj ET\n");
                }
            }
        }
    }
}
