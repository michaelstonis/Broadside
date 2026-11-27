using System.Text;
using System.Text.Json;
using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>
/// The layout tree written as JSON (issue #629), checked against schema version 1 as <see cref="LayoutTree.SaveJson"/> documents it.
/// ISO 32000-2 §14.8.4, §14.8.5.4.3 (<c>BBox</c>), §14.9.3 (alternate descriptions).
/// </summary>
public sealed class LayoutJsonTests
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "document", "section", "paragraph", "heading", "list", "listItem", "label", "listBody", "table", "tableRow", "tableCell",
        "figure", "caption", "span", "link", "note",
    };

    // Schema 1: each property and the JSON type its value has.
    private static readonly Dictionary<string, JsonValueKind[]> Properties = new(StringComparer.Ordinal)
    {
        ["schema"] = [JsonValueKind.Number],
        ["kind"] = [JsonValueKind.String],
        ["structureType"] = [JsonValueKind.String],
        ["level"] = [JsonValueKind.Number],
        ["numbering"] = [JsonValueKind.String],
        ["rows"] = [JsonValueKind.Number],
        ["columns"] = [JsonValueKind.Number],
        ["previousPart"] = [JsonValueKind.True],
        ["nextPart"] = [JsonValueKind.True],
        ["row"] = [JsonValueKind.Number],
        ["column"] = [JsonValueKind.Number],
        ["rowSpan"] = [JsonValueKind.Number],
        ["columnSpan"] = [JsonValueKind.Number],
        ["header"] = [JsonValueKind.True, JsonValueKind.False],
        ["uri"] = [JsonValueKind.String],
        ["alt"] = [JsonValueKind.String],
        ["lang"] = [JsonValueKind.String],
        ["text"] = [JsonValueKind.String],
        ["pages"] = [JsonValueKind.Array],
        ["bounds"] = [JsonValueKind.Array],
        ["children"] = [JsonValueKind.Array],
    };

    public static TheoryData<string> Files => ["tagged-structure.pdf", "layout-tagged.pdf", "layout-headings-lists.pdf", "layout-tables.pdf", "layout-figures.pdf"];

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_node_matches_schema_version_1_and_mirrors_the_tree(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        LayoutTree tree = document.ExtractLayout();

        using JsonDocument json = Json(tree, new LayoutJsonOptions().WithBounds(true));

        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        Check(json.RootElement, tree.Root, isRoot: true, bounds: true);
    }

    [Fact]
    public void Kind_specific_properties_describe_headings_lists_tables_cells_figures_and_links()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tagged.pdf"));

        using JsonDocument json = Json(document.ExtractLayout(), null);

        JsonElement section = json.RootElement.GetProperty("children")[0];
        JsonElement heading = section.GetProperty("children")[0];
        Assert.Equal(("heading", 1, "Results", "H"), (heading.GetProperty("kind").GetString(), heading.GetProperty("level").GetInt32(), heading.GetProperty("text").GetString(), heading.GetProperty("structureType").GetString()));
        Assert.Equal([1], heading.GetProperty("pages").EnumerateArray().Select(static page => page.GetInt32()));
        Assert.False(heading.TryGetProperty("bounds", out _));
        JsonElement figure = Find(json.RootElement, "figure");
        Assert.Equal("A grey square", figure.GetProperty("alt").GetString());
        JsonElement table = Find(json.RootElement, "table");
        Assert.Equal((4, 3), (table.GetProperty("rows").GetInt32(), table.GetProperty("columns").GetInt32()));
        Assert.False(table.TryGetProperty("text", out _));
        JsonElement cell = table.GetProperty("children")[0].GetProperty("children")[0];
        Assert.Equal((0, 0, 2, 1, true, "Name"), (cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32(), cell.GetProperty("rowSpan").GetInt32(), cell.GetProperty("columnSpan").GetInt32(), cell.GetProperty("header").GetBoolean(), cell.GetProperty("text").GetString()));
        Assert.Equal("en-US", json.RootElement.GetProperty("lang").GetString());
        Assert.False(section.TryGetProperty("lang", out _));
    }

    [Fact]
    public void A_link_carries_its_uri_and_split_tables_are_flagged()
    {
        byte[] file = TaggedSample.Build(new Element("Document", new Element("P", new Run("See "), new Element("Link", new Run("site")) { LinkAction = "<< /S /URI /URI (https://example.com/) >>" })));
        using PdfDocument tagged = PdfDocument.Open(file);
        using PdfDocument tables = PdfDocument.Open(Corpus.Path("layout-tables.pdf"));

        using JsonDocument link = Json(tagged.ExtractLayout(), null);
        using JsonDocument split = Json(tables.ExtractLayout(), null);

        Assert.Equal("https://example.com/", Find(link.RootElement, "link").GetProperty("uri").GetString());
        JsonElement[] parts = [.. All(split.RootElement).Where(static node => node.GetProperty("kind").GetString() == "table")];
        Assert.True(parts[0].GetProperty("nextPart").GetBoolean());
        Assert.False(parts[0].TryGetProperty("previousPart", out _));
        Assert.True(parts[1].GetProperty("previousPart").GetBoolean());
    }

    [Fact]
    public void Indenting_changes_only_white_space_and_text_stays_unescaped()
    {
        byte[] file = TaggedSample.Build(new Element("Document", new Element("P", new Run("Café <b> & \"quoted\""))));
        using PdfDocument document = PdfDocument.Open(file);
        LayoutTree tree = document.ExtractLayout();

        string compact = Text(tree, null);
        string indented = Text(tree, new LayoutJsonOptions().WithIndented(true));

        Assert.DoesNotContain('\n', compact);
        Assert.Contains('\n', indented);
        Assert.Equal(compact, JsonSerializer.Serialize(JsonDocument.Parse(indented).RootElement, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Contains("Café <b> & \\\"quoted\\\"", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_tree_is_a_document_with_no_children()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        using JsonDocument json = Json(document.ExtractLayout(new LayoutOptions().WithDetection(LayoutDetection.None)), null);

        Assert.Equal("document", json.RootElement.GetProperty("kind").GetString());
        Assert.False(json.RootElement.TryGetProperty("children", out _));
    }

    [Fact]
    public void Options_are_immutable_copies_and_the_stream_stays_open()
    {
        var options = new LayoutJsonOptions();
        LayoutJsonOptions changed = options.WithBounds(true).WithIndented(true);
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tagged.pdf"));
        using var stream = new MemoryStream();

        document.ExtractLayout().SaveJson(stream, changed);

        Assert.Equal((false, false), (options.IncludeBounds, options.Indented));
        Assert.Equal((true, true), (changed.IncludeBounds, changed.Indented));
        Assert.True(stream.CanWrite);
        Assert.Throws<ArgumentNullException>(() => document.ExtractLayout().SaveJson(null!));
    }

    private static void Check(JsonElement json, LayoutNode node, bool isRoot, bool bounds)
    {
        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        foreach (JsonProperty property in json.EnumerateObject())
        {
            Assert.True(Properties.TryGetValue(property.Name, out JsonValueKind[]? kinds), $"unknown property {property.Name}");
            Assert.Contains(property.Value.ValueKind, kinds);
        }

        Assert.Equal(isRoot, json.TryGetProperty("schema", out _));
        string kind = json.GetProperty("kind").GetString()!;
        Assert.Contains(kind, Kinds);
        Assert.Equal(node.Kind.ToString(), kind, ignoreCase: true);
        Assert.Equal(node is LayoutHeading, json.TryGetProperty("level", out _));
        Assert.Equal(node is LayoutList, json.TryGetProperty("numbering", out _));
        Assert.Equal(node is LayoutTable, json.TryGetProperty("rows", out _));
        Assert.Equal(node is LayoutTableCell, json.TryGetProperty("rowSpan", out _));
        if (json.TryGetProperty("text", out JsonElement text))
        {
            Assert.Equal(node.Text, text.GetString());
        }

        int[] pages = [.. node.Extents.Select(static extent => extent.Page.PageIndex + 1).Distinct()];
        Assert.Equal(pages, json.GetProperty("pages").EnumerateArray().Select(static page => page.GetInt32()));
        if (bounds)
        {
            JsonElement[] boxes = [.. json.GetProperty("bounds").EnumerateArray()];
            Assert.Equal(node.Extents.Count, boxes.Length);
            Assert.All(boxes, static box => Assert.True(box.ValueKind == JsonValueKind.Null || box.GetArrayLength() == 4));
        }

        JsonElement[] children = json.TryGetProperty("children", out JsonElement array) ? [.. array.EnumerateArray()] : [];
        Assert.Equal(node.Children.Count, children.Length);
        for (int index = 0; index < children.Length; index++)
        {
            Check(children[index], node.Children[index], isRoot: false, bounds);
        }
    }

    private static JsonDocument Json(LayoutTree tree, LayoutJsonOptions? options) => JsonDocument.Parse(Text(tree, options));

    private static string Text(LayoutTree tree, LayoutJsonOptions? options)
    {
        using var stream = new MemoryStream();
        tree.SaveJson(stream, options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonElement Find(JsonElement root, string kind) => All(root).First(node => node.GetProperty("kind").GetString() == kind);

    private static IEnumerable<JsonElement> All(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("children", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                foreach (JsonElement descendant in All(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
