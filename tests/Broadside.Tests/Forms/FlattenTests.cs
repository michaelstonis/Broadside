using System.Globalization;
using System.Text;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Forms;

/// <summary>
/// Flattening fields, and optionally every annotation, into page content (ISO 32000-2 §12.5.5): the appearances are placed with the
/// appearance matrix, the widgets and the form are removed, and poppler draws the flattened page as it drew the form.
/// </summary>
public sealed class FlattenTests
{
    private static readonly CosName F = new("F");
    private static readonly PdfRectangle NameRect = new(50, 700, 250, 720);

    [Fact]
    public void Flattening_acroform_fields_pdf_leaves_no_form_and_no_widgets_and_poppler_draws_the_same_pages()
    {
        byte[] original = File.ReadAllBytes(Corpus.Path("acroform-fields.pdf"));
        using PdfDocument document = PdfDocument.Open(original);

        Assert.True(document.Flatten());
        byte[] flattened = Save(document);

        using PdfDocument reopened = PdfDocument.Open(flattened);
        Assert.Null(reopened.AcroForm);
        Assert.All(reopened.Pages, page => Assert.DoesNotContain(page.Annotations, annotation => annotation.Kind == PdfAnnotationKind.Widget));
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        for (int page = 1; page <= 2; page++)
        {
            AssertSameRender(original, flattened, page);
        }

        // The pages show the fields: "Ada" in the name field, now drawn by the page's content.
        Assert.True(Render(flattened, 1).DarkPixels(NameRect) > 10);
        Assert.Equal("Ada", TextIn(reopened.Pages[0], NameRect));

        (int exitCode, string output) = ExternalTool.Run("qpdf", flattened, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void A_hidden_field_is_not_drawn_but_its_widget_is_removed_all_the_same()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        NameWidget(document).Dictionary[F] = new CosInteger((long)(PdfAnnotationFlags.Hidden | PdfAnnotationFlags.Print));

        Assert.True(document.Flatten());
        byte[] flattened = Save(document);

        Assert.Equal(0, Render(flattened, 1).DarkPixels(NameRect));
        using PdfDocument reopened = PdfDocument.Open(flattened);
        Assert.Equal(string.Empty, TextIn(reopened.Pages[0], NameRect));
        Assert.DoesNotContain(reopened.Pages[0].Annotations, annotation => annotation.Kind == PdfAnnotationKind.Widget);
    }

    [Theory]
    [InlineData(PdfFlattenIntent.View, PdfAnnotationFlags.Print | PdfAnnotationFlags.NoView, false)]
    [InlineData(PdfFlattenIntent.Print, PdfAnnotationFlags.Print | PdfAnnotationFlags.NoView, true)]
    [InlineData(PdfFlattenIntent.View, PdfAnnotationFlags.None, true)]
    [InlineData(PdfFlattenIntent.Print, PdfAnnotationFlags.None, false)]
    public void A_field_is_drawn_when_its_flags_show_it_for_the_intent(PdfFlattenIntent intent, PdfAnnotationFlags flags, bool drawn)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        NameWidget(document).Dictionary[F] = new CosInteger((long)flags);

        Assert.True(document.Flatten(new PdfFlattenOptions().WithIntent(intent)));

        using PdfDocument reopened = PdfDocument.Open(Save(document));
        Assert.Equal(drawn ? "Ada" : string.Empty, TextIn(reopened.Pages[0], NameRect));
    }

    [Fact]
    public void An_xfa_form_refuses_to_flatten_and_nothing_changes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-xfa.pdf"));

        var error = Assert.Throws<InvalidOperationException>(() => document.Flatten());

        Assert.Contains("XfaFormPresent", error.Message, StringComparison.Ordinal);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "XfaFormPresent");
        Assert.NotNull(document.AcroForm);
        Assert.False(document.Catalog.IsDirty);
    }

    [Fact]
    public void A_signed_form_flattens_with_a_warning_that_the_signatures_no_longer_verify()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        Sign(document);

        Assert.True(document.Flatten());

        Diagnostic warning = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "FlattenInvalidatesSignatures");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Null(document.AcroForm);
    }

    [Fact]
    public void In_strict_mode_a_signed_form_is_not_flattened_and_nothing_changes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"), new PdfOptions().UseStrict());
        Sign(document);

        Assert.Throws<DiagnosticException>(() => document.Flatten());

        Assert.NotNull(document.AcroForm);
        Assert.Equal(11, document.Pages[0].Annotations.Count);
        Assert.False(document.Pages[0].Dictionary.ContainsKey(KnownNames.Contents));
    }

    [Fact]
    public void Appearances_land_where_they_did_whatever_the_page_content_leaves_in_the_graphics_state()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));

        // An unmatched Q, a scale, and a q never restored: the appearances still start from the default graphics state.
        document.Pages[0].Dictionary[KnownNames.Contents] = document.AddObject(new CosStream(new CosDictionary(), "Q 2 0 0 2 0 0 cm q 0.5 0 0 3 10 10 cm BT\n"u8.ToArray()));

        // A rotated widget (MK R 90): its generated appearance has a Matrix, so Do applies part of AA itself.
        var name = (PdfTextField)document.AcroForm!.FindField("name")!;
        name.Dictionary[new CosName("MK")] = new CosDictionary { [new CosName("R")] = new CosInteger(90) };
        name.Dictionary[new CosName("Rect")] = new CosArray([new CosInteger(300), new CosInteger(500), new CosInteger(320), new CosInteger(700)]);
        name.SetValue("Up");
        var expected = new TextExtractor();
        Assert.True(name.Widgets[0].ProcessAppearance(name.Widgets[0].GetAppearance()!, expected));
        TextPage before = expected.ToTextPage();

        Assert.True(document.Flatten());

        using PdfDocument reopened = PdfDocument.Open(Save(document));
        TextPage after = reopened.Pages[0].ExtractText();
        TextGlyph[] up = [.. after.Glyphs.Where(glyph => glyph.Origin.X > 290 && glyph.Origin.X < 330)];
        Assert.Equal(before.Glyphs.Count, up.Length);
        for (int index = 0; index < up.Length; index++)
        {
            Assert.Equal(before.Glyphs[index].Origin.X, up[index].Origin.X, 3);
            Assert.Equal(before.Glyphs[index].Origin.Y, up[index].Origin.Y, 3);
            Assert.Equal(before.Glyphs[index].TextRenderingMatrix.B, up[index].TextRenderingMatrix.B, 3);
        }

        // The other fields keep their places too (person.first shows the appearance it shares with name in the corpus file).
        Assert.Equal("Ada", TextIn(reopened.Pages[0], new PdfRectangle(50, 670, 150, 690)));
    }

    [Fact]
    public void A_field_with_optional_content_stays_optional_after_flattening()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        CosReference group = document.AddObject(new CosDictionary { [KnownNames.Type] = new CosName("OCG"), [new CosName("Name")] = new CosString("Fields"u8) });
        var defaults = new CosDictionary { [new CosName("OFF")] = new CosArray([group]) };
        document.Catalog[new CosName("OCProperties")] = new CosDictionary { [new CosName("OCGs")] = new CosArray([group]), [new CosName("D")] = defaults };
        NameWidget(document).Dictionary[new CosName("OC")] = group;

        Assert.True(document.Flatten());
        byte[] flattened = Save(document);

        // Hidden in the default configuration, as the widget was; shown once the group is turned on.
        using (PdfDocument reopened = PdfDocument.Open(flattened))
        {
            Assert.Equal(string.Empty, TextIn(reopened.Pages[0], NameRect));
        }

        Assert.Equal(0, Render(flattened, 1).DarkPixels(NameRect));
        defaults.Remove(new CosName("OFF"));
        byte[] turnedOn = Save(document);
        using (PdfDocument reopened = PdfDocument.Open(turnedOn))
        {
            Assert.Equal("Ada", TextIn(reopened.Pages[0], NameRect));
        }

        Assert.True(Render(turnedOn, 1).DarkPixels(NameRect) > 10);
    }

    [Fact]
    public void A_field_with_an_opacity_is_composited_with_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        NameWidget(document).Dictionary[new CosName("CA")] = new CosReal(0.5);

        Assert.True(document.Flatten());
        Pixmap page = Render(Save(document), 1);

        // Black text at half opacity over white is mid-grey: nothing in the field is darker than about 50%.
        int darkest = 255;
        for (int y = 792 - 720; y < 792 - 700; y++)
        {
            for (int x = 50; x < 250; x++)
            {
                darkest = Math.Min(darkest, page.Rgb(x, y).G);
            }
        }

        Assert.InRange(darkest, 110, 150);
    }

    [Fact]
    public void Flattening_every_annotation_draws_each_appearance_in_its_state_and_removes_them_all()
    {
        byte[] original = File.ReadAllBytes(Corpus.Path("annotations-appearance.pdf"));
        using PdfDocument document = PdfDocument.Open(original);

        Assert.False(document.Flatten());
        Assert.False(document.Pages[0].Dictionary.IsDirty);

        Assert.True(document.Flatten(new PdfFlattenOptions().WithAnnotations(true)));
        byte[] flattened = Save(document);

        using PdfDocument reopened = PdfDocument.Open(flattened);
        Assert.Empty(reopened.Pages[0].Annotations);
        AssertSameRender(original, flattened, 1);
        Assert.True(Render(flattened, 1).DarkPixels(new PdfRectangle(0, 0, 612, 792)) > 100);
    }

    /// <summary>
    /// The page tree node's dictionary as the four <c>flatten</c> fuzz findings of issue #296 damaged it (seeds 1, 3, 5 and 6 of
    /// the 60 s smoke: <c>annotations-link.pdf</c>, <c>annotations-subtypes.pdf</c>, <c>actions-all.pdf</c>,
    /// <c>annotations-appearance.pdf</c>): each leaves it unterminated, so the repaired node also takes the entries of the page
    /// written after it, its <c>Annots</c> among them.
    /// </summary>
    public static TheoryData<string> DamagedPageTreeNodes => new()
    {
        "<< /Type /Pages /Kids [3 0 R] /Resources <",
        "<< /Type /Pages /Kids [3 0 R] /Coun0 R /AP <dobj",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 ><",
        "<< /Type /Pages /Kids [3 0 R] /C< /Type /Pages /",
    };

    [Theory]
    [MemberData(nameof(DamagedPageTreeNodes))]
    public void A_page_whose_tree_node_also_lists_annotations_keeps_none_after_flattening(string node)
    {
        using PdfDocument document = PdfDocument.Open(WithAnnotatedNode(node));
        Assert.Equal(3, document.Pages[0].Annotations.Count);

        Assert.True(document.Flatten(new PdfFlattenOptions().WithAnnotations(true)));

        Assert.Null(document.AcroForm);
        Assert.Empty(document.Pages[0].Annotations);
        using PdfDocument reopened = PdfDocument.Open(Save(document));
        Assert.Single(reopened.Pages);
        Assert.Empty(reopened.Pages[0].Annotations);
        Assert.Null(reopened.AcroForm);
    }

    [Fact]
    public void Flattening_only_the_fields_does_not_bring_back_the_annotations_of_a_page_tree_node()
    {
        // A node's Annots is not inheritable (Table 31) but is read as viewers read it, only when the page has none of its own.
        using PdfDocument document = PdfDocument.Open(WithAnnotatedNode("<< /Type /Pages /Kids [3 0 R] /Count 1 /Annots [4 0 R] >>", "7 0 R"));
        Assert.Equal(PdfAnnotationKind.Widget, Assert.Single(document.Pages[0].Annotations).Kind);

        Assert.True(document.Flatten());

        Assert.Empty(document.Pages[0].Annotations);
        using PdfDocument reopened = PdfDocument.Open(Save(document));
        Assert.Empty(reopened.Pages[0].Annotations);
    }

    [Fact]
    public void A_flattened_form_saves_incrementally_and_a_full_save_can_drop_the_widgets()
    {
        byte[] original = File.ReadAllBytes(Corpus.Path("acroform-fields.pdf"));
        using PdfDocument document = PdfDocument.Open(original);
        Assert.True(document.Flatten());

        byte[] incremental = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));
        Assert.Equal(original, incremental[..original.Length]);
        using (PdfDocument reopened = PdfDocument.Open(incremental))
        {
            Assert.Null(reopened.AcroForm);
            Assert.Equal("Ada", TextIn(reopened.Pages[0], NameRect));
        }

        byte[] full = Save(document, new PdfSaveOptions { RemoveUnreachableObjects = true });
        string text = Encoding.Latin1.GetString(full);
        Assert.DoesNotContain("/Widget", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/AcroForm", text, StringComparison.Ordinal);
        AssertSameRender(original, full, 1);
    }

    internal static byte[] Save(PdfDocument document) => Save(document, new PdfSaveOptions());

    internal static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options);
        return stream.ToArray();
    }

    /// <summary>
    /// Asserts poppler draws page <paramref name="page"/> (1-based) of both files alike at 72 dpi: no pixel differs by more than a
    /// quarter of the range in any channel, outside at most 0.1% of the pixels (anti-aliased edges placed a hair apart).
    /// </summary>
    private static void AssertSameRender(byte[] expected, byte[] actual, int page)
    {
        Pixmap before = Render(expected, page);
        Pixmap after = Render(actual, page);
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height, after.Height);
        int differing = 0;
        for (int y = 0; y < before.Height; y++)
        {
            for (int x = 0; x < before.Width; x++)
            {
                (int r, int g, int b) = before.Rgb(x, y);
                (int r2, int g2, int b2) = after.Rgb(x, y);
                differing += Math.Max(Math.Abs(r - r2), Math.Max(Math.Abs(g - g2), Math.Abs(b - b2))) > 64 ? 1 : 0;
            }
        }

        Assert.True(differing <= before.Width * before.Height / 1000, $"page {page}: {differing} pixels differ");
    }

    /// <summary>Gives the form's signature field a value, a signature dictionary (§12.8.1, Table 255), as signing would.</summary>
    private static void Sign(PdfDocument document)
    {
        var field = (PdfSignatureField)document.AcroForm!.FindField("sig")!;
        field.Dictionary[new CosName("V")] = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = new CosName("Sig"),
            [new CosName("Filter")] = new CosName("Adobe.PPKLite"),
            [new CosName("SubFilter")] = new CosName("adbe.pkcs7.detached"),
        });
        Assert.True(field.IsSigned);
    }

    /// <summary>
    /// A form with one page under <paramref name="node"/> (object 2): a link without an appearance, a square and a text field widget
    /// that share one appearance (unless <paramref name="annots"/> names others).
    /// </summary>
    private static byte[] WithAnnotatedNode(string node, string annots = "4 0 R 5 0 R 7 0 R") =>
        new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [7 0 R] >> >>",
            node,
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [{annots}] >>",
            "<< /Type /Annot /Subtype /Link /Rect [72 700 300 724] /Border [0 0 0] /A << /S /URI /URI (https://example.com/) >> >>",
            "<< /Type /Annot /Subtype /Square /Rect [72 600 172 650] /AP << /N 6 0 R >> >>",
            "<< /Type /XObject /Subtype /Form /BBox [0 0 100 50] /Resources << >> /Length 24 >>\nstream\n0 0 1 rg 0 0 100 50 re f\nendstream",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /P 3 0 R /Rect [72 500 172 550] /AP << /N 6 0 R >> >>");

    private static PdfAnnotation NameWidget(PdfDocument document) =>
        document.Pages[0].Annotations.Single(annotation => annotation.Kind == PdfAnnotationKind.Widget && annotation.Rect == NameRect);

    /// <summary>The text of the glyphs the page's content shows with their origin inside <paramref name="rect"/>.</summary>
    private static string TextIn(PdfPage page, PdfRectangle rect)
    {
        TextPage text = page.ExtractText();
        return string.Concat(text.Glyphs
            .Where(glyph => glyph.Origin.X >= rect.Left && glyph.Origin.X <= rect.Right && glyph.Origin.Y >= rect.Bottom && glyph.Origin.Y <= rect.Top)
            .Select(glyph => text.Text.Substring(glyph.TextStart, glyph.TextLength)));
    }

    internal static Pixmap Render(byte[] file, int page)
    {
        string number = page.ToString(CultureInfo.InvariantCulture);
        (int exitCode, byte[] image, string error) = ExternalTool.RunBinary("pdftoppm", file, "-r", "72", "-f", number, "-l", number);
        Assert.True(exitCode == 0, error);
        return Pixmap.Parse(image);
    }

    /// <summary>A binary PPM (P6) image as pdftoppm writes it.</summary>
    internal sealed class Pixmap(int width, int height, byte[] data, int offset)
    {
        public int Width => width;

        public int Height => height;

        public static Pixmap Parse(byte[] image)
        {
            int position = 0;
            string Token()
            {
                while (char.IsWhiteSpace((char)image[position]))
                {
                    position++;
                }

                int start = position;
                while (!char.IsWhiteSpace((char)image[position]))
                {
                    position++;
                }

                return Encoding.ASCII.GetString(image, start, position - start);
            }

            Assert.Equal("P6", Token());
            int width = int.Parse(Token(), CultureInfo.InvariantCulture);
            int height = int.Parse(Token(), CultureInfo.InvariantCulture);
            _ = Token();
            return new Pixmap(width, height, image, position + 1);
        }

        public (int R, int G, int B) Rgb(int x, int y)
        {
            int at = offset + (((y * width) + x) * 3);
            return (data[at], data[at + 1], data[at + 2]);
        }

        /// <summary>Counts the pixels inside <paramref name="rect"/> (default user space of a 792-point-high page) darker than mid-grey.</summary>
        public int DarkPixels(PdfRectangle rect)
        {
            int count = 0;
            for (int y = Math.Max(0, 792 - (int)rect.Top); y < Math.Min(height, 792 - (int)rect.Bottom); y++)
            {
                for (int x = Math.Max(0, (int)rect.Left); x < Math.Min(width, (int)rect.Right); x++)
                {
                    (int r, int g, int b) = Rgb(x, y);
                    count += (r + g + b) / 3 < 128 ? 1 : 0;
                }
            }

            return count;
        }
    }
}
