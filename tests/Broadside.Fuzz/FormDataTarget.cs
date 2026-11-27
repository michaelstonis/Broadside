using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>fdf-import</c> and <c>xfdf-import</c> targets (issue #626): the input is imported as an FDF or XFDF file, fields and
/// annotations, into a new one-page form with a field of every kind. A file that holds no FDF catalog or is not well-formed XML
/// (<see cref="DiagnosticException"/>) and an encrypted FDF file (<see cref="NotSupportedException"/>) are documented outcomes;
/// anything else that escapes is a finding. Invariants: every field the import reports set is a field of the form; every annotation
/// it reports added or replaced is on the first page; the document then saves and reopens; and its own export of what it now holds,
/// in both formats, imports again into a new form without an exception.
/// </summary>
/// <remarks>ISO 32000-2 §12.7.8; Adobe XFDF 3.0 (ISO 19444-1).</remarks>
internal static class FormDataTarget
{
    private static readonly PdfEngine Engine = new(new PdfOptions().WithMaxImagePixels(1 << 16).WithMaxDecodedLength(1 << 20));

    public static void Fdf(ReadOnlySpan<byte> data) => Import(data, PdfFormDataFormat.Fdf);

    public static void Xfdf(ReadOnlySpan<byte> data) => Import(data, PdfFormDataFormat.Xfdf);

    private static void Import(ReadOnlySpan<byte> data, PdfFormDataFormat format)
    {
        using PdfDocument document = CreateForm();
        PdfFormDataImport import;
        try
        {
            using var stream = new MemoryStream(data.ToArray());
            import = document.ImportFormData(stream, new PdfFormDataOptions().WithFormat(format).WithAnnotations());
        }
        catch (DiagnosticException)
        {
            return;
        }
        catch (NotSupportedException) when (format == PdfFormDataFormat.Fdf)
        {
            return;
        }

        IReadOnlyList<PdfField> fields = document.AcroForm!.AllFields;
        foreach (PdfField field in import.FieldsSet)
        {
            if (!fields.Any(candidate => ReferenceEquals(candidate.Dictionary, field.Dictionary)))
            {
                throw new InvalidOperationException($"The import reports {field.FullyQualifiedName} set, which is not a field of the form.");
            }
        }

        IReadOnlyList<PdfAnnotation> onPage = document.Pages[0].Annotations;
        foreach (PdfAnnotation annotation in import.AnnotationsAdded.Concat(import.AnnotationsReplaced))
        {
            if (!onPage.Any(candidate => ReferenceEquals(candidate.Dictionary, annotation.Dictionary)))
            {
                throw new InvalidOperationException("The import reports an annotation it did not put on the page.");
            }
        }

        using (var saved = new MemoryStream())
        {
            document.Save(saved);
            using PdfDocument reopened = Engine.Open(saved.ToArray());
            _ = reopened.Pages[0].Annotations.Count;
        }

        foreach (PdfFormDataFormat exported in (ReadOnlySpan<PdfFormDataFormat>)[PdfFormDataFormat.Fdf, PdfFormDataFormat.Xfdf])
        {
            using var file = new MemoryStream();
            document.SaveFormData(file, new PdfFormDataOptions().WithFormat(exported).WithAnnotations());
            file.Position = 0;
            using PdfDocument again = CreateForm();
            _ = again.ImportFormData(file, new PdfFormDataOptions().WithFormat(exported).WithAnnotations());
        }
    }

    /// <summary>A one-page form with the fields the corpus files name: text fields under a hierarchy, a check box, radio buttons, a combo box and a multi-select list box.</summary>
    private static PdfDocument CreateForm()
    {
        PdfDocument document = Engine.Create();
        PdfPage page = document.Pages[0];
        PdfAcroForm form = document.CreateAcroForm();
        form.AddTextField("person.name", page, new PdfRectangle(72, 700, 272, 724));
        form.AddTextField("person.address.city", page, new PdfRectangle(72, 670, 272, 694));
        form.AddCheckBox("check", page, new PdfRectangle(72, 640, 86, 654));
        form.AddRadioButtons(
            "radio",
            [
                new PdfRadioButton(page, new PdfRectangle(72, 610, 86, 624), "Red"),
                new PdfRadioButton(page, new PdfRectangle(92, 610, 106, 624), "Blue"),
            ]);
        form.AddComboBox("combo", page, new PdfRectangle(72, 570, 272, 594), [new PdfChoiceOption("ca", "Canada"), new PdfChoiceOption("us", "United States")]);
        form.AddListBox(
            "list", page, new PdfRectangle(72, 480, 272, 550), [new PdfChoiceOption("Cheese"), new PdfChoiceOption("Olives"), new PdfChoiceOption("Peppers")], new PdfFieldOptions().WithFlags(PdfFieldFlags.MultiSelect));
        _ = page.AddTextAnnotation(new PdfRectangle(300, 700, 320, 720), "Existing", new PdfAnnotationOptions { Name = "note-1" });
        return document;
    }
}
