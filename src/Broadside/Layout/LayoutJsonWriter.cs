using System.Text.Encodings.Web;
using System.Text.Json;

namespace Broadside.Layout;

/// <summary>Writes a layout tree as JSON, version <see cref="Schema"/> of the layout schema (issue #629); see <see cref="LayoutTree.SaveJson"/>.</summary>
/// <remarks>ISO 32000-2 §14.8.4. Reads only the tree.</remarks>
internal static class LayoutJsonWriter
{
    /// <summary>The schema version; it changes only when a property changes meaning, never when one is added.</summary>
    public const int Schema = 1;

    public static void Write(LayoutTree tree, Stream stream, LayoutJsonOptions options)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = options.Indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        Node(writer, tree.Root, options, isRoot: true);
        writer.Flush();
    }

    /// <summary>Gets the JSON name of a node kind: its name in camel case.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name.</returns>
    public static string KindName(LayoutNodeKind kind) => kind switch
    {
        LayoutNodeKind.Document => "document",
        LayoutNodeKind.Section => "section",
        LayoutNodeKind.Paragraph => "paragraph",
        LayoutNodeKind.Heading => "heading",
        LayoutNodeKind.List => "list",
        LayoutNodeKind.ListItem => "listItem",
        LayoutNodeKind.Label => "label",
        LayoutNodeKind.ListBody => "listBody",
        LayoutNodeKind.Table => "table",
        LayoutNodeKind.TableRow => "tableRow",
        LayoutNodeKind.TableCell => "tableCell",
        LayoutNodeKind.Figure => "figure",
        LayoutNodeKind.Caption => "caption",
        LayoutNodeKind.Span => "span",
        LayoutNodeKind.Link => "link",
        LayoutNodeKind.Note => "note",
        _ => kind.ToString(),
    };

    private static void Node(Utf8JsonWriter writer, LayoutNode node, LayoutJsonOptions options, bool isRoot)
    {
        writer.WriteStartObject();
        if (isRoot)
        {
            writer.WriteNumber("schema", Schema);
        }

        writer.WriteString("kind", KindName(node.Kind));
        if (node.StructureElement?.StructureType?.Value is { } type)
        {
            writer.WriteString("structureType", type);
        }

        switch (node)
        {
            case LayoutHeading heading:
                writer.WriteNumber("level", heading.Level);
                break;
            case LayoutList list:
                writer.WriteString("numbering", list.Numbering);
                break;
            case LayoutTable table:
                writer.WriteNumber("rows", table.RowCount);
                writer.WriteNumber("columns", table.ColumnCount);
                if (table.PreviousPart is not null)
                {
                    writer.WriteBoolean("previousPart", true);
                }

                if (table.NextPart is not null)
                {
                    writer.WriteBoolean("nextPart", true);
                }

                break;
            case LayoutTableCell cell:
                writer.WriteNumber("row", cell.Row);
                writer.WriteNumber("column", cell.Column);
                writer.WriteNumber("rowSpan", cell.RowSpan);
                writer.WriteNumber("columnSpan", cell.ColumnSpan);
                writer.WriteBoolean("header", cell.IsHeader);
                break;
        }

        if (node.Kind == LayoutNodeKind.Link && LayoutInlines.UriOf(node) is { } uri)
        {
            writer.WriteString("uri", uri);
        }

        if (node.AlternateDescription is { } alternate)
        {
            writer.WriteString("alt", alternate);
        }

        if (node.Language is { } language && (node.Parent is null || !string.Equals(language, node.Parent.Language, StringComparison.Ordinal)))
        {
            writer.WriteString("lang", language);
        }

        bool container = node.Kind is LayoutNodeKind.Document or LayoutNodeKind.Section or LayoutNodeKind.List or LayoutNodeKind.ListItem
            or LayoutNodeKind.ListBody or LayoutNodeKind.Table or LayoutNodeKind.TableRow;
        if (!(container && node.Children.Count > 0) && node.Text is { Length: > 0 } text)
        {
            writer.WriteString("text", text);
        }

        writer.WriteStartArray("pages");
        LayoutPage? last = null;
        foreach (LayoutExtent extent in node.Extents)
        {
            if (!ReferenceEquals(extent.Page, last))
            {
                writer.WriteNumberValue(extent.Page.PageIndex + 1);
                last = extent.Page;
            }
        }

        writer.WriteEndArray();
        if (options.IncludeBounds)
        {
            writer.WriteStartArray("bounds");
            foreach (LayoutExtent extent in node.Extents)
            {
                if (extent.Bounds is { } bounds)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(Math.Round(bounds.Left, 3));
                    writer.WriteNumberValue(Math.Round(bounds.Bottom, 3));
                    writer.WriteNumberValue(Math.Round(bounds.Right, 3));
                    writer.WriteNumberValue(Math.Round(bounds.Top, 3));
                    writer.WriteEndArray();
                }
                else
                {
                    writer.WriteNullValue();
                }
            }

            writer.WriteEndArray();
        }

        if (node.Children.Count > 0)
        {
            writer.WriteStartArray("children");
            foreach (LayoutNode child in node.Children)
            {
                Node(writer, child, options, isRoot: false);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}
