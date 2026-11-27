using Broadside.Forms;
using Broadside.Objects;

namespace Broadside.Tests.Forms;

/// <summary>A one-page document with a field of every kind form data carries, for the FDF and XFDF tests (ISO 32000-2 §12.7.8).</summary>
internal static class FormDataFixture
{
    public static readonly string[] ValueFieldNames = ["person.name", "person.address.city", "check", "radio", "combo", "list"];

    /// <summary>Creates the form: text fields under a hierarchy, a check box, radio buttons, a combo box, a multi-select list box, a push button and a signature field.</summary>
    public static PdfDocument Create(PdfEngine? engine = null)
    {
        PdfDocument document = (engine ?? PdfEngine.Default).Create();
        PdfPage page = document.Pages[0];
        PdfAcroForm form = document.CreateAcroForm();
        form.AddTextField("person.name", page, new PdfRectangle(72, 700, 272, 724));
        form.AddTextField("person.address.city", page, new PdfRectangle(72, 670, 272, 694));
        form.AddCheckBox("check", page, new PdfRectangle(72, 640, 86, 654));
        form.AddRadioButtons(
            "radio",
            [
                new PdfRadioButton(page, new PdfRectangle(72, 610, 86, 624), "Red"),
                new PdfRadioButton(page, new PdfRectangle(92, 610, 106, 624), "Green"),
                new PdfRadioButton(page, new PdfRectangle(112, 610, 126, 624), "Blue"),
            ]);
        form.AddComboBox("combo", page, new PdfRectangle(72, 570, 272, 594), [new PdfChoiceOption("ca", "Canada"), new PdfChoiceOption("us", "United States")]);
        form.AddListBox(
            "list", page, new PdfRectangle(72, 480, 272, 550), [new PdfChoiceOption("Cheese"), new PdfChoiceOption("Olives"), new PdfChoiceOption("Peppers")], new PdfFieldOptions().WithFlags(PdfFieldFlags.MultiSelect));
        form.AddPushButton("button", page, new PdfRectangle(72, 430, 172, 460));
        form.AddSignatureField("signature", page, new PdfRectangle(72, 330, 272, 410));
        return document;
    }

    /// <summary>Creates the form with a value in every value field.</summary>
    public static PdfDocument CreateFilled()
    {
        PdfDocument document = Create();
        Fill(document);
        return document;
    }

    public static void Fill(PdfDocument document)
    {
        PdfAcroForm form = document.AcroForm!;
        ((PdfTextField)form.FindField("person.name")!).SetValue("Ada Lovelace");
        ((PdfTextField)form.FindField("person.address.city")!).SetValue("London (UK) & <Europe> été €");
        ((PdfCheckBoxField)form.FindField("check")!).SetChecked(true);
        ((PdfRadioButtonField)form.FindField("radio")!).SetValue(new CosName("Green"));
        ((PdfComboBoxField)form.FindField("combo")!).SetValue("us");
        ((PdfListBoxField)form.FindField("list")!).SetSelectedIndices([0, 2]);
    }

    /// <summary>The values of the value fields, as text, for comparing a document with another.</summary>
    public static string[] Values(PdfDocument document)
    {
        PdfAcroForm form = document.AcroForm!;
        return
        [
            ((PdfTextField)form.FindField("person.name")!).Value ?? "(null)",
            ((PdfTextField)form.FindField("person.address.city")!).Value ?? "(null)",
            ((PdfCheckBoxField)form.FindField("check")!).Value.Value,
            ((PdfRadioButtonField)form.FindField("radio")!).Value.Value,
            string.Join("|", ((PdfComboBoxField)form.FindField("combo")!).Values),
            string.Join("|", ((PdfListBoxField)form.FindField("list")!).SelectedIndices),
        ];
    }

    public static byte[] Export(PdfDocument document, PdfFormDataOptions? options = null)
    {
        using var stream = new MemoryStream();
        document.SaveFormData(stream, options);
        return stream.ToArray();
    }

    public static PdfFormDataImport Import(PdfDocument document, byte[] file, PdfFormDataOptions? options = null)
    {
        using var stream = new MemoryStream(file);
        return document.ImportFormData(stream, options);
    }

    public static PdfFormDataImport Import(PdfDocument document, string file, PdfFormDataOptions? options = null) =>
        Import(document, System.Text.Encoding.Latin1.GetBytes(file), options);
}
