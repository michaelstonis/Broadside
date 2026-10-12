using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Text;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>form-fill</c> target (issue #167): opens the input as a whole file and fills up to 32 text fields with a Latin value (cut
/// to the field's <c>MaxLen</c>), which generates their appearances from whatever <c>DA</c>, <c>DR</c>, <c>MK</c>, <c>BS</c>,
/// <c>Rect</c>, <c>Q</c> and flags the file holds; then runs every generated appearance through a text extractor, saves, and
/// reopens. Invariants: filling never throws (the value is one Helvetica can always show, so no font program is needed; the system
/// font resolver is off so runs are deterministic); each widget's normal appearance is afterwards a form XObject whose text is
/// at most the value's length; the saved file opens with the same page count. An encrypted input (which cannot be saved yet) stops
/// after filling.
/// </summary>
/// <remarks>ISO 32000-2 §12.7.4.3, §12.7.5.3, §12.5.5.</remarks>
internal static class FormFillTarget
{
    private const string Value = "Fill 42 été\nzwei";

    private static readonly PdfEngine Engine = new(new PdfOptions().UseSystemFontResolver(null));

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument? document = FuzzTargets.OpenOrNull(data, Engine);
        if (document?.AcroForm is not { } form)
        {
            return;
        }

        int pages = document.Pages.Count;
        foreach (PdfTextField field in form.TerminalFields.OfType<PdfTextField>().Take(32))
        {
            string value = field.MaxLength is int length && length >= 0 && length < Value.Length ? Value[..length] : Value;
            field.SetValue(value);
            foreach (PdfWidgetAnnotation widget in field.Widgets)
            {
                if (widget.GetAppearance() is not { } appearance)
                {
                    throw new InvalidOperationException("A filled widget has no normal appearance.");
                }

                var extractor = new TextExtractor();
                if (widget.ProcessAppearance(appearance, extractor) && extractor.Text.Length > value.Length)
                {
                    throw new InvalidOperationException($"The appearance shows {extractor.Text.Length} characters for a value of {value.Length}.");
                }
            }
        }

        if (document.IsEncrypted)
        {
            return;
        }

        using var output = new MemoryStream();
        document.Save(output);
        using PdfDocument saved = PdfDocument.Open(output.ToArray());
        if (saved.Pages.Count != pages)
        {
            throw new InvalidOperationException($"The saved file has {saved.Pages.Count} pages; the input had {pages}.");
        }

        _ = saved.AcroForm?.TerminalFields.OfType<PdfTextField>().Select(field => field.Value).Count();
    }
}
