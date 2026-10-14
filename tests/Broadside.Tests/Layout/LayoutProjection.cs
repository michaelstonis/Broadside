using System.Globalization;
using System.Text;
using Broadside.Layout;

namespace Broadside.Tests.Layout;

/// <summary>Prints a layout tree for Verify: one line per node with its kind, its kind's data, its extents and its text.</summary>
internal static class LayoutProjection
{
    public static string Of(LayoutTree tree, int maxNodes = int.MaxValue, int maxText = int.MaxValue)
    {
        var output = new StringBuilder();
        int printed = 0;
        int total = 0;
        Append(output, tree.Root, 0, ref printed, ref total, maxNodes, maxText);
        if (total > printed)
        {
            output.Append(CultureInfo.InvariantCulture, $"... {total - printed} more nodes\n");
        }

        return output.ToString();
    }

    private static void Append(StringBuilder output, LayoutNode node, int depth, ref int printed, ref int total, int maxNodes, int maxText)
    {
        total++;
        if (printed < maxNodes)
        {
            printed++;
            output.Append(' ', depth * 2).Append(node.Kind);
            switch (node)
            {
                case LayoutHeading heading:
                    output.Append(CultureInfo.InvariantCulture, $" level {heading.Level}");
                    break;
                case LayoutTable table:
                    output.Append(CultureInfo.InvariantCulture, $" {table.RowCount}x{table.ColumnCount}");
                    break;
                case LayoutTableCell cell:
                    output.Append(CultureInfo.InvariantCulture, $" ({cell.Row},{cell.Column})");
                    if (cell.IsHeader)
                    {
                        output.Append(" header");
                    }

                    if (cell.RowSpan != 1 || cell.ColumnSpan != 1)
                    {
                        output.Append(CultureInfo.InvariantCulture, $" span {cell.RowSpan}x{cell.ColumnSpan}");
                    }

                    if (cell.Headers.Count > 0)
                    {
                        output.Append(" headers ").AppendJoin(' ', cell.Headers.Select(static header => $"({header.Row},{header.Column})"));
                    }

                    break;
            }

            if (node.StructureElement?.StructureType is { } type && type.Value != node.Kind.ToString())
            {
                output.Append(" /").Append(type.Value);
            }

            if (node.AlternateDescription is { } alternate)
            {
                output.Append(" alt \"").Append(alternate).Append('"');
            }

            if (node.Language is { } language && language != node.Parent?.Language)
            {
                output.Append(" lang ").Append(language);
            }

            foreach (Broadside.Annotations.PdfAnnotation annotation in node.Annotations)
            {
                output.Append(" annot ").Append(annotation.GetType().Name);
            }

            foreach (LayoutExtent extent in node.Extents)
            {
                output.Append(CultureInfo.InvariantCulture, $" p{extent.Page.PageIndex + 1}");
                if (extent.Bounds is { } bounds)
                {
                    output.Append(CultureInfo.InvariantCulture, $" [{bounds.Left:0.#} {bounds.Bottom:0.#} {bounds.Right:0.#} {bounds.Top:0.#}]");
                }

                output.Append(CultureInfo.InvariantCulture, $" g{extent.GlyphStart}+{extent.GlyphCount} t{extent.TextStart}+{extent.TextLength}");
            }

            if ((node.Children.Count == 0 || node.Kind is LayoutNodeKind.Paragraph or LayoutNodeKind.Heading) && node.Text.Length > 0)
            {
                output.Append(": ").Append(Escape(node.Text, maxText));
            }

            output.Append('\n');
        }

        foreach (LayoutNode child in node.Children)
        {
            Append(output, child, depth + 1, ref printed, ref total, maxNodes, maxText);
        }
    }

    private static string Escape(string text, int maxText)
    {
        var escaped = new StringBuilder();
        foreach (char unit in text.Length > maxText ? text[..maxText] : text)
        {
            escaped.Append(unit switch
            {
                '\n' => " / ",
                _ when char.IsControl(unit) || char.IsSurrogate(unit) => $"\\u{(int)unit:X4}",
                _ => unit.ToString(),
            });
        }

        return text.Length > maxText ? escaped.Append(" ...").ToString() : escaped.ToString();
    }
}
