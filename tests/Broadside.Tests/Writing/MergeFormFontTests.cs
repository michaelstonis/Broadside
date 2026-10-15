using System.Text;
using System.Text.RegularExpressions;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Tests.Writing;

/// <summary>
/// Merged forms whose default resources give one font name to different fonts (issue #308): the copied fields keep drawing with
/// their own form's font. ISO 32000-2 §12.7.3, Table 224 (<c>DR</c>, <c>DA</c>) and §12.7.4.3 (variable text).
/// </summary>
public sealed partial class MergeFormFontTests
{
    private static readonly CosName DA = new("DA");
    private static readonly CosName DR = new("DR");
    private static readonly CosName Font = new("Font");
    private static readonly CosName BaseFont = new("BaseFont");

    [Fact]
    public void Merging_two_forms_whose_dr_name_helv_for_different_fonts_fills_each_field_with_its_own_form_s_font()
    {
        // Both forms' fields inherit the form-wide DA "/Helv 0 Tf 0 g"; /Helv is Helvetica in the first and Courier in the second.
        using PdfDocument first = PdfDocument.Open(FormDocument("first", "Helvetica", formDa: "/Helv 0 Tf 0 g", fieldDa: null));
        using PdfDocument second = PdfDocument.Open(FormDocument("second", "Courier", formDa: "/Helv 0 Tf 0 g", fieldDa: null));

        using PdfDocument merged = PdfDocument.Merge(first, second);
        ((PdfTextField)merged.AcroForm!.FindField("first")!).SetValue("Ada");
        ((PdfTextField)merged.AcroForm.FindField("second")!).SetValue("Grace");

        using PdfDocument reopened = PdfDocument.Open(Save(merged));
        Assert.Equal("Helvetica", AppearanceFont(reopened, "first"));
        Assert.Equal("Courier", AppearanceFont(reopened, "second"));
        Assert.Equal("Courier", DefaultAppearanceFont(reopened, "second"));
        Assert.Equal(["Courier", "Helvetica"], DefaultResourceFonts(reopened).Values.Order(StringComparer.Ordinal));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Appending_a_form_whose_field_da_names_a_colliding_f1_adds_the_font_under_a_free_name_and_rewrites_only_that_name()
    {
        // The source's DR also has an /F1_1, so the colliding /F1 takes the next free name; the field's DA is otherwise kept as written.
        using PdfDocument target = PdfDocument.Open(FormDocument("first", "Helvetica", formDa: "/F1 0 Tf 0 g", fieldDa: null, fontName: "F1"));
        using PdfDocument source = PdfDocument.Open(FormDocument(
            "second",
            "Times-Roman",
            formDa: "/F1 0 Tf 0 g",
            fieldDa: "0 0 1 rg /F1 9 Tf 2 Tz",
            fontName: "F1",
            edit: (document, form, _) => Fonts(document, form)[new CosName("F1_1")] = document.AddObject(StandardFont("Courier"))));

        target.AppendPages(source);
        ((PdfTextField)target.AcroForm!.FindField("second")!).SetValue("Grace");

        using PdfDocument reopened = PdfDocument.Open(Save(target));
        Assert.Equal("0 0 1 rg /F1_2 9 Tf 2 Tz", reopened.AcroForm!.FindField("second")!.DefaultAppearance);
        Assert.Equal("/F1 0 Tf 0 g", reopened.AcroForm.DefaultAppearance);
        Assert.Null(reopened.AcroForm.FindField("first")!.Dictionary.GetValueOrDefault(DA));
        Assert.Equal("Times-Roman", AppearanceFont(reopened, "second"));
        Assert.Equal(
            new Dictionary<string, string> { ["F1"] = "Helvetica", ["F1_1"] = "Courier", ["F1_2"] = "Times-Roman" },
            DefaultResourceFonts(reopened));
    }

    [Fact]
    public void Merging_forms_whose_dr_agree_adds_no_font_and_changes_no_da()
    {
        // Two files with equal Helvetica dictionaries under /Helv, and one file merged with a second copy of itself.
        using PdfDocument first = PdfDocument.Open(FormDocument("first", "Helvetica", formDa: "/Helv 0 Tf 0 g", fieldDa: "/Helv 10 Tf 0 g"));
        using PdfDocument second = PdfDocument.Open(FormDocument("second", "Helvetica", formDa: "/Helv 0 Tf 0 g", fieldDa: null));
        using PdfDocument again = PdfDocument.Open(FormDocument("second", "Helvetica", formDa: "/Helv 0 Tf 0 g", fieldDa: null));

        using PdfDocument merged = PdfDocument.Merge(first, second, again);

        Assert.Equal(new Dictionary<string, string> { ["Helv"] = "Helvetica" }, DefaultResourceFonts(merged));
        Assert.Equal("/Helv 0 Tf 0 g", merged.AcroForm!.DefaultAppearance);
        Assert.Equal("/Helv 10 Tf 0 g", merged.AcroForm.FindField("first")!.DefaultAppearance);
        Assert.Null(merged.AcroForm.FindField("second")!.Dictionary.GetValueOrDefault(DA));
        Assert.Null(merged.AcroForm.FindField("second_1")!.Dictionary.GetValueOrDefault(DA));
    }

    [Fact]
    public void A_copied_field_whose_own_dr_holds_the_colliding_name_keeps_it_and_its_widgets_das_are_rewritten_otherwise()
    {
        // The second form's field "group" has two widget kids: the first has its own DA, the second inherits the form's. Its
        // sibling "own" carries its own DR /Helv (Times-Roman), which readers search before the form's, so its DA keeps /Helv.
        using PdfDocument first = PdfDocument.Open(FormDocument("first", "Helvetica", formDa: "/Helv 0 Tf 0 g", fieldDa: null));
        using PdfDocument second = PdfDocument.Open(FormDocument("own", "Courier", formDa: "/Helv 0 Tf 0 g", fieldDa: "/Helv 8 Tf 0 g", edit: (document, form, field) =>
        {
            field[DR] = new CosDictionary { [Font] = new CosDictionary { [new CosName("Helv")] = document.AddObject(StandardFont("Times-Roman")) } };
            PdfPage page = document.Pages[0];
            var group = new CosDictionary { [new CosName("FT")] = new CosName("Tx"), [new CosName("T")] = new CosString("group"u8) };
            CosReference groupReference = document.AddObject(group);
            var kids = new CosArray();
            foreach (string? da in new[] { "/Helv 11 Tf 1 0 0 rg", null })
            {
                CosDictionary widget = Widget(page, 600);
                widget[new CosName("Parent")] = groupReference;
                if (da is not null)
                {
                    widget[DA] = new CosString(Encoding.ASCII.GetBytes(da));
                }

                CosReference widgetReference = document.AddObject(widget);
                kids.Add(widgetReference);
                ((CosArray)page.Dictionary[new CosName("Annots")]).Add(widgetReference);
            }

            group[new CosName("Kids")] = kids;
            ((CosArray)document.Resolve(form[new CosName("Fields")])).Add(groupReference);
        }));

        using PdfDocument merged = PdfDocument.Merge(first, second);
        ((PdfTextField)merged.AcroForm!.FindField("own")!).SetValue("Own");
        ((PdfTextField)merged.AcroForm.FindField("group")!).SetValue("Group");

        using PdfDocument reopened = PdfDocument.Open(Save(merged));
        PdfAcroForm form = reopened.AcroForm!;
        Assert.Equal("/Helv 8 Tf 0 g", form.FindField("own")!.DefaultAppearance);
        Assert.Equal("Times-Roman", AppearanceFont(reopened, "own"));
        var group = (PdfTextField)form.FindField("group")!;
        Assert.Equal("/Helv_1 0 Tf 0 g", group.DefaultAppearance);
        Assert.Equal("/Helv_1 11 Tf 1 0 0 rg", Encoding.Latin1.GetString(((CosString)group.Widgets[0].Dictionary[DA]).Bytes));
        Assert.Equal("Courier", AppearanceFont(reopened, "group"));
    }

    private static string AppearanceFont(PdfDocument document, string field)
    {
        PdfFormXObject appearance = ((PdfTextField)document.AcroForm!.FindField(field)!).Widgets[0].GetAppearance()!;
        string content = Encoding.Latin1.GetString(appearance.Decode().Span);
        string name = TfName().Match(content).Groups[1].Value;
        var fonts = (CosDictionary)document.Resolve(appearance.Resources![Font]);
        return BaseFontOf(document, fonts[new CosName(name)]);
    }

    private static string? DefaultAppearanceFont(PdfDocument document, string field) =>
        document.AcroForm!.FindField(field)!.ParsedDefaultAppearance?.Font is { } font
            ? BaseFontOf(document, font.Dictionary)
            : null;

    private static Dictionary<string, string> DefaultResourceFonts(PdfDocument document)
    {
        var resources = (CosDictionary)document.Resolve(document.AcroForm!.Dictionary[DR]);
        var fonts = (CosDictionary)document.Resolve(resources[Font]);
        return fonts.ToDictionary(entry => entry.Key.Value, entry => BaseFontOf(document, entry.Value), StringComparer.Ordinal);
    }

    private static string BaseFontOf(PdfDocument document, CosObject font) =>
        ((CosName)((CosDictionary)document.Resolve(font))[BaseFont]).Value;

    /// <summary>
    /// One page with one text field (merged with its widget) named <paramref name="name"/>; the form's <c>DR</c> names the
    /// non-embedded Standard 14 font <paramref name="baseFont"/> <c>/Helv</c>.
    /// </summary>
    private static byte[] FormDocument(string name, string baseFont, string? formDa, string? fieldDa, string fontName = "Helv", Action<PdfDocument, CosDictionary, CosDictionary>? edit = null)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        CosDictionary field = Widget(page, 700);
        field[new CosName("FT")] = new CosName("Tx");
        field[new CosName("T")] = new CosString(Encoding.ASCII.GetBytes(name));
        if (fieldDa is not null)
        {
            field[DA] = new CosString(Encoding.ASCII.GetBytes(fieldDa));
        }

        CosReference fieldReference = document.AddObject(field);
        page.Dictionary[new CosName("Annots")] = new CosArray([fieldReference]);
        CosReference font = document.AddObject(StandardFont(baseFont));
        var form = new CosDictionary
        {
            [new CosName("Fields")] = new CosArray([fieldReference]),
            [DR] = new CosDictionary { [Font] = new CosDictionary { [new CosName(fontName)] = font } },
        };
        if (formDa is not null)
        {
            form[DA] = new CosString(Encoding.ASCII.GetBytes(formDa));
        }

        document.Catalog[new CosName("AcroForm")] = document.AddObject(form);
        edit?.Invoke(document, form, field);
        return Save(document);
    }

    /// <summary>A printable widget annotation on <paramref name="page"/>, 200 by 20 points, its bottom at <paramref name="y"/>.</summary>
    private static CosDictionary Widget(PdfPage page, int y) => new()
    {
        [new CosName("Type")] = new CosName("Annot"),
        [new CosName("Subtype")] = new CosName("Widget"),
        [new CosName("Rect")] = new CosArray([new CosInteger(50), new CosInteger(y), new CosInteger(250), new CosInteger(y + 20)]),
        [new CosName("P")] = page.Reference!,
        [new CosName("F")] = new CosInteger(4),
    };

    private static CosDictionary Fonts(PdfDocument document, CosDictionary form) =>
        (CosDictionary)document.Resolve(((CosDictionary)document.Resolve(form[DR]))[Font]);

    private static CosDictionary StandardFont(string baseFont) => new()
    {
        [new CosName("Type")] = Font,
        [new CosName("Subtype")] = new CosName("Type1"),
        [BaseFont] = new CosName(baseFont),
        [new CosName("Encoding")] = new CosName("WinAnsiEncoding"),
    };

    private static byte[] Save(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions());
        return stream.ToArray();
    }

    [GeneratedRegex(@"/(\S+) [-\d.]+ Tf")]
    private static partial Regex TfName();
}
