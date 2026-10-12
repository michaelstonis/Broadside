using System.Text;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Forms;

/// <summary>Filling text fields and generating their appearances (ISO 32000-2 §12.7.4.3 and §12.7.5.3), read back after a save.</summary>
public sealed class TextFieldFillTests
{
    private static readonly CosName Ff = new("Ff");
    private static readonly CosName MaxLen = new("MaxLen");
    private static readonly CosName DA = new("DA");
    private static readonly CosName Q = new("Q");
    private static readonly CosName MK = new("MK");

    [Fact]
    public void A_filled_text_field_saves_and_re_reads_its_value_and_an_appearance_that_shows_it()
    {
        byte[] saved = Fill("name", field => field.SetValue("Grace Hopper"));

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfTextField)reopened.AcroForm!.FindField("name")!;
        Assert.Equal("Grace Hopper", field.Value);
        Assert.Equal("Grace Hopper", AppearanceText(Assert.Single(field.Widgets)).Text);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
    }

    [Fact]
    public void Every_text_field_of_acroform_fields_pdf_filled_and_saved_re_reads_its_value_and_renders_in_poppler() =>
        FillEveryFieldSaveAndCheck(new PdfSaveOptions());

    [Fact]
    public void Every_text_field_of_acroform_fields_pdf_filled_and_saved_incrementally_re_reads_its_value_and_renders_in_poppler()
    {
        byte[] original = File.ReadAllBytes(Corpus.Path("acroform-fields.pdf"));

        byte[] filled = FillEveryFieldSaveAndCheck(new PdfSaveOptions().WithIncrementalUpdate(true));

        // An incremental update (§7.5.6) appends: the original file is the start of the saved one, byte for byte.
        Assert.True(filled.Length > original.Length);
        Assert.Equal(original, filled[..original.Length]);
        using PdfDocument reopened = PdfDocument.Open(filled);
        Assert.Equal(2, reopened.Revisions.Count);
    }

    [Fact]
    public void A_filled_text_field_saved_incrementally_twice_re_reads_the_second_value()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        PdfSaveOptions incremental = new PdfSaveOptions().WithIncrementalUpdate(true);
        ((PdfTextField)document.AcroForm!.FindField("name")!).SetValue("Ada Lovelace");
        byte[] first = Save(document, incremental);

        ((PdfTextField)document.AcroForm.FindField("name")!).SetValue("Grace Hopper");
        byte[] second = Save(document, incremental);

        Assert.Equal(first, second[..first.Length]);
        using PdfDocument reopened = PdfDocument.Open(second);
        var field = (PdfTextField)reopened.AcroForm!.FindField("name")!;
        Assert.Equal("Grace Hopper", field.Value);
        Assert.Equal("Grace Hopper", AppearanceText(Assert.Single(field.Widgets)).Text);
        (int exitCode, string output) = ExternalTool.Run("qpdf", second, "--check");
        Assert.True(exitCode == 0, output);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
    }

    [Fact]
    public void Each_widget_gets_its_own_appearance_and_the_shared_original_is_left_alone()
    {
        byte[] saved = Fill("name", field => field.SetValue("Grace"));

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfFormXObject appearance = ((PdfTextField)reopened.AcroForm!.FindField("name")!).Widgets[0].GetAppearance()!;
        Assert.NotEqual(new CosReference(7, 0), appearance.Reference);
        Assert.Equal(new PdfRectangle(0, 0, 200, 20), appearance.BoundingBox);
        Assert.Contains("/Tx BMC", Encoding.Latin1.GetString(appearance.Decode().Span), StringComparison.Ordinal);

        // person.first still shows the appearance it shared with the field just filled.
        var first = (PdfTextField)reopened.AcroForm.FindField("person.first")!;
        Assert.Equal(new CosReference(7, 0), first.Widgets[0].GetAppearance()!.Reference);
    }

    [Fact]
    public void A_comb_field_with_maxlen_6_spaces_six_characters_evenly()
    {
        byte[] saved = Fill("name", field =>
        {
            field.Dictionary[Ff] = new CosInteger((long)PdfFieldFlags.Comb);
            field.Dictionary[MaxLen] = new CosInteger(6);
            field.SetValue("ABC123");
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfWidgetAnnotation widget = ((PdfTextField)reopened.AcroForm!.FindField("name")!).Widgets[0];
        TextPage text = AppearanceText(widget);
        Assert.Equal("ABC123", text.Text);
        double cell = widget.Rect.Width / 6;
        for (int index = 0; index < 6; index++)
        {
            PdfRectangle bounds = text.Glyphs[index].Quad.Bounds;
            double centre = (bounds.Left + bounds.Right) / 2;
            Assert.Equal(widget.Rect.Left + (cell * (index + 0.5)), centre, 3);
        }
    }

    [Fact]
    public void A_value_longer_than_maxlen_is_refused_and_nothing_changes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        var field = (PdfTextField)document.AcroForm!.FindField("name")!;

        Assert.Throws<ArgumentException>(() => field.SetValue(new string('x', 41)));
        Assert.Equal("Ada", field.Value);
        Assert.Equal(new CosReference(7, 0), field.Widgets[0].GetAppearance()!.Reference);
    }

    [Fact]
    public void A_multi_line_field_wraps_its_text_between_words_inside_the_widget_in_a_compressed_appearance()
    {
        byte[] saved = Fill("name", field =>
        {
            field.Dictionary[Ff] = new CosInteger((long)PdfFieldFlags.Multiline);
            field.Dictionary.Remove(MaxLen);
            field.Dictionary[DA] = new CosString("/Helv 0 Tf 0 g"u8);
            field.Dictionary[new CosName("Rect")] = new CosArray([new CosInteger(50), new CosInteger(600), new CosInteger(150), new CosInteger(720)]);
            field.SetValue("The quick brown fox jumps over the lazy dog\nagain");
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfWidgetAnnotation widget = ((PdfTextField)reopened.AcroForm!.FindField("name")!).Widgets[0];
        TextPage text = AppearanceText(widget);
        Assert.Equal("Thequickbrownfoxjumpsoverthelazydogagain", text.Text.Replace(" ", string.Empty, StringComparison.Ordinal));
        double[] baselines = [.. text.Glyphs.Select(glyph => Math.Round(glyph.Origin.Y, 2)).Distinct()];
        Assert.True(baselines.Length >= 3, $"{baselines.Length} lines");
        Assert.Equal(baselines.OrderDescending(), baselines);
        Assert.All(text.Glyphs, glyph =>
        {
            Assert.InRange(glyph.Quad.Bounds.Right, widget.Rect.Left, widget.Rect.Right);
            Assert.InRange(glyph.Quad.Bounds.Bottom, widget.Rect.Bottom, widget.Rect.Top);
        });
        Assert.Equal(12, text.Glyphs[0].FontSize);

        // A new stream is compressed by the default save (issue #142) when that makes it smaller.
        Assert.Equal(new CosName("FlateDecode"), widget.GetAppearance()!.Stream.Dictionary[new CosName("Filter")]);
        Assert.Equal("again", string.Concat(text.Glyphs.Where(glyph => Math.Round(glyph.Origin.Y, 2) == baselines[^1]).Select(glyph => text.GetGlyphText(glyph).ToString())));
    }

    [Fact]
    public void An_auto_sized_single_line_shrinks_to_the_width_and_quadding_right_ends_it_at_the_right_padding()
    {
        byte[] saved = Fill("name", field =>
        {
            field.Dictionary[DA] = new CosString("/Helv 0 Tf 0 g"u8);
            field.Dictionary[Q] = new CosInteger(2);
            field.Dictionary.Remove(MaxLen);
            field.SetValue("A value much too long to fit at the height's size");
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfWidgetAnnotation widget = ((PdfTextField)reopened.AcroForm!.FindField("name")!).Widgets[0];
        TextPage text = AppearanceText(widget);
        Assert.InRange(text.Glyphs[0].FontSize, 4, 20 / 1.35);
        Assert.Equal(widget.Rect.Right - 1 - 2, text.Glyphs[^1].Origin.X + text.Glyphs[^1].Advance.X, 3);
        Assert.InRange(text.Glyphs[0].Origin.X, widget.Rect.Left + 3 - 0.001, widget.Rect.Right);
    }

    [Fact]
    public void A_password_field_stores_no_value_and_shows_one_asterisk_per_character()
    {
        byte[] saved = Fill("name", field =>
        {
            field.Dictionary[Ff] = new CosInteger((long)PdfFieldFlags.Password);
            field.SetValue("secret");
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfTextField)reopened.AcroForm!.FindField("name")!;
        Assert.Null(field.Value);
        Assert.Equal("******", AppearanceText(field.Widgets[0]).Text);
    }

    [Fact]
    public void The_colour_and_size_of_the_default_appearance_and_its_quadding_apply()
    {
        byte[] saved = Fill("person.first", field => field.SetValue("Grace"));

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfWidgetAnnotation widget = ((PdfTextField)reopened.AcroForm!.FindField("person.first")!).Widgets[0];
        TextPage text = AppearanceText(widget);
        Assert.All(text.Glyphs, glyph => Assert.Equal(10, glyph.FontSize));
        double left = text.Glyphs[0].Origin.X - widget.Rect.Left;
        double right = widget.Rect.Right - (text.Glyphs[^1].Origin.X + text.Glyphs[^1].Advance.X);
        Assert.Equal(left, right, 3);
        Assert.Contains("0 0 1 rg", Encoding.Latin1.GetString(widget.GetAppearance()!.Decode().Span), StringComparison.Ordinal);
    }

    [Fact]
    public void The_other_operators_of_the_default_appearance_are_kept_but_text_it_shows_or_positions_itself_is_not()
    {
        byte[] saved = Fill("name", field =>
        {
            field.Dictionary[DA] = new CosString("/Helv 12 Tf 0 g 2 Tc 5 5 Td (Extra) Tj 3 Tz"u8);
            field.SetValue("Grace");
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfWidgetAnnotation widget = ((PdfTextField)reopened.AcroForm!.FindField("name")!).Widgets[0];
        Assert.Equal("Grace", AppearanceText(widget).Text);
        string content = Encoding.Latin1.GetString(widget.GetAppearance()!.Decode().Span);
        Assert.Contains("/Helv 12 Tf\n0 g\n2 Tc 3 Tz\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Background_and_border_come_from_mk_and_poppler_draws_them()
    {
        byte[] saved = Fill("name", field =>
        {
            field.Dictionary[MK] = new CosDictionary
            {
                [new CosName("BG")] = new CosArray([new CosInteger(1), new CosInteger(1), new CosInteger(0)]),
                [new CosName("BC")] = new CosArray([new CosInteger(1), new CosInteger(0), new CosInteger(0)]),
            };
            field.Dictionary[new CosName("BS")] = new CosDictionary { [new CosName("W")] = new CosInteger(2) };
            field.SetValue("x");
        });

        (int exitCode, byte[] image, _) = ExternalTool.RunBinary("pdftoppm", saved, "-r", "72", "-f", "1", "-l", "1");
        Assert.Equal(0, exitCode);
        Pixmap page = Pixmap.Parse(image);

        // The rect is [50 700 250 720]: the border's 2 points are red, inside them is the yellow background.
        Assert.Equal((255, 0, 0), page.Rgb(51, 792 - 710));
        Assert.Equal((255, 255, 0), page.Rgb(200, 792 - 703));
    }

    [Fact]
    public void A_rotated_widget_runs_its_text_along_the_rotated_baseline_inside_the_rectangle()
    {
        byte[] saved = Fill("name", field =>
        {
            field.Dictionary[MK] = new CosDictionary { [new CosName("R")] = new CosInteger(90) };
            field.Dictionary[new CosName("Rect")] = new CosArray([new CosInteger(50), new CosInteger(500), new CosInteger(70), new CosInteger(700)]);
            field.SetValue("Up");
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfWidgetAnnotation widget = ((PdfTextField)reopened.AcroForm!.FindField("name")!).Widgets[0];
        PdfFormXObject appearance = widget.GetAppearance()!;
        Assert.Equal(new PdfRectangle(0, 0, 200, 20), appearance.BoundingBox);
        TextPage text = AppearanceText(widget);
        Assert.Equal("Up", text.Text);
        Quad quad = text.Glyphs[0].Quad;
        Assert.Equal(quad.BottomLeft.X, quad.BottomRight.X, 3);
        Assert.True(quad.BottomRight.Y > quad.BottomLeft.Y);
        Assert.All(text.Glyphs, glyph => Assert.InRange(glyph.Origin.X, 50, 70));
        Assert.All(text.Glyphs, glyph => Assert.InRange(glyph.Origin.Y, 500, 700));
    }

    [Fact]
    public void A_character_no_font_can_show_is_refused_and_nothing_changes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"), new PdfOptions().UseSystemFontResolver(null));
        var field = (PdfTextField)document.AcroForm!.FindField("name")!;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => field.SetValue("Ωμέγα"));
        Assert.Contains("UseStandard14Fonts", error.Message, StringComparison.Ordinal);
        Assert.Equal("Ada", field.Value);
        Assert.Equal(new CosReference(7, 0), field.Widgets[0].GetAppearance()!.Reference);
    }

    [Fact]
    public void Filling_an_xfa_form_reports_the_xfa_diagnostic_and_fills_only_the_acroform_value()
    {
        byte[] saved;
        using (PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-xfa.pdf")))
        {
            ((PdfTextField)document.AcroForm!.FindField("name")!).SetValue("Grace");
            Diagnostic diagnostic = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "XfaDataNotUpdated");
            Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
            saved = Save(document);
        }

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfTextField)reopened.AcroForm!.FindField("name")!;
        Assert.Equal("Grace", field.Value);
        Assert.Equal("Grace", AppearanceText(field.Widgets[0]).Text);
        PdfXfaPacket datasets = Assert.Single(reopened.AcroForm.Xfa!.Packets, packet => packet.Name == "datasets");
        Assert.Contains("<name>Ada</name>", Encoding.UTF8.GetString(reopened.DecodeStream(datasets.Stream).Span), StringComparison.Ordinal);
        Assert.Contains(reopened.Diagnostics, diagnostic => diagnostic.Code == "XfaFormPresent");
    }

    [Fact]
    public void Generating_appearances_shows_the_current_value()
    {
        byte[] saved = Fill("person.last", field => field.GenerateAppearances());

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfTextField)reopened.AcroForm!.FindField("person.last")!;
        Assert.Equal("Löv", AppearanceText(field.Widgets[0]).Text);
    }

    internal static byte[] Save(PdfDocument document) => Save(document, new PdfSaveOptions());

    internal static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options);
        return stream.ToArray();
    }

    internal static TextPage AppearanceText(PdfWidgetAnnotation widget)
    {
        var extractor = new TextExtractor();
        Assert.True(widget.ProcessAppearance(widget.GetAppearance()!, extractor));
        return extractor.ToTextPage();
    }

    private static byte[] Fill(string name, Action<PdfTextField> fill)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        fill((PdfTextField)document.AcroForm!.FindField(name)!);
        return Save(document);
    }

    /// <summary>
    /// Fills every text field of acroform-fields.pdf, saves with <paramref name="options"/>, and checks the saved file re-reads each
    /// value with an appearance that shows it, poppler draws ink only where a value is, and qpdf finds nothing wrong. Returns the file.
    /// </summary>
    private static byte[] FillEveryFieldSaveAndCheck(PdfSaveOptions options)
    {
        Dictionary<string, string?> values = new() { ["name"] = "Ada Lovelace", ["person.first"] = "Grace", ["person.last"] = "Hopper" };
        byte[] filled = FillAll(values, options);
        byte[] blank = FillAll(values.ToDictionary(pair => pair.Key, _ => (string?)null), options);

        using PdfDocument reopened = PdfDocument.Open(filled);
        List<PdfTextField> fields = [.. reopened.AcroForm!.TerminalFields.OfType<PdfTextField>()];
        Assert.Equal(values.Keys.Order(), fields.Select(field => field.FullyQualifiedName).Order());
        foreach (PdfTextField field in fields)
        {
            Assert.Equal(values[field.FullyQualifiedName], field.Value);
            PdfWidgetAnnotation widget = Assert.Single(field.Widgets);
            Assert.Equal(values[field.FullyQualifiedName], AppearanceText(widget).Text);

            // Ink inside the widget with the value, none without: poppler draws the generated appearance.
            Assert.True(DarkPixels(filled, widget.Rect) > 10, field.FullyQualifiedName);
            Assert.Equal(0, DarkPixels(blank, widget.Rect));
        }

        (int exitCode, string output) = ExternalTool.Run("qpdf", filled, "--check");
        Assert.True(exitCode == 0, output);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        return filled;
    }

    private static byte[] FillAll(Dictionary<string, string?> values, PdfSaveOptions options)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        foreach ((string name, string? value) in values)
        {
            ((PdfTextField)document.AcroForm!.FindField(name)!).SetValue(value);
        }

        return Save(document, options);
    }

    /// <summary>Counts the pixels darker than mid-grey that poppler draws inside <paramref name="rect"/> on page 1 at 72 dpi.</summary>
    private static int DarkPixels(byte[] file, PdfRectangle rect)
    {
        (int exitCode, byte[] image, _) = ExternalTool.RunBinary("pdftoppm", file, "-r", "72", "-gray", "-f", "1", "-l", "1");
        Assert.Equal(0, exitCode);
        Pixmap page = Pixmap.Parse(image);
        int count = 0;
        for (int y = 792 - (int)rect.Top; y < 792 - (int)rect.Bottom; y++)
        {
            for (int x = (int)rect.Left; x < (int)rect.Right; x++)
            {
                count += page.Gray(x, y) < 128 ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>A binary PGM (P5) or PPM (P6) image as pdftoppm writes it.</summary>
    private sealed class Pixmap(int width, int channels, byte[] data, int offset)
    {
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

            string magic = Token();
            int width = int.Parse(Token(), System.Globalization.CultureInfo.InvariantCulture);
            _ = Token();
            _ = Token();
            return new Pixmap(width, magic == "P6" ? 3 : 1, image, position + 1);
        }

        public byte Gray(int x, int y) => data[offset + (((y * width) + x) * channels)];

        public (int R, int G, int B) Rgb(int x, int y)
        {
            int at = offset + (((y * width) + x) * channels);
            return (data[at], data[at + 1], data[at + 2]);
        }
    }
}
