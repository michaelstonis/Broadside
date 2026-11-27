using System.Text;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Tests.Writing;

namespace Broadside.Tests.Forms;

/// <summary>Exporting field values as FDF (ISO 32000-2 §12.7.8): the file structure, the field hierarchy and the round trip of every field kind.</summary>
public sealed class FdfExportTests
{
    private static readonly PdfFormDataOptions Fdf = new PdfFormDataOptions().WithFormat(PdfFormDataFormat.Fdf);

    [Fact]
    public void An_exported_fdf_file_has_the_fdf_header_a_catalog_with_an_fdf_dictionary_and_the_field_hierarchy()
    {
        using PdfDocument document = FormDataFixture.CreateFilled();

        byte[] file = FormDataFixture.Export(document, new PdfFormDataOptions().WithFormat(PdfFormDataFormat.Fdf).WithSourceFile("form.pdf"));

        string text = Encoding.Latin1.GetString(file);
        Assert.StartsWith("%FDF-1.2\n", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        Assert.Contains("trailer\n<< /Size 2 /Root 1 0 R >>", text, StringComparison.Ordinal);
        CosDictionary fdf = ReadFdfDictionary(file);
        Assert.Equal("form.pdf", ((CosString)fdf[new CosName("F")]).DecodeText());
        Assert.True(fdf.ContainsKey(new CosName("ID")));
        var fields = (CosArray)fdf[new CosName("Fields")];
        Assert.Equal(["person", "check", "radio", "combo", "list"], fields.Cast<CosDictionary>().Select(field => ((CosString)field[new CosName("T")]).DecodeText()));
        var person = (CosDictionary)fields[0];
        var kids = (CosArray)person[new CosName("Kids")];
        Assert.Equal(["name", "address"], kids.Cast<CosDictionary>().Select(field => ((CosString)field[new CosName("T")]).DecodeText()));
        Assert.Equal("Ada Lovelace", ((CosString)((CosDictionary)kids[0])[new CosName("V")]).DecodeText());
        Assert.Equal(new CosName("Green"), ((CosDictionary)fields[2])[new CosName("V")]);
        Assert.Equal(["Cheese", "Peppers"], ((CosArray)((CosDictionary)fields[4])[new CosName("V")]).Cast<CosString>().Select(value => value.DecodeText()));
    }

    [Fact]
    public void Every_field_kind_round_trips_through_fdf_into_an_empty_copy_of_the_form()
    {
        using PdfDocument source = FormDataFixture.CreateFilled();
        using PdfDocument target = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(target, FormDataFixture.Export(source, Fdf));

        Assert.Equal(FormDataFixture.Values(source), FormDataFixture.Values(target));
        Assert.Equal(FormDataFixture.ValueFieldNames, import.FieldsSet.Select(field => field.FullyQualifiedName));
        Assert.Empty(import.FieldsNotFound);
        Assert.Empty(import.Diagnostics);
        Assert.NotNull(((PdfTextField)target.AcroForm!.FindField("person.name")!).Widgets[0].GetAppearance());
    }

    [Fact]
    public void Clearing_values_round_trips_because_empty_fields_are_exported()
    {
        using PdfDocument empty = FormDataFixture.Create();
        using PdfDocument target = FormDataFixture.CreateFilled();

        FormDataFixture.Import(target, FormDataFixture.Export(empty, Fdf));

        Assert.Equal(FormDataFixture.Values(empty), FormDataFixture.Values(target));
    }

    [Fact]
    public void The_field_names_option_exports_only_the_named_fields()
    {
        using PdfDocument document = FormDataFixture.CreateFilled();

        byte[] file = FormDataFixture.Export(document, new PdfFormDataOptions().WithFormat(PdfFormDataFormat.Fdf).WithFieldNames("person.address.city", "check"));

        var fields = (CosArray)ReadFdfDictionary(file)[new CosName("Fields")];
        Assert.Equal(["person", "check"], fields.Cast<CosDictionary>().Select(field => ((CosString)field[new CosName("T")]).DecodeText()));
        Assert.Equal("address", ((CosString)((CosDictionary)((CosArray)((CosDictionary)fields[0])[new CosName("Kids")])[0])[new CosName("T")]).DecodeText());
    }

    [Fact]
    public void A_rich_text_value_is_exported_as_rv_beside_the_plain_value()
    {
        using PdfDocument document = FormDataFixture.CreateFilled();
        var field = (PdfTextField)document.AcroForm!.FindField("person.name")!;
        field.Dictionary[new CosName("RV")] = new CosString("<body><p>Ada <b>Lovelace</b></p></body>"u8.ToArray());

        var person = (CosDictionary)((CosArray)ReadFdfDictionary(FormDataFixture.Export(document, Fdf))[new CosName("Fields")])[0];
        var name = (CosDictionary)((CosArray)person[new CosName("Kids")])[0];

        Assert.Equal("<body><p>Ada <b>Lovelace</b></p></body>", ((CosString)name[new CosName("RV")]).DecodeText());
    }

    [Fact]
    public void An_exported_fdf_file_passes_qpdf_check_once_wrapped_in_a_pdf()
    {
        using PdfDocument document = FormDataFixture.CreateFilled();
        document.Pages[0].AddTextAnnotation(new PdfRectangle(300, 700, 320, 720), "Note", new Broadside.Annotations.PdfAnnotationOptions { Name = "n1" });
        byte[] file = FormDataFixture.Export(document, new PdfFormDataOptions().WithFormat(PdfFormDataFormat.Fdf).WithAnnotations());

        (int exitCode, string output) = ExternalTool.Run("qpdf", Wrap(file), "--check");

        Assert.True(exitCode == 0, output);
        Assert.Contains("No syntax or stream encoding errors found", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// qpdf reads PDF files only: the FDF's header is renamed %PDF- (the same length, so every offset holds) and an incremental update
    /// adds a catalog with one page beside the FDF catalog, so --check verifies the FDF's objects, cross-reference table and trailer.
    /// </summary>
    private static byte[] Wrap(byte[] fdf)
    {
        string text = Encoding.Latin1.GetString(fdf);
        int startxref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        int sizeStart = text.LastIndexOf("/Size ", StringComparison.Ordinal) + 6;
        int size = int.Parse(text[sizeStart..text.IndexOf(' ', sizeStart)], System.Globalization.CultureInfo.InvariantCulture);
        var update = new StringBuilder();
        int[] offsets = new int[3];
        string[] bodies =
        [
            $"<< /Type /Catalog /Pages {size + 1} 0 R /FDFCatalog 1 0 R >>",
            $"<< /Type /Pages /Kids [{size + 2} 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent {size + 1} 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
        ];
        for (int index = 0; index < 3; index++)
        {
            offsets[index] = fdf.Length + update.Length;
            update.Append(System.Globalization.CultureInfo.InvariantCulture, $"{size + index} 0 obj\n{bodies[index]}\nendobj\n");
        }

        int table = fdf.Length + update.Length;
        update.Append(System.Globalization.CultureInfo.InvariantCulture, $"xref\n{size} 3\n");
        foreach (int offset in offsets)
        {
            update.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        update.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size {size + 3} /Root {size} 0 R /Prev {startxref} >>\nstartxref\n{table}\n%%EOF\n");
        return [.. "%PDF"u8, .. fdf.AsSpan(4), .. Encoding.Latin1.GetBytes(update.ToString())];
    }

    internal static CosDictionary ReadFdfDictionary(byte[] file)
    {
        string text = Encoding.Latin1.GetString(file);
        int start = text.IndexOf("1 0 obj\n", StringComparison.Ordinal) + "1 0 obj\n".Length;
        int end = text.IndexOf("\nendobj", start, StringComparison.Ordinal);
        var catalog = (CosDictionary)CosObject.Parse(file.AsSpan(start, end - start));
        return (CosDictionary)catalog[new CosName("FDF")];
    }
}
