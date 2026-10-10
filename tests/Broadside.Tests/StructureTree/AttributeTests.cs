using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Tests.StructureTree;

/// <summary>Attribute objects, classes, revision numbers, typed owners and inheritance. ISO 32000-2 §14.7.6, §14.8.5.</summary>
public class AttributeTests
{
    private static readonly CosName Layout = new("Layout");

    [Fact]
    public void Layout_attributes_are_typed()
    {
        using PdfDocument document1 = Open(
            "<< /O /Layout /Placement /Block /WritingMode /TbRl /BackgroundColor [1 0 0] /BorderStyle [/Solid null /Dashed /None] " +
            "/BorderThickness 2 /Padding [1 2 3 4] /Color [0 0 1] /SpaceBefore 3 /SpaceAfter 4.5 /StartIndent 5 /EndIndent 6 /TextIndent 7 " +
            "/TextAlign /Justify /BBox [10 20 0 0] /Width /Auto /Height 50 /BlockAlign /Middle /InlineAlign /Center /TBorderStyle /Double " +
            "/TPadding [1 1 1 1] /BaselineShift -2 /LineHeight /Normal /TextPosition /Sup /TextDecorationColor [0 1 0] " +
            "/TextDecorationThickness 0.5 /TextDecorationType /Underline /RubyAlign /Center /RubyPosition /After " +
            "/GlyphOrientationVertical 90 /ColumnCount 2 /ColumnGap 12 /ColumnWidths [100 200] >>");
        PdfLayoutAttributes layout = Single<PdfLayoutAttributes>(document1);

        Assert.Equal("Block", layout.Placement);
        Assert.Equal("TbRl", layout.WritingMode);
        Assert.Equal([1.0, 0, 0], layout.BackgroundColor);
        Assert.Equal(["Solid", null, "Dashed", "None"], layout.BorderStyle);
        Assert.Equal([2.0, 2, 2, 2], layout.BorderThickness);
        Assert.Equal([1.0, 2, 3, 4], layout.Padding);
        Assert.Equal([0.0, 0, 1], layout.Color);
        Assert.Equal(3, layout.SpaceBefore);
        Assert.Equal(4.5, layout.SpaceAfter);
        Assert.Equal(5, layout.StartIndent);
        Assert.Equal(6, layout.EndIndent);
        Assert.Equal(7, layout.TextIndent);
        Assert.Equal("Justify", layout.TextAlign);
        Assert.Equal(new PdfRectangle(0, 0, 10, 20), layout.BoundingBox);
        Assert.Null(layout.Width);
        Assert.Equal(new CosName("Auto"), layout.GetValue(new CosName("Width")));
        Assert.Equal(50, layout.Height);
        Assert.Equal("Middle", layout.BlockAlign);
        Assert.Equal("Center", layout.InlineAlign);
        Assert.Equal(["Double", "Double", "Double", "Double"], layout.TableBorderStyle);
        Assert.Equal([1.0, 1, 1, 1], layout.TablePadding);
        Assert.Equal(-2, layout.BaselineShift);
        Assert.Null(layout.LineHeight);
        Assert.Equal("Sup", layout.TextPosition);
        Assert.Equal([0.0, 1, 0], layout.TextDecorationColor);
        Assert.Equal(0.5, layout.TextDecorationThickness);
        Assert.Equal("Underline", layout.TextDecorationType);
        Assert.Equal("Center", layout.RubyAlign);
        Assert.Equal("After", layout.RubyPosition);
        Assert.Equal(90, layout.GlyphOrientationVertical);
        Assert.Equal(2, layout.ColumnCount);
        Assert.Equal([12.0], layout.ColumnGap);
        Assert.Equal([100.0, 200], layout.ColumnWidths);
        Assert.Equal(Layout, layout.Owner);
    }

    [Fact]
    public void List_table_print_field_artifact_and_user_property_attributes_are_typed()
    {
        using PdfDocument document2 = Open("<< /O /List /ListNumbering /Disc /ContinuedList true /ContinuedFrom (list1) >>");
        PdfListAttributes list = Single<PdfListAttributes>(document2);
        Assert.Equal("Disc", list.ListNumbering);
        Assert.False(list.IsOrdered);
        Assert.True(list.ContinuedList);
        Assert.Equal("list1"u8.ToArray(), list.ContinuedFrom!.Bytes.ToArray());

        using PdfDocument document3 = Open("<< /O /Table /RowSpan 2 /ColSpan 3 /Headers [(a) (b)] /Scope /Both /Summary (S) /Short (Sh) >>");
        PdfTableAttributes table = Single<PdfTableAttributes>(document3);
        Assert.Equal(2, table.RowSpan);
        Assert.Equal(3, table.ColumnSpan);
        Assert.Equal(2, table.Headers!.Count);
        Assert.Equal("Both", table.Scope);
        Assert.Equal("S", table.Summary);
        Assert.Equal("Sh", table.ShortForm);

        using PdfDocument document4 = Open("<< /O /PrintField /Role /cb /checked /on /Desc (Agree) >>");
        PdfPrintFieldAttributes field = Single<PdfPrintFieldAttributes>(document4);
        Assert.Equal("cb", field.Role);
        Assert.Equal("on", field.Checked);
        Assert.Equal("Agree", field.Description);

        using PdfDocument document5 = Open("<< /O /Artifact /Type /Pagination /Subtype /Footer /BBox [0 0 612 40] >>");
        PdfArtifactAttributes artifact = Single<PdfArtifactAttributes>(document5);
        Assert.Equal("Pagination", artifact.ArtifactType);
        Assert.Equal("Footer", artifact.ArtifactSubtype);
        Assert.Equal(new PdfRectangle(0, 0, 612, 40), artifact.BoundingBox);

        using PdfDocument document6 = Open("<< /O /UserProperties /P [<< /N (Part) /V 42 /F (forty-two) /H true >> << /N (Bad) >> << /N (Color) /V /Red >>] >>");
        PdfUserPropertiesAttributes user = Single<PdfUserPropertiesAttributes>(document6);
        Assert.Collection(
            user.Properties,
            static property =>
            {
                Assert.Equal("Part", property.Name);
                Assert.Equal(new CosInteger(42), property.Value);
                Assert.Equal("forty-two", property.FormattedValue);
                Assert.True(property.IsHidden);
            },
            static property =>
            {
                Assert.Equal("Color", property.Name);
                Assert.False(property.IsHidden);
            });
    }

    [Fact]
    public void An_attribute_object_owned_by_a_namespace_or_another_owner_is_read_generically()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R] /Namespaces [6 0 R]",
            "<< /S /P /P 4 0 R /A [<< /O /NSO /NS 6 0 R /color (red) >> << /O /CSS-3 /color (blue) >>] >>",
            "<< /Type /Namespace /NS (urn:example:attributes) >>"));
        IReadOnlyList<PdfAttributeObject> attributes = Assert.Single(document.StructureTree!.Children).Attributes;

        Assert.Equal("urn:example:attributes", attributes[0].Namespace!.Name);
        Assert.Equal(typeof(PdfAttributeObject), attributes[1].GetType());
        Assert.Equal(new CosName("CSS-3"), attributes[1].Owner);
        Assert.Null(attributes[1].Namespace);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Revision_numbers_follow_the_object_or_class_they_belong_to_and_later_entries_win()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R] /ClassMap << /Wide [<< /O /Layout /Width 500 >> << /O /Layout /Width 600 >>] /Tall << /O /Layout /Height 9 >> >>",
            "<< /S /Table /P 4 0 R /R 3 /A [<< /O /Layout /TextAlign /End >> 2 << /O /Layout /TextAlign /Center >>] /C [/Wide 1 /Tall] >>"));
        PdfStructureElement table = Assert.Single(document.StructureTree!.Children);

        Assert.Equal([2, 0], table.Attributes.Select(static attributes => attributes.Revision));
        Assert.Equal(new CosName("Center"), table.GetAttributeValue(Layout, new CosName("TextAlign")));
        Assert.Equal([new CosName("Wide"), new CosName("Tall")], table.ClassNames);
        Assert.Equal([1, 1, 0], table.ClassAttributes.Select(static attributes => attributes.Revision));
        Assert.Equal(new CosInteger(600), table.GetAttributeValue(Layout, new CosName("Width")));
        Assert.Equal(new CosInteger(9), table.GetAttributeValue(Layout, new CosName("Height")));
        Assert.Equal(3, table.Revision);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_legacy_attribute_stream_is_read_through_its_dictionary()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R]",
            "<< /S /P /P 4 0 R /A 6 0 R >>",
            "<< /O /Layout /TextAlign /End /Length 0 >>\nstream\n\nendstream"));
        PdfAttributeObject attributes = Assert.Single(Assert.Single(document.StructureTree!.Children).Attributes);

        Assert.NotNull(attributes.Stream);
        Assert.Equal("End", Assert.IsType<PdfLayoutAttributes>(attributes).TextAlign);
        Assert.Equal(new CosReference(6, 0), attributes.Reference);
    }

    [Fact]
    public void Inheritable_attributes_pass_through_every_ancestor_and_others_stop_at_the_element()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R]",
            "<< /S /Sect /P 4 0 R /A << /O /Layout /TextAlign /Center /SpaceBefore 20 /WritingMode /RlTb >> /K [6 0 R] >>",
            "<< /S /Div /P 5 0 R /A << /O /Layout /WritingMode /LrTb >> /K [7 0 R] >>",
            "<< /S /P /P 6 0 R /A << /O /PrintField /Role /tv >> >>"));
        PdfStructureElement paragraph = document.StructureTree!.Elements[2];

        Assert.Equal(new CosName("Center"), paragraph.GetAttributeValue(Layout, new CosName("TextAlign")));
        Assert.Equal(new CosName("LrTb"), paragraph.GetAttributeValue(Layout, new CosName("WritingMode")));
        Assert.Equal(new CosInteger(0), paragraph.GetAttributeValue(Layout, new CosName("SpaceBefore")));
        Assert.Null(paragraph.GetAttributeValue(Layout, new CosName("Placement")));
        Assert.Equal(new CosName("off"), paragraph.GetAttributeValue(new CosName("PrintField"), new CosName("Checked")));
        Assert.Equal(new CosName("tv"), paragraph.GetAttributeValue(new CosName("PrintField"), new CosName("Role")));
        Assert.Null(document.StructureTree!.Elements[1].GetAttributeValue(new CosName("PrintField"), new CosName("Role")));
        Assert.Empty(document.Diagnostics);
    }

    private static PdfDocument Open(string attributeObject) => PdfDocument.Open(TaggedPdf.Build("/K [5 0 R]", $"<< /S /P /P 4 0 R /A {attributeObject} >>"));

    private static T Single<T>(PdfDocument document)
        where T : PdfAttributeObject
    {
        T attributes = Assert.IsType<T>(Assert.Single(Assert.Single(document.StructureTree!.Children).Attributes));
        Assert.Empty(document.Diagnostics);
        return attributes;
    }
}
