using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Forms.FormData;
using Broadside.Parsing;

namespace Broadside;

/// <content>Form data exchange (issue #626): ISO 32000-2 §12.7.8 (FDF) and Adobe XFDF 3.0 (ISO 19444-1).</content>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Writes the document's field values, and with <see cref="PdfFormDataOptions.IncludeAnnotations"/> its markup annotations, to
    /// <paramref name="stream"/> as an FDF or XFDF file.
    /// </summary>
    /// <param name="stream">The writable stream the file is written to.</param>
    /// <param name="options">The format and what to include; <see langword="null"/> for an XFDF file of every field value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.7.8. The fields are the form's terminal fields that hold a value (text, choice, check box and radio button
    /// fields), without those flagged NoExport (Table 227), each fully qualified name once. FDF is written as a <c>%FDF-1.2</c>
    /// file (§12.7.8.2.2) whose FDF dictionary (Table 246) holds <c>Fields</c> (partial names in <c>T</c>, the hierarchy in
    /// <c>Kids</c>, values in <c>V</c>, rich text in <c>RV</c>), <c>Annots</c> (a copy of each markup annotation and its pop-up with
    /// its 0-based page in <c>Page</c>, Table 254, and <c>IRT</c>, <c>Popup</c> and <c>Parent</c> remapped inside the file),
    /// <c>F</c> (<see cref="PdfFormDataOptions.SourceFile"/>) and <c>ID</c> (the trailer's file identifier). An encrypted document's
    /// annotations are written without their appearance streams.
    /// </para>
    /// <para>
    /// XFDF is written as UTF-8 with the root <c>&lt;xfdf xmlns="http://ns.adobe.com/xfdf/"&gt;</c>: nested <c>&lt;field name&gt;</c>
    /// elements with one <c>&lt;value&gt;</c> per value (several for a multi-select choice) and <c>&lt;value-richtext&gt;</c> for a
    /// rich text value; one element per markup annotation (<c>&lt;highlight&gt;</c>, <c>&lt;text&gt;</c>, <c>&lt;ink&gt;</c> …) with
    /// its <c>page</c>, <c>rect</c>, <c>name</c>, <c>inreplyto</c>, <c>color</c>, <c>date</c>, <c>title</c> and <c>subject</c>,
    /// <c>&lt;contents&gt;</c> and <c>&lt;popup&gt;</c>. XFDF is specified by ISO 19444-1:2019; Broadside follows Adobe's public XFDF 3.0
    /// specification. File attachment, sound and redaction annotations have no XFDF element Broadside writes and are left out.
    /// </para>
    /// <para>An export is a read: it may run concurrently with other reads.</para>
    /// </remarks>
    public void SaveFormData(Stream stream, PdfFormDataOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(FormDataExporter.Export(this, options ?? new PdfFormDataOptions()));
    }

    /// <summary>
    /// Reads an FDF or XFDF file from <paramref name="stream"/> and applies it: field values and flags to the fields of the same fully
    /// qualified name, and annotations to their pages.
    /// </summary>
    /// <param name="stream">The readable stream the file is read from.</param>
    /// <param name="options">
    /// The format and what to import. <see langword="null"/> sniffs the format and imports both fields and annotations; otherwise
    /// <see cref="PdfFormDataOptions.Format"/> names the format and <see cref="PdfFormDataOptions.IncludeFields"/>,
    /// <see cref="PdfFormDataOptions.IncludeAnnotations"/> and <see cref="PdfFormDataOptions.FieldNames"/> choose what is applied.
    /// </param>
    /// <returns>What was set, added, replaced and skipped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The FDF file is encrypted (its trailer has an <c>Encrypt</c> entry).</exception>
    /// <exception cref="DiagnosticException">
    /// The file is neither FDF nor XFDF, holds no FDF catalog or is not well-formed XML; or, when the document was opened in strict
    /// mode, at the first deviation of the file or the first value that cannot be applied.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.7.8 and §12.7.6.4. The format is sniffed from the first 1024 bytes: <c>%FDF-</c> is FDF; <c>&lt;xfdf</c>, or a
    /// byte order mark followed by <c>&lt;</c>, is XFDF. An FDF file is read through the same structure reader and repairs as a PDF file
    /// (a file without a cross-reference table, which FDF allows, is indexed by scanning); each repair is recorded in
    /// <see cref="PdfFormDataImport.Diagnostics"/>. <c>Pages</c> (template pages), <c>JavaScript</c> and <c>EmbeddedFDFs</c> are not
    /// applied, with an information diagnostic.
    /// </para>
    /// <para>
    /// Each field entry sets every field <see cref="PdfAcroForm.FindFields(string)"/> finds for its name through the typed setters,
    /// which write new appearances (an FDF <c>AP</c> is not imported). Names no field has are listed in
    /// <see cref="PdfFormDataImport.FieldsNotFound"/>; a value of the wrong type, or an option or state the field does not have, is
    /// skipped with a <c>FormDataValueInvalid</c> warning. <c>Ff</c>, <c>SetFf</c> and <c>ClrFf</c> change the field flags and
    /// <c>F</c>, <c>SetF</c> and <c>ClrF</c> the widgets' annotation flags (Table 249). A form with an XFA resource has only its
    /// AcroForm fields set, with an <c>XfaFormPresent</c> information. Changing a field of a signed document changes what was signed;
    /// <see cref="ValidateSignatures"/> reports the modification.
    /// </para>
    /// <para>
    /// An annotation whose <c>NM</c> matches one on its page replaces it in place; others are added with <c>P</c> set
    /// (§12.7.8.3.4). Replies named by <c>NM</c> (XFDF <c>inreplyto</c>) are resolved once every annotation is in. An annotation
    /// without an appearance gets one generated, and the PDF version its entries need is recorded for the next save (§7.5.2).
    /// </para>
    /// <para>An import is a mutation: no read of the document may run concurrently with it.</para>
    /// </remarks>
    public PdfFormDataImport ImportFormData(Stream stream, PdfFormDataOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        var diagnostics = new DiagnosticSink(_diagnostics.IsStrict);
        PdfFormDataFormat? format = options?.Format ?? SniffFormDataFormat(bytes);
        if (format is null)
        {
            diagnostics.Fail(FormDiagnostics.FormDataInvalid, "The file is neither FDF (no %FDF- header in its first 1024 bytes) nor XFDF (no <xfdf element there).");
        }

        using FormDataSet set = format == PdfFormDataFormat.Fdf
            ? FdfReader.Read(bytes, Configuration, diagnostics)
            : XfdfReader.Read(bytes, diagnostics);
        return FormDataImporter.Import(this, set, options?.IncludeFields ?? true, options?.IncludeAnnotations ?? true, options?.FieldNames, diagnostics);
    }

    /// <summary>The format the first 1024 bytes show: <c>%FDF-</c> for FDF; <c>&lt;xfdf</c> or a byte order mark and <c>&lt;</c> for XFDF.</summary>
    internal static PdfFormDataFormat? SniffFormDataFormat(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> start = bytes[..Math.Min(bytes.Length, 1024)];
        if (start.IndexOf("%FDF-"u8) >= 0)
        {
            return PdfFormDataFormat.Fdf;
        }

        if (start.IndexOf("<xfdf"u8) >= 0
            || start.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF, (byte)'<'])
            || start.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF, 0x00, (byte)'<'])
            || start.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, (byte)'<', 0x00]))
        {
            return PdfFormDataFormat.Xfdf;
        }

        return null;
    }
}
