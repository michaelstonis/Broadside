using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>
/// The GFM table extension: a header row, a delimiter row of <c>---</c> cells with optional colons for alignment, then body rows
/// until a blank line or another block starts. Cells split on unescaped pipes; <c>\|</c> is a pipe inside a cell, code spans included.
/// </summary>
internal static class MarkdownTables
{
    /// <summary>The column alignments when <paramref name="line"/> is a delimiter row, else null.</summary>
    public static MarkdownAlignment[]? DelimiterRow(ReadOnlySpan<char> line)
    {
        List<string> cells = SplitRow(line);
        if (cells.Count == 0)
        {
            return null;
        }

        var alignments = new MarkdownAlignment[cells.Count];
        for (int index = 0; index < cells.Count; index++)
        {
            string cell = cells[index];
            bool left = cell.StartsWith(':');
            bool right = cell.Length > 1 && cell.EndsWith(':');
            ReadOnlySpan<char> dashes = cell.AsSpan(left ? 1 : 0, cell.Length - (left ? 1 : 0) - (right ? 1 : 0));
            if (dashes.IsEmpty || dashes.ContainsAnyExcept('-'))
            {
                return null;
            }

            alignments[index] = left && right ? MarkdownAlignment.Center : left ? MarkdownAlignment.Left : right ? MarkdownAlignment.Right : MarkdownAlignment.None;
        }

        return alignments;
    }

    /// <summary>The trimmed cells of a row, outer pipes dropped, <c>\|</c> turned into a pipe.</summary>
    public static List<string> SplitRow(ReadOnlySpan<char> line)
    {
        var cells = new List<string>();
        ReadOnlySpan<char> row = line.Trim(" \t\n\r\f\v");
        if (row.StartsWith('|'))
        {
            row = row[1..];
        }

        if (row.EndsWith('|') && !(row.Length >= 2 && row[^2] == '\\'))
        {
            row = row[..^1];
        }

        if (row.IsEmpty && line.Trim(" \t").Length <= 1)
        {
            return cells;
        }

        var cell = new StringBuilder();
        for (int index = 0; index < row.Length; index++)
        {
            char c = row[index];
            if (c == '\\' && index + 1 < row.Length && row[index + 1] == '|')
            {
                cell.Append('|');
                index++;
            }
            else if (c == '|')
            {
                cells.Add(cell.ToString().Trim(" \t".ToCharArray()));
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString().Trim(" \t".ToCharArray()));
        return cells;
    }

    /// <summary>Turns a finished table block's lines into its header row, body rows and cells, each body row cut or padded to the header's width.</summary>
    public static void Build(MarkdownNode table)
    {
        string content = table.Content!.ToString();
        table.Content = null;
        MarkdownAlignment[] alignments = table.Alignments!;
        bool header = true;
        foreach (Range range in content.AsSpan().TrimEnd('\n').Split('\n'))
        {
            // The delimiter row's line reaches the block empty; a row is never blank, as a blank line ends the table.
            if (MarkdownText.IsBlank(content.AsSpan()[range]))
            {
                continue;
            }

            List<string> cells = SplitRow(content.AsSpan()[range]);
            var row = new MarkdownNode(MarkdownKind.TableRow) { IsHeader = header, IsOpen = false };
            for (int column = 0; column < alignments.Length; column++)
            {
                row.AppendChild(new MarkdownNode(MarkdownKind.TableCell)
                {
                    IsOpen = false,
                    Alignments = [alignments[column]],
                    Content = new StringBuilder(column < cells.Count ? cells[column] : string.Empty),
                });
            }

            table.AppendChild(row);
            header = false;
        }
    }
}
