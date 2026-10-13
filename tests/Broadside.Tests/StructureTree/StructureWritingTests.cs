using System.Text;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.StructureTree;

/// <summary>
/// Writing and editing structure trees (ISO 32000-2 §14.7, §14.8; ISO/TS 32005): trees built through the structure API, saved and
/// re-read through the structure-tree reader, edits visible without reopening, and the written parent tree checked by qpdf.
/// </summary>
public sealed class StructureWritingTests
{
    [Fact]
    public void A_tree_written_over_tagged_content_re_reads_with_the_same_elements_and_mcid_mapping()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        PdfStructureTreeRoot root = document.CreateStructureTree();
        PdfStructureElement body = root.AddElement(new PdfStructureType("Document", PdfStructureNamespace.Pdf20));
        PdfStructureElement heading = body.AddElement(new PdfStructureType("H1", PdfStructureNamespace.Pdf20));
        PdfStructureElement paragraph = body.AddElement(new PdfStructureType("P", PdfStructureNamespace.Pdf20));
        TagText(document.Pages[0], heading, "H1");
        TagText(document.Pages[1], paragraph, "P");

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfStructureTreeRoot read = Assert.IsType<PdfStructureTreeRoot>(reopened.StructureTree);

        Assert.Equal(
            """
            Document (2.0) -> Document (2.0)
              H1 (2.0) -> H1 (2.0)
                MCID 0 page 1
              P (2.0) -> P (2.0)
                MCID 0 page 2

            """.ReplaceLineEndings("\n"),
            StructureProjection.Of(reopened, read));
        Assert.Equal("H1", read.FindElement(reopened.Pages[0], 0)!.StructureType!.Value);
        Assert.Equal("P", read.FindElement(reopened.Pages[1], 0)!.StructureType!.Value);
        Assert.True(reopened.MarkInfo.Marked);
        TaggedPdf.ReadEverything(reopened);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void Edits_inside_tagged_structure_pdf_are_seen_by_the_reader_at_once_and_after_an_incremental_save()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;
        PdfPage page = document.Pages[0];
        TaggedPdf.ReadEverything(document);
        PdfStructureElement heading = root.FindElement(page, 0)!;
        PdfStructureElement paragraph = root.FindElement(page, 1)!;
        PdfStructureElement list = root.FindElement(page, 2)!.Parent!.Parent!;
        PdfStructureElement chapter = paragraph.Parent!;
        Assert.Equal(("H1", "P", "L"), (heading.StandardType!.Name, paragraph.StandardType!.Name, list.StandardType!.Name));

        root.MapRole(new CosName("Para"), new CosName("Code"));
        heading.SetStructureType(new PdfStructureType("H2", PdfStructureNamespace.Pdf20));
        heading.Id = new CosString("intro"u8);
        list.Remove();
        PdfStructureElement aside = chapter.AddElement(new PdfStructureType("Aside", PdfStructureNamespace.Pdf20));
        aside.AddMarkedContent(page, 2);
        aside.AddMarkedContent(page, 3);
        PdfMarkedContentReference allocated = aside.AddMarkedContent(page);

        Assert.Equal("Code", paragraph.StandardType!.Name);
        Assert.Equal("H2", root.FindElement(page, 0)!.StandardType!.Name);
        Assert.Same(heading.Dictionary, root.FindElementById("intro"u8)!.Dictionary);
        Assert.Equal("TH", root.FindElementById("h1"u8)!.StructureType!.Value);
        Assert.Equal(10, allocated.Mcid);
        Assert.Same(aside.Dictionary, root.FindElement(page, 2)!.Dictionary);
        Assert.Same(aside.Dictionary, root.FindElement(page, 10)!.Dictionary);
        Assert.Null(root.FindElement(page, 4));
        Assert.DoesNotContain(root.Elements, element => element.StructureType!.Value is "L" or "LI" or "Lbl" or "LBody");
        Assert.Equal(["H2", "Para", "Table", "Aside"], chapter.Children.OfType<PdfStructureElement>().Select(child => child.StructureType!.Value));
        Assert.Equal(chapter, root.Elements.Single(element => element.Equals(aside)).Parent);
        Assert.Empty(document.Diagnostics);

        byte[] file = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(
            """
            Document (2.0) -> Document (2.0)
              Chapter (https://example.com/broadside-corpus) -> Sect (2.0) T=Chapter 1
                H2 (2.0) -> H2 (2.0) ID=intro
                  A Layout SpaceAfter=12
                  C Centered
                  MCID 0 page 1
                Para (1.7) -> Code (1.7)
                  A Layout TextAlign=/Justify
                  MCID 1 page 1
                  OBJR Link page 1
                Table (2.0) -> Table (2.0) ID=tbl1
                  A Table Summary=(Names and values)
                  TR (2.0) -> TR (2.0)
                    TH (2.0) -> TH (2.0) ID=h1
                      A Table Scope=/Column
                      MCID 6 page 1
                    TH (2.0) -> TH (2.0)
                      MCID 7 page 1
                  TR (2.0) -> TR (2.0)
                    TD (2.0) -> TD (2.0)
                      A Table Headers=[(h1)]
                      MCID 8 page 1
                    TD (2.0) -> TD (2.0)
                      MCR MCID 9 page 1
                Aside (2.0) -> Aside (2.0)
                  MCID 2 page 1
                  MCID 3 page 1
                  MCID 10 page 1

            """.ReplaceLineEndings("\n"),
            StructureProjection.Of(reopened, reopened.StructureTree!));
        Assert.Equal(2, reopened.Revisions.Count);
        Assert.Equal("Aside", reopened.StructureTree!.FindElement(reopened.Pages[0], 3)!.StructureType!.Value);
        Assert.Null(reopened.StructureTree!.FindElement(reopened.Pages[0], 5));
        Assert.Equal("H2", reopened.StructureTree!.FindElementById("intro"u8)!.StructureType!.Value);
        TaggedPdf.ReadEverything(reopened);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void A_change_made_directly_to_the_cos_objects_is_seen_by_the_next_lookup()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;
        PdfPage page = document.Pages[0];
        TaggedPdf.ReadEverything(document);
        PdfStructureElement chapter = root.FindElement(page, 1)!.Parent!;
        Assert.Equal(28 - 10, root.Elements.Count);

        // The chapter's K loses the table (7 elements: Table, two TR, two TH, two TD); the root's RoleMap loses Para.
        var kids = (CosArray)chapter.Dictionary[new CosName("K")];
        kids.RemoveAt(kids.Count - 1);
        root.RoleMap!.Remove(new CosName("Para"));

        Assert.Equal(28 - 10 - 7, root.Elements.Count);
        Assert.DoesNotContain(root.Elements, element => element.StructureType!.Value is "Table" or "TR" or "TH" or "TD");
        Assert.Null(root.FindElement(page, 1)!.StandardType);
    }

    [Fact]
    public void Namespaced_elements_round_trip_with_their_namespaces_and_role_maps()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfStructureTreeRoot root = document.CreateStructureTree();
        PdfStructureNamespace custom = root.GetOrAddNamespace("https://example.com/book");
        custom.MapRole(new CosName("Chapter"), new PdfStructureType("Sect", PdfStructureNamespace.Pdf20));
        custom.MapRole(new CosName("Remark"), new PdfStructureType("Para", PdfStructureNamespace.Pdf17));
        root.MapRole(new CosName("Para"), new CosName("P"));

        PdfStructureElement body = root.AddElement(new PdfStructureType("Document", PdfStructureNamespace.Pdf20));
        PdfStructureElement chapter = body.AddElement(new PdfStructureType("Chapter", custom));
        chapter.AddElement(new PdfStructureType("Title", PdfStructureNamespace.Pdf20));
        chapter.AddElement(new PdfStructureType("Remark", custom));
        chapter.AddElement("Para");
        chapter.AddElement(new PdfStructureType("math", PdfStructureNamespace.MathML)).AddElement(new PdfStructureType("mi", PdfStructureNamespace.MathML));

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfStructureTreeRoot read = reopened.StructureTree!;

        Assert.Equal(
            """
            Document (2.0) -> Document (2.0)
              Chapter (https://example.com/book) -> Sect (2.0)
                Title (2.0) -> Title (2.0)
                Remark (https://example.com/book) -> P (1.7)
                Para (1.7) -> P (1.7)
                math (MathML) -> math (MathML)
                  mi (MathML) -> mi (MathML)

            """.ReplaceLineEndings("\n"),
            StructureProjection.Of(reopened, read));
        Assert.Equal(
            ["https://example.com/book", PdfStructureNamespace.Pdf20Name, PdfStructureNamespace.MathMLName],
            read.Namespaces.Select(ns => ns.Name));
        Assert.Equal(
            ["Remark (https://example.com/book)", "Para (http://iso.org/pdf/ssn)", "P (http://iso.org/pdf/ssn)"],
            read.Elements.Single(element => element.StructureType!.Value == "Remark").RoleMapping.Select(type => type.ToString()));
        Assert.Equal(new PdfVersion(2, 0), read.MinimumVersion);
        TaggedPdf.ReadEverything(reopened);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void Text_entries_attributes_classes_ids_and_object_and_form_content_items_round_trip()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        PdfPage page = document.Pages[0];
        PdfAnnotation square = page.Annotations[0];
        PdfFormXObject appearance = square.GetAppearance()!;
        PdfStructureTreeRoot root = document.CreateStructureTree();
        root.AddClassAttributes(new CosName("Boxed"), new CosName("Layout")).SetValue(new CosName("BorderStyle"), new CosName("Solid"));

        PdfStructureElement body = root.AddElement("Document");
        body.Language = "en-GB";
        PdfStructureElement figure = body.AddElement("Figure");
        figure.AlternateDescription = "A red square";
        figure.Title = "Figure 1";
        figure.Id = new CosString("fig1"u8);
        figure.AddClassName(new CosName("Boxed"));
        PdfAttributeObject layout = figure.AddAttributes(new CosName("Layout"));
        layout.SetValue(new CosName("Placement"), new CosName("Block"));
        layout.SetValue(new CosName("BBox"), new CosArray { new CosInteger(100), new CosInteger(100), new CosInteger(150), new CosInteger(200) });
        figure.AddObjectReference(Assert.IsType<CosReference>(square.Reference), page);
        PdfMarkedContentReference drawn = figure.AddMarkedContent(page, appearance);
        PdfStructureElement abbreviation = body.AddElement("Span");
        abbreviation.ActualText = "Broadside";
        abbreviation.Expansion = "Broadside PDF library";
        abbreviation.Language = "fr";
        abbreviation.AddMarkedContent(page, 0);

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfStructureTreeRoot read = reopened.StructureTree!;
        PdfPage readPage = reopened.Pages[0];

        PdfStructureElement readFigure = read.FindElementById("fig1"u8)!;
        Assert.Equal(("Figure", "A red square", "Figure 1", "en-GB"), (readFigure.StructureType!.Value, readFigure.AlternateDescription, readFigure.Title, readFigure.EffectiveLanguage));
        Assert.Equal(new CosName("Block"), readFigure.GetAttributeValue(new CosName("Layout"), new CosName("Placement")));
        Assert.Equal(new CosName("Solid"), readFigure.GetAttributeValue(new CosName("Layout"), new CosName("BorderStyle")));
        Assert.Equal([new CosName("Boxed")], readFigure.ClassNames);
        Assert.Equal(new PdfRectangle(100, 100, 150, 200), Assert.IsType<PdfLayoutAttributes>(Assert.Single(readFigure.Attributes)).BoundingBox);
        Assert.Equal(readFigure, read.FindElementForObject(readPage.Annotations[0].Reference!));
        CosStream readAppearance = readPage.Annotations[0].GetAppearance()!.Stream;
        Assert.Equal(readFigure, read.FindElement(readAppearance, drawn.Mcid));
        PdfMarkedContentReference formItem = Assert.Single(readFigure.Children.OfType<PdfMarkedContentReference>());
        Assert.Same(readAppearance, formItem.ContentStream);
        Assert.Same(readPage, formItem.Page);

        PdfStructureElement readAbbreviation = read.FindElement(readPage, 0)!;
        Assert.Equal(("Broadside", "Broadside PDF library", "fr"), (readAbbreviation.ActualText, readAbbreviation.Expansion, readAbbreviation.EffectiveLanguage));

        // Three parent-tree keys, in the order handed out: the annotation's StructParent, the appearance's StructParents, the page's.
        Assert.Equal([0, 1, 2], read.ParentTree!.Select(entry => entry.Key));
        Assert.Equal(3, read.ParentTreeNextKey);
        Assert.Equal(new PdfVersion(1, 5), read.MinimumVersion);
        TaggedPdf.ReadEverything(reopened);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void Removing_an_element_unlinks_its_objects_mcids_and_ids_from_the_parent_and_id_trees()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;
        PdfPage page = document.Pages[0];
        CosReference link = Assert.IsType<CosReference>(page.Annotations[0].Reference);
        PdfStructureElement paragraph = root.FindElementForObject(link)!;
        PdfStructureElement table = root.FindElementById("tbl1"u8)!;

        paragraph.Remove();
        table.Remove();

        var annotation = (CosDictionary)document.Resolve(link);
        Assert.False(annotation.ContainsKey(new CosName("StructParent")));
        Assert.False(root.ParentTree!.ContainsKey(1));
        Assert.Null(root.FindElementForObject(link));
        Assert.Null(root.FindElement(page, 1));
        Assert.All(Enumerable.Range(6, 4), mcid => Assert.Null(root.FindElement(page, mcid)));
        Assert.Null(root.FindElementById("tbl1"u8));
        Assert.Null(root.FindElementById("h1"u8));
        Assert.Empty(root.IdTree!);
        Assert.Equal(2, root.ParentTreeNextKey);
        Assert.Throws<InvalidOperationException>(paragraph.Remove);

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(["Document", "Chapter", "H1", "L", "LI", "Lbl", "LBody", "LI", "Lbl", "LBody"], reopened.StructureTree!.Elements.Select(element => element.StructureType!.Value));
        TaggedPdf.ReadEverything(reopened);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void Edits_that_would_break_the_tree_are_refused()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        using PdfDocument other = PdfDocument.Create();
        PdfStructureTreeRoot root = document.StructureTree!;
        PdfPage page = document.Pages[0];
        PdfStructureElement heading = root.FindElement(page, 0)!;
        PdfStructureNamespace foreign = other.CreateStructureTree().GetOrAddNamespace("https://example.com/other");

        Assert.Throws<InvalidOperationException>(document.CreateStructureTree);
        Assert.Throws<InvalidOperationException>(() => heading.AddMarkedContent(page, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => heading.AddMarkedContent(page, -1));
        Assert.Throws<InvalidOperationException>(() => heading.AddObjectReference(Assert.IsType<CosReference>(page.Annotations[0].Reference), page));
        Assert.Throws<InvalidOperationException>(() => heading.Id = new CosString("tbl1"u8));
        Assert.Throws<ArgumentException>(() => heading.AddElement(new PdfStructureType("Sect", foreign)));
        Assert.Throws<ArgumentException>(() => heading.AddElement(string.Empty));
        Assert.Throws<InvalidOperationException>(() => PdfStructureNamespace.Pdf20.MapRole(new CosName("X"), new PdfStructureType("P", PdfStructureNamespace.Pdf20)));
        Assert.Throws<ArgumentException>(() => heading.AddAttributes(new CosName("Layout")).SetValue(new CosName("O"), new CosName("List")));
    }

    /// <summary>Wraps every text object of <paramref name="page"/> in a marked-content sequence whose MCID <paramref name="element"/> allocates.</summary>
    private static void TagText(PdfPage page, PdfStructureElement element, string tag) =>
        Assert.True(page.RewriteContent(new TextTagger(page, element, tag)));

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Save(output, options ?? new PdfSaveOptions());
        return output.ToArray();
    }

    private static void AssertQpdfPasses(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    /// <summary>The #147 pattern: remember <c>BT</c>, wrap through <c>ET</c> in <c>/Tag &lt;&lt;/MCID n&gt;&gt; BDC ... EMC</c>.</summary>
    private sealed class TextTagger(PdfPage page, PdfStructureElement element, string tag) : ContentEditor
    {
        private int _begin;

        public override ContentEvents Events => ContentEvents.Text;

        public override void BeginText(ContentContext context) => _begin = CurrentOperator;

        public override void EndText(ContentContext context)
        {
            int mcid = element.AddMarkedContent(page).Mcid;
            Wrap(_begin, CurrentOperator, Encoding.ASCII.GetBytes($"/{tag} <</MCID {mcid}>> BDC"), "EMC"u8);
        }
    }
}
