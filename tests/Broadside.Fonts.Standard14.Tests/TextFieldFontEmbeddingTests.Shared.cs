using System.Text;
using System.Text.RegularExpressions;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Fields of one document that need the same embedded font share one subset of it, grown as fields need glyphs and written once
/// (issue #285). ISO 32000-2 §9.9.2 (subset fonts) and §12.7.4.3 (variable text).
/// </summary>
public sealed partial class TextFieldFontEmbeddingTests
{
    private const string Cyrillic = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";
    private const int FieldCount = 50;
    private const int ComboFlag = 1 << 17;

    [Fact]
    public void Fifty_cyrillic_fields_filled_in_one_document_share_one_embedded_subset()
    {
        using PdfDocument document = OpenWithCyrillicFields();
        for (int index = 0; index < FieldCount; index++)
        {
            CyrillicField(document, index).SetValue(CyrillicValue(index));
        }

        byte[] saved = SaveUndated(document, incremental: false);

        AssertEveryCyrillicFieldSharesOneSubset(saved, FieldCount);
    }

    [Fact]
    public void Fields_filled_after_an_incremental_save_grow_the_shared_subset_the_earlier_fields_show()
    {
        using PdfDocument document = OpenWithCyrillicFields();
        for (int index = 0; index < FieldCount / 2; index++)
        {
            CyrillicField(document, index).SetValue(CyrillicValue(index));
        }

        _ = SaveUndated(document, incremental: true);
        for (int index = FieldCount / 2; index < FieldCount; index++)
        {
            CyrillicField(document, index).SetValue(CyrillicValue(index));
        }

        byte[] saved = SaveUndated(document, incremental: true);

        AssertEveryCyrillicFieldSharesOneSubset(saved, FieldCount);
    }

    [Fact]
    public void Before_a_save_a_field_filled_after_another_shows_its_own_glyphs_through_the_shared_font()
    {
        using PdfDocument document = OpenWithCyrillicFields();
        PdfTextField first = CyrillicField(document, 0);
        first.SetValue("Привет");
        Assert.Equal("Привет", AppearanceText(first.Widgets[0]).Text);

        // Every letter here is new to the shared font, whose view the first extraction created.
        PdfTextField second = CyrillicField(document, 1);
        second.SetValue("Южный сбор");
        TextPage text = AppearanceText(second.Widgets[0]);
        Assert.Equal("Южный сбор", text.Text);
        AssertGlyphsHaveOutlines(text);
        Assert.Equal(Type0Font(document, first.Widgets[0]), Type0Font(document, second.Widgets[0]));

        // The widths read before the save are the ones the saved file has: the font's view follows the grown W array.
        using PdfDocument reopened = PdfDocument.Open(SaveUndated(document, incremental: false));
        TextPage saved = AppearanceText(CyrillicField(reopened, 1).Widgets[0]);
        Assert.Equal(saved.Glyphs.Select(glyph => glyph.Advance), text.Glyphs.Select(glyph => glyph.Advance));
    }

    [Fact]
    public void A_save_strict_mode_refuses_leaves_a_grown_shared_font_whole_and_the_next_save_subsets_it()
    {
        // acroform-fields.pdf is PDF 2.0: RC4 needs no raise, and its deprecation is what strict mode refuses (issue #300).
        using PdfDocument document = OpenWithCyrillicFields(new PdfOptions().UseStandard14Fonts().UseStrict());
        CyrillicField(document, 0).SetValue(CyrillicValue(0));
        CyrillicField(document, 1).SetValue("Южный сбор");
        string before = CosSnapshot.Of(document);
        using var refused = new MemoryStream();

        DiagnosticException thrown = Assert.Throws<DiagnosticException>(() => document.Save(
            refused,
            new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(PdfEncryptionAlgorithm.Rc4))));

        Assert.Equal("EncryptionDeprecated", thrown.Diagnostic.Code);
        Assert.Equal(0, refused.Length);
        Assert.Equal(before, CosSnapshot.Of(document));
        var font = (PdfType0Font)document.GetFont(Type0Font(document, CyrillicField(document, 1).Widgets[0]))!;
        Assert.True(font.DescendantFont!.Descriptor!.FontFile2!.EncodedData.Length > 40_000, "The pending font was subset by the refused save.");

        using PdfDocument reopened = PdfDocument.Open(SaveUndated(document, incremental: false));
        var saved = (PdfType0Font)reopened.GetFont(Type0Font(reopened, CyrillicField(reopened, 1).Widgets[0]))!;
        Assert.InRange(saved.DescendantFont!.Descriptor!.FontFile2!.EncodedData.Length, 1, 40_000);
    }

    [Fact]
    public void Combo_and_list_box_appearances_show_cyrillic_options_through_the_text_fields_shared_subset()
    {
        // Choice fields (issue #168) build their variable text through the same font choice as text fields, so they grow the same
        // shared subset rather than embedding one of their own.
        using PdfDocument document = OpenWithCyrillicFields();
        PdfTextField text = CyrillicField(document, 0);
        text.SetValue(CyrillicValue(0));
        PdfComboBoxField combo = AddCyrillicChoice<PdfComboBoxField>(document, "cyrillicCombo", ComboFlag, ["Южный", "Северный"]);
        PdfListBoxField list = AddCyrillicChoice<PdfListBoxField>(document, "cyrillicList", 0, ["Восток", "Запад", "Центр"]);

        combo.SetValue("Северный");
        list.SetSelectedIndices([1]);

        CosReference font = Type0Font(document, text.Widgets[0]);
        Assert.Equal(font, Type0Font(document, combo.Widgets[0]));
        Assert.Equal(font, Type0Font(document, list.Widgets[0]));

        byte[] saved = SaveUndated(document, incremental: false);
        using PdfDocument reopened = PdfDocument.Open(saved);
        var reopenedCombo = (PdfComboBoxField)reopened.AcroForm!.FindField("cyrillicCombo")!;
        var reopenedList = (PdfListBoxField)reopened.AcroForm!.FindField("cyrillicList")!;
        Assert.Equal("Северный", AppearanceText(reopenedCombo.Widgets[0]).Text);
        Assert.Contains("Запад", AppearanceText(reopenedList.Widgets[0]).Text, StringComparison.Ordinal);
        CosReference savedFont = Type0Font(reopened, CyrillicField(reopened, 0).Widgets[0]);
        Assert.Equal(savedFont, Type0Font(reopened, reopenedCombo.Widgets[0]));
        Assert.Equal(savedFont, Type0Font(reopened, reopenedList.Widgets[0]));
        var embedded = (PdfType0Font)reopened.GetFont(savedFont)!;
        Assert.InRange(embedded.DescendantFont!.Descriptor!.FontFile2!.EncodedData.Length, 1, 40_000);
        (int exitCode, string output) = ExternalTool.Run("pdffonts", saved);
        Assert.Equal(0, exitCode);
        Assert.Single(Regex.Matches(output, @"[A-Z]{6}\+LiberationSans\s+CID TrueType\s+Identity-H\s+yes\s+yes\s+yes"));
    }

    /// <summary>Adds a choice field (<c>Ch</c>, with <paramref name="flags"/>: <see cref="ComboFlag"/> for a combo box) of Cyrillic options to page 1, below the text fields.</summary>
    private static TField AddCyrillicChoice<TField>(PdfDocument document, string name, int flags, string[] options)
        where TField : PdfChoiceField
    {
        var acroForm = (CosDictionary)document.Resolve(document.Catalog[new CosName("AcroForm")]);
        var fields = (CosArray)document.Resolve(acroForm[new CosName("Fields")]);
        var annotations = (CosArray)document.Resolve(document.Pages[0].Dictionary[new CosName("Annots")]);
        bool comboBox = flags == ComboFlag;
        int bottom = comboBox ? 330 : 250;
        CosReference field = document.AddObject(new CosDictionary
        {
            [new CosName("Type")] = new CosName("Annot"),
            [new CosName("Subtype")] = new CosName("Widget"),
            [new CosName("FT")] = new CosName("Ch"),
            [new CosName("Ff")] = new CosInteger(flags),
            [new CosName("T")] = new CosString(Encoding.ASCII.GetBytes(name)),
            [new CosName("Opt")] = new CosArray([.. options.Select(option => (CosObject)new CosString(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(option)).ToArray()))]),
            [new CosName("Rect")] = new CosArray([new CosInteger(300), new CosInteger(bottom), new CosInteger(440), new CosInteger(bottom + (comboBox ? 14 : 60))]),
            [new CosName("F")] = new CosInteger(4),
            [new CosName("DA")] = new CosString("/Helv 0 Tf 0 g"u8),
        });
        fields.Add(field);
        annotations.Add(field);
        return (TField)document.AcroForm!.FindField(name)!;
    }

    /// <summary>The value of field <paramref name="index"/>: eight letters from a different place in the alphabet, so the font grows.</summary>
    private static string CyrillicValue(int index)
    {
        var value = new StringBuilder();
        for (int offset = 0; offset < 8; offset++)
        {
            value.Append(Cyrillic[((index * 3) + offset) % Cyrillic.Length]);
            if (offset == 3)
            {
                value.Append(' ');
            }
        }

        return value.ToString();
    }

    /// <summary>Opens acroform-fields.pdf with <see cref="FieldCount"/> more text fields in the free right half of page 1.</summary>
    private static PdfDocument OpenWithCyrillicFields(PdfOptions? options = null)
    {
        PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"), options ?? new PdfOptions().UseStandard14Fonts());
        var acroForm = (CosDictionary)document.Resolve(document.Catalog[new CosName("AcroForm")]);
        var fields = (CosArray)document.Resolve(acroForm[new CosName("Fields")]);
        PdfPage page = document.Pages[0];
        var annotations = (CosArray)document.Resolve(page.Dictionary[new CosName("Annots")]);
        for (int index = 0; index < FieldCount; index++)
        {
            double left = 300 + (index % 2 * 150);
            double bottom = 760 - (index / 2 * 16);
            CosReference field = document.AddObject(new CosDictionary
            {
                [new CosName("Type")] = new CosName("Annot"),
                [new CosName("Subtype")] = new CosName("Widget"),
                [new CosName("FT")] = new CosName("Tx"),
                [new CosName("T")] = new CosString(Encoding.ASCII.GetBytes(FieldName(index))),
                [new CosName("Rect")] = new CosArray([new CosInteger((long)left), new CosInteger((long)bottom), new CosInteger((long)left + 140), new CosInteger((long)bottom + 14)]),
                [new CosName("F")] = new CosInteger(4),
                [new CosName("DA")] = new CosString("/Helv 0 Tf 0 g"u8),
            });
            fields.Add(field);
            annotations.Add(field);
        }

        return document;
    }

    private static string FieldName(int index) => "cyrillic" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static PdfTextField CyrillicField(PdfDocument document, int index) => (PdfTextField)document.AcroForm!.FindField(FieldName(index))!;

    private static byte[] SaveUndated(PdfDocument document, bool incremental)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithUpdateModificationDate(false).WithIncrementalUpdate(incremental));
        return stream.ToArray();
    }

    /// <summary>
    /// Reopens <paramref name="saved"/> without the font package: every field reads and shows its value, every appearance names the
    /// same Type 0 font, the file embeds one subset, and qpdf and poppler accept it.
    /// </summary>
    private static void AssertEveryCyrillicFieldSharesOneSubset(byte[] saved, int count)
    {
        using PdfDocument reopened = PdfDocument.Open(saved);
        var fonts = new HashSet<CosReference>();
        for (int index = 0; index < count; index++)
        {
            PdfTextField field = CyrillicField(reopened, index);
            Assert.Equal(CyrillicValue(index), field.Value);
            TextPage text = AppearanceText(field.Widgets[0]);
            Assert.Equal(CyrillicValue(index), text.Text);
            AssertGlyphsHaveOutlines(text);
            fonts.Add(Type0Font(reopened, field.Widgets[0]));
        }

        Assert.Single(fonts);

        // Written as the subset of the glyphs used, not the whole 400 KB program the font held while it grew.
        var font = (PdfType0Font)reopened.GetFont(fonts.Single())!;
        Assert.InRange(font.DescendantFont!.Descriptor!.FontFile2!.EncodedData.Length, 1, 40_000);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);

        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        (exitCode, output) = ExternalTool.Run("pdffonts", saved);
        Assert.Equal(0, exitCode);
        Assert.Single(Regex.Matches(output, @"[A-Z]{6}\+LiberationSans\s+CID TrueType\s+Identity-H\s+yes\s+yes\s+yes"));

        for (int index = 0; index < count; index++)
        {
            Assert.True(DarkPixels(saved, CyrillicField(reopened, index).Widgets[0].Rect) > 10, FieldName(index));
        }
    }

    private static TextPage AppearanceText(PdfWidgetAnnotation widget)
    {
        var extractor = new TextExtractor();
        Assert.True(widget.ProcessAppearance(widget.GetAppearance()!, extractor));
        return extractor.ToTextPage();
    }

    private static void AssertGlyphsHaveOutlines(TextPage text)
    {
        var outline = new GlyphOutline();
        foreach (TextGlyph glyph in text.Glyphs)
        {
            if (text.GetGlyphText(glyph) is not " ")
            {
                FontProgram program = Assert.IsType<PdfType0Font>(glyph.Font).DescendantFont!.Program!;
                Assert.Equal(GlyphOutlineStatus.Complete, program.GetOutline((int)glyph.CharacterCode, outline));
                Assert.False(outline.IsEmpty);
            }
        }
    }

    /// <summary>The reference of the one Type 0 font the widget's normal appearance names.</summary>
    private static CosReference Type0Font(PdfDocument document, PdfWidgetAnnotation widget)
    {
        PdfFormXObject appearance = widget.GetAppearance()!;
        var fonts = (CosDictionary)document.Resolve(appearance.Resources![new CosName("Font")]);
        return Assert.Single(fonts.Values.OfType<CosReference>(), reference =>
            document.Resolve(reference) is CosDictionary font && font.TryGetValue(new CosName("Subtype"), out CosObject? subtype) && subtype.Equals(new CosName("Type0")));
    }
}
