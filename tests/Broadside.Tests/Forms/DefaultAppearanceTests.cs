using System.Text;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>The default appearance string (<c>DA</c>) parsed and its font resolved against the default resources (ISO 32000-2 §12.7.4.3).</summary>
public sealed class DefaultAppearanceTests
{
    [Fact]
    public void A_default_appearance_reads_its_font_its_size_and_its_gray_colour()
    {
        PdfDefaultAppearance appearance = PdfDefaultAppearance.Parse("/Helv 0 Tf 0 g"u8);

        Assert.Equal(new CosName("Helv"), appearance.FontName);
        Assert.Equal(0, appearance.FontSize);
        Assert.True(appearance.IsAutoSize);
        Assert.Equal(new PdfColor(PdfDeviceGrayColorSpace.Instance, [0f]), appearance.Color);
        Assert.Null(appearance.TextMatrix);
        Assert.True(appearance.OtherOperators.IsEmpty);
        Assert.Equal(PdfDefaultAppearanceProblems.None, appearance.Problems);
        Assert.True(appearance.IsValid);
        Assert.Null(appearance.Font);
        Assert.Null(appearance.FontResources);
    }

    [Fact]
    public void Rgb_and_cmyk_fill_colours_read_in_their_device_spaces_and_the_last_colour_wins()
    {
        PdfDefaultAppearance rgb = PdfDefaultAppearance.Parse("/HelveticaNeueLTStd-Bd 8.00 Tf 0.000 0.000 0.502 rg"u8);
        PdfDefaultAppearance cmyk = PdfDefaultAppearance.Parse("0 0 0 rg /F3 11 Tf 0.1 0.2 0.3 0.4 k"u8);

        Assert.Equal((new CosName("HelveticaNeueLTStd-Bd"), 8.0), (rgb.FontName, rgb.FontSize));
        Assert.Equal(new PdfColor(PdfDeviceRgbColorSpace.Instance, [0f, 0f, 0.502f]), rgb.Color);
        Assert.False(rgb.IsAutoSize);
        Assert.Equal(new PdfColor(PdfDeviceCmykColorSpace.Instance, [0.1f, 0.2f, 0.3f, 0.4f]), cmyk.Color);
        Assert.Equal(PdfDefaultAppearanceProblems.None, rgb.Problems | cmyk.Problems);
    }

    [Fact]
    public void Every_other_text_state_and_graphics_state_operator_is_kept_as_written_in_order()
    {
        PdfDefaultAppearance appearance = PdfDefaultAppearance.Parse("0 G 0 g 0 Tc 0 Tw 100 Tz\n16.2 TL 0 Ts 0 Tr /Cour 12 Tf [1 2] 0 d"u8);

        Assert.Equal((new CosName("Cour"), 12.0), (appearance.FontName, appearance.FontSize));
        Assert.Equal("0 G 0 Tc 0 Tw 100 Tz 16.2 TL 0 Ts 0 Tr [1 2] 0 d", Encoding.Latin1.GetString(appearance.OtherOperators.Span));
        Assert.Equal(PdfDefaultAppearanceProblems.None, appearance.Problems);
    }

    [Fact]
    public void A_text_matrix_is_read_apart_from_the_other_operators()
    {
        PdfDefaultAppearance appearance = PdfDefaultAppearance.Parse("/F1 12 Tf 1 0 0 1 -2.0 1.4040000000000001 Tm 0 g\n"u8);

        Assert.Equal(new Matrix(1, 0, 0, 1, -2, 1.4040000000000001), appearance.TextMatrix);
        Assert.True(appearance.OtherOperators.IsEmpty);
        Assert.Equal(PdfDefaultAppearanceProblems.None, appearance.Problems);
    }

    public static TheoryData<string, string?, double, string, PdfDefaultAppearanceProblems> Malformed => new()
    {
        // DA, font name read, size read, other operators kept, problems
        { "", null, 0, "", PdfDefaultAppearanceProblems.FontOperatorMissing },
        { "0 g", null, 0, "", PdfDefaultAppearanceProblems.FontOperatorMissing },
        { "/Helv Tf 0 g", null, 0, "", PdfDefaultAppearanceProblems.FontOperatorMissing | PdfDefaultAppearanceProblems.OperandsInvalid },
        { "12 /Helv Tf", null, 0, "", PdfDefaultAppearanceProblems.FontOperatorMissing | PdfDefaultAppearanceProblems.OperandsInvalid },
        { "//Helvetica 12 Tf 0 g", "Helvetica", 12, "", PdfDefaultAppearanceProblems.OperandsInvalid },
        { "/ 12.00000 Tf 0 g", "", 12, "", PdfDefaultAppearanceProblems.None },
        { "/Helv 12 Tf 0 0 rg", "Helv", 12, "", PdfDefaultAppearanceProblems.OperandsInvalid },
        { "/Helv 12 Tf 0 Tc 5", "Helv", 12, "0 Tc", PdfDefaultAppearanceProblems.OperandsInvalid },
        { "/Helv 12 Tf Tc 2 Tw", "Helv", 12, "2 Tw", PdfDefaultAppearanceProblems.OperandsInvalid },
        { "q /Helv 12 Tf 0 g Q", "Helv", 12, "", PdfDefaultAppearanceProblems.OperatorNotAllowed },
        { "BT /Helv 12 Tf 0 0 m 1 1 l S ET", "Helv", 12, "", PdfDefaultAppearanceProblems.OperatorNotAllowed },
        { "/Helv 12 Tf 0 g foo 1 Tc", "Helv", 12, "1 Tc", PdfDefaultAppearanceProblems.OperatorNotAllowed },
        { "/Helv 12 Tf 1 0 0 1 0 0 Tm 1 0 0 1 5 5 Tm", "Helv", 12, "", PdfDefaultAppearanceProblems.MultipleTextMatrices },
        { "/Helv 12 Tf ) 0 g", "Helv", 12, "", PdfDefaultAppearanceProblems.SyntaxInvalid },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void A_malformed_default_appearance_reads_what_it_can_and_names_its_problems(string text, string? font, double size, string other, PdfDefaultAppearanceProblems problems)
    {
        PdfDefaultAppearance appearance = PdfDefaultAppearance.Parse(Encoding.Latin1.GetBytes(text));

        Assert.Equal(font, appearance.FontName?.Value);
        Assert.Equal(size, appearance.FontSize);
        Assert.Equal(other, Encoding.Latin1.GetString(appearance.OtherOperators.Span));
        Assert.Equal(problems, appearance.Problems);
        Assert.Equal(problems == PdfDefaultAppearanceProblems.None, appearance.IsValid);
    }

    [Fact]
    public void The_last_of_several_matrices_and_fonts_wins()
    {
        PdfDefaultAppearance appearance = PdfDefaultAppearance.Parse("/ZaDb 5.6 Tf 0 g /ZaDb 9 Tf 1 g 1 0 0 1 0 0 Tm 2 0 0 2 5 5 Tm"u8);

        Assert.Equal((new CosName("ZaDb"), 9.0), (appearance.FontName, appearance.FontSize));
        Assert.Equal(new PdfColor(PdfDeviceGrayColorSpace.Instance, [1f]), appearance.Color);
        Assert.Equal(new Matrix(2, 0, 0, 2, 5, 5), appearance.TextMatrix);
    }

    [Fact]
    public void Every_field_of_the_corpus_form_resolves_its_font_in_the_form_default_resources()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;
        CosDictionary resources = form.DefaultResources!;

        var name = (PdfTextField)form.FindField("name")!;
        PdfDefaultAppearance appearance = name.ParsedDefaultAppearance!;
        Assert.Equal((new CosName("Helv"), 12.0), (appearance.FontName, appearance.FontSize));
        Assert.Equal(new PdfColor(PdfDeviceGrayColorSpace.Instance, [0f]), appearance.Color);
        Assert.Equal("Helvetica", appearance.Font!.BaseFont);
        Assert.Same(resources, appearance.FontResources);

        PdfDefaultAppearance first = form.FindField("person.first")!.ParsedDefaultAppearance!;
        Assert.Equal(new PdfColor(PdfDeviceRgbColorSpace.Instance, [0f, 0f, 1f]), first.Color);
        Assert.Equal((10.0, "Helvetica"), (first.FontSize, first.Font!.BaseFont));

        PdfDefaultAppearance country = form.FindField("country")!.ParsedDefaultAppearance!;
        Assert.True(country.IsAutoSize);
        Assert.Same(appearance.Font, country.Font);

        foreach (PdfField field in form.AllFields)
        {
            if (field.DefaultAppearance is not null)
            {
                PdfDefaultAppearance parsed = field.ParsedDefaultAppearance!;
                Assert.True(parsed.IsValid);
                Assert.NotNull(parsed.Font);
            }
        }

        Assert.Empty(document.Diagnostics);
    }

    private static byte[] FormWith(string fieldEntries, string formEntries = "/DR << /Font << /Helv 6 0 R >> >>") => new TestPdf().Build(
        $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R 5 0 R] {formEntries} >> >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
        $"<< /T (text) /FT /Tx {fieldEntries} >>",
        "<< /T (parent) /DR << /Font << /Own 7 0 R >> >> /Kids [8 0 R] >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
        "<< /T (child) /FT /Tx /Parent 5 0 R /DA (/Own 9 Tf 0 g) >>");

    [Fact]
    public void A_field_resolves_its_font_in_its_own_or_an_ancestor_default_resources_before_the_form()
    {
        using PdfDocument document = PdfDocument.Open(FormWith("/DA (/Helv 12 Tf 0 g)"));

        PdfDefaultAppearance child = document.AcroForm!.FindField("parent.child")!.ParsedDefaultAppearance!;

        Assert.Equal("Courier", child.Font!.BaseFont);
        Assert.Same(((CosDictionary)document.Resolve(new CosReference(5, 0)))[new CosName("DR")], child.FontResources);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_font_in_no_default_resources_reads_as_unresolved_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(FormWith("/DA (0.00000 0.00000 0.00000 rg\n/Invalid_font 18.00 Tf\n)"));

        PdfDefaultAppearance appearance = document.AcroForm!.FindField("text")!.ParsedDefaultAppearance!;

        Assert.Equal((new CosName("Invalid_font"), 18.0), (appearance.FontName, appearance.FontSize));
        Assert.True(appearance.IsValid);
        Assert.Null(appearance.Font);
        Assert.Null(appearance.FontResources);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("DefaultAppearanceFontMissing", new CosReference(4, 0)), (diagnostic.Code, diagnostic.ObjectReference));
    }

    [Fact]
    public void A_malformed_default_appearance_records_a_diagnostic_and_strict_mode_throws()
    {
        byte[] file = FormWith("/DA (q 0 g Q)");
        using (PdfDocument document = PdfDocument.Open(file))
        {
            PdfDefaultAppearance appearance = document.AcroForm!.FindField("text")!.ParsedDefaultAppearance!;

            Assert.Equal(PdfDefaultAppearanceProblems.FontOperatorMissing | PdfDefaultAppearanceProblems.OperatorNotAllowed, appearance.Problems);
            Assert.Equal(["DefaultAppearanceInvalid"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
        }

        using PdfDocument strict = PdfDocument.Open(file, new PdfOptions().UseStrict());
        PdfField field = strict.AcroForm!.FindField("text")!;
        Assert.Equal("DefaultAppearanceInvalid", Assert.Throws<DiagnosticException>(() => field.ParsedDefaultAppearance).Diagnostic.Code);
    }

    [Fact]
    public void A_field_without_any_default_appearance_has_none_to_parse()
    {
        using PdfDocument document = PdfDocument.Open(FormWith(string.Empty));

        Assert.Null(document.AcroForm!.FindField("text")!.ParsedDefaultAppearance);
        Assert.Equal(["DefaultAppearanceMissing"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }
}

/// <summary>Allocation proof for the default appearance tokenizer (ISO 32000-2 §12.7.4.3).</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DefaultAppearanceAllocationTests
{
    [Fact]
    public void Tokenizing_allocates_only_the_result_however_many_tokens_the_string_has()
    {
        byte[] plain = "/Helv 12 Tf 0 0 1 rg 2 Tc"u8.ToArray();
        byte[] noisy = Encoding.Latin1.GetBytes(
            "% a comment\n/F1 9 Tf 1 g 0.5 g /Zz 3 Tf " + string.Concat(Enumerable.Repeat("0 g  1 0 0 rg\r\n", 200)) + "/Helv 12 Tf 0 0 1 rg 2 Tc");

        long forPlain = Allocations.Measure(() => PdfDefaultAppearance.Parse(plain));
        long forNoisy = Allocations.Measure(() => PdfDefaultAppearance.Parse(noisy));

        Assert.Equal(forPlain, forNoisy);
        Assert.InRange(forPlain, 1, 512);
    }
}
