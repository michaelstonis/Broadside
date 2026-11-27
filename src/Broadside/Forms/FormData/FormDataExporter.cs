using System.Buffers;
using System.Globalization;
using System.Text;
using Broadside.Annotations;
using Broadside.Objects;

namespace Broadside.Forms.FormData;

/// <summary>Writes a document's field values and markup annotations as an FDF or XFDF file.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7.8. Fields are the form's terminal fields (<see cref="PdfAcroForm.TerminalFields"/>) that hold a value an import
/// can set (text, choice, check box and radio button fields), without those flagged NoExport (§12.7.4.1, Table 227), once per fully
/// qualified name. Annotations are the markup annotations of every page (§12.5.6.2), with the pop-ups that belong to them.
/// </para>
/// <para>
/// FDF (§12.7.8.2, §12.7.8.3) is written with a <c>%FDF-1.2</c> header, a cross-reference table and a trailer whose <c>Root</c> is
/// the catalog; the FDF dictionary holds <c>F</c> (the source file the options name), <c>ID</c> (the document's file identifier),
/// <c>Fields</c> (the hierarchy of partial names in <c>T</c> and <c>Kids</c>, values in <c>V</c>, rich text in <c>RV</c>) and
/// <c>Annots</c> (a copy of each annotation dictionary without <c>P</c>, its page in <c>Page</c>, and <c>IRT</c>, <c>Popup</c> and
/// <c>Parent</c> remapped to the copies in the file). The appearance streams of an encrypted document are left out.
/// </para>
/// <para>
/// XFDF (Adobe XFDF 3.0, ISO 19444-1) is written as UTF-8 XML whose root is <c>&lt;xfdf xmlns="http://ns.adobe.com/xfdf/"&gt;</c>:
/// nested <c>&lt;field name&gt;</c> elements with one <c>&lt;value&gt;</c> per value and <c>&lt;value-richtext&gt;</c> for a rich
/// text value; an element per annotation subtype with its attributes and <c>&lt;contents&gt;</c>.
/// </para>
/// <para>An export is a read of the document.</para>
/// </remarks>
internal static class FormDataExporter
{
    private static readonly CosName Fdf = new("FDF");
    private static readonly CosName Annots = new("Annots");
    private static readonly CosName Page = new("Page");

    public static byte[] Export(PdfDocument document, PdfFormDataOptions options)
    {
        List<ExportedField> fields = options.IncludeFields ? CollectFields(document, options.FieldNames) : [];
        List<(int Page, PdfAnnotation Annotation)> annotations = options.IncludeAnnotations ? CollectAnnotations(document) : [];
        return options.Format == PdfFormDataFormat.Fdf
            ? WriteFdf(document, options, fields, annotations)
            : WriteXfdf(document, options, fields, annotations);
    }

    private static List<ExportedField> CollectFields(PdfDocument document, IReadOnlyList<string>? names)
    {
        var fields = new List<ExportedField>();
        if (document.AcroForm is not { } form)
        {
            return fields;
        }

        HashSet<string>? filter = names is null ? null : new HashSet<string>(names, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PdfTerminalField field in form.TerminalFields)
        {
            if ((filter is not null && !filter.Contains(field.FullyQualifiedName)) || field.IsNoExport || !seen.Add(field.FullyQualifiedName))
            {
                continue;
            }

            CosObject? value = field switch
            {
                PdfTextField text => Text(text.Value ?? string.Empty),
                PdfChoiceField choice => choice.Values switch
                {
                    [] => Text(string.Empty),
                    [var single] => Text(single),
                    var several => new CosArray(several.Select(Text)),
                },
                PdfToggleButtonField toggle => toggle.Value,
                _ => null,
            };
            if (value is null)
            {
                continue;
            }

            var path = new List<string>();
            for (PdfField? node = field; node is not null; node = node.Parent)
            {
                path.Insert(0, node.PartialName);
            }

            fields.Add(new ExportedField(path, value, field is PdfTextField ? field.RichValue : null));
        }

        return fields;
    }

    private static CosString Text(string value) => new(TextStringEncoder.Encode(value));

    private static List<(int Page, PdfAnnotation Annotation)> CollectAnnotations(PdfDocument document)
    {
        var annotations = new List<(int, PdfAnnotation)>();
        for (int index = 0; index < document.Pages.Count; index++)
        {
            foreach (PdfAnnotation annotation in document.Pages[index].Annotations)
            {
                if (annotation is PdfMarkupAnnotation || (annotation is PdfPopupAnnotation { Parent: PdfMarkupAnnotation }))
                {
                    annotations.Add((index, annotation));
                }
            }
        }

        return annotations;
    }

    private static byte[] WriteFdf(PdfDocument document, PdfFormDataOptions options, List<ExportedField> fields, List<(int Page, PdfAnnotation Annotation)> annotations)
    {
        var objects = new List<CosObject>();
        CosReference Add(CosObject value)
        {
            objects.Add(value);
            return new CosReference(objects.Count, 0);
        }

        var fdf = new CosDictionary();
        _ = Add(new CosDictionary { [Fdf] = fdf });
        if (options.SourceFile is { } file)
        {
            fdf[AnnotationNames.F] = Text(file);
        }

        if (document.Resolve(document.Trailer.TryGetValue(KnownNames.ID, out CosObject? id) ? id : null) is CosArray { Count: 2 } identifier
            && document.Resolve(identifier[0]) is CosString first && document.Resolve(identifier[1]) is CosString second)
        {
            fdf[KnownNames.ID] = new CosArray { first, second };
        }

        if (fields.Count > 0)
        {
            fdf[FormNames.Fields] = FdfFields(fields);
        }

        if (annotations.Count > 0)
        {
            var copier = new CosGraphCopier(document.Resolve, Add);
            var copies = new List<(int Page, PdfAnnotation Annotation, CosDictionary Copy, CosReference Reference)>();
            foreach ((int page, PdfAnnotation annotation) in annotations)
            {
                var copy = new CosDictionary();
                CosReference reference = Add(copy);
                if (annotation.Reference is { } source)
                {
                    copier.Assign(source, reference);
                }

                copies.Add((page, annotation, copy, reference));
            }

            bool dropAppearances = document.IsEncrypted;
            var array = new CosArray();
            foreach ((int page, PdfAnnotation annotation, CosDictionary copy, CosReference reference) in copies)
            {
                copier.CopyEntries(annotation.Dictionary, copy, key => key.Equals(AnnotationNames.P) || key.Equals(AnnotationNames.StructParent) || key.Equals(Page)
                    || (dropAppearances && key.Equals(AnnotationNames.AP)));
                copy[Page] = new CosInteger(page);
                array.Add(reference);
            }

            fdf[Annots] = array;
        }

        return Serialize(objects);
    }

    /// <summary>The field hierarchy as FDF field dictionaries: partial names in <c>T</c>, children in <c>Kids</c> (direct, Table 249).</summary>
    private static CosArray FdfFields(List<ExportedField> fields)
    {
        var roots = new CosArray();
        var nodes = new Dictionary<string, CosDictionary>(StringComparer.Ordinal);
        foreach (ExportedField field in fields)
        {
            CosArray siblings = roots;
            string key = string.Empty;
            CosDictionary? node = null;
            for (int level = 0; level < field.Path.Count; level++)
            {
                key = level == 0 ? field.Path[0] : $"{key}\u0000{field.Path[level]}";
                if (!nodes.TryGetValue(key, out node))
                {
                    node = new CosDictionary { [FormNames.T] = Text(field.Path[level]) };
                    nodes.Add(key, node);
                    siblings.Add(node);
                }

                if (level < field.Path.Count - 1)
                {
                    if (!node.TryGetValue(FormNames.Kids, out CosObject? kids))
                    {
                        kids = new CosArray();
                        node[FormNames.Kids] = kids;
                    }

                    siblings = (CosArray)kids;
                }
            }

            node![FormNames.V] = field.Value;
            if (field.RichValue is { } rich)
            {
                node[FormNames.RV] = Text(rich);
            }
        }

        return roots;
    }

    private static byte[] Serialize(List<CosObject> objects)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Ascii(buffer, "%FDF-1.2\n");
        buffer.Write((ReadOnlySpan<byte>)[(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
        var offsets = new long[objects.Count];
        for (int index = 0; index < objects.Count; index++)
        {
            offsets[index] = buffer.WrittenCount;
            Ascii(buffer, string.Create(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n"));
            objects[index].WriteTo(buffer);
            Ascii(buffer, "\nendobj\n");
        }

        long table = buffer.WrittenCount;
        var xref = new StringBuilder();
        xref.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            xref.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        xref.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{table}\n%%EOF\n");
        Ascii(buffer, xref.ToString());
        return buffer.WrittenSpan.ToArray();
    }

    private static void Ascii(ArrayBufferWriter<byte> buffer, string text) => buffer.Write(Encoding.ASCII.GetBytes(text));

    private static byte[] WriteXfdf(PdfDocument document, PdfFormDataOptions options, List<ExportedField> fields, List<(int Page, PdfAnnotation Annotation)> annotations)
    {
        var xml = new XfdfWriter();
        xml.Raw("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        xml.Start("xfdf");
        xml.Attribute("xmlns", XfdfReader.Namespace);
        xml.Attribute("xml:space", "preserve");
        if (options.SourceFile is { } file)
        {
            xml.Start("f");
            xml.Attribute("href", file);
            xml.End();
        }

        if (document.Resolve(document.Trailer.TryGetValue(KnownNames.ID, out CosObject? id) ? id : null) is CosArray { Count: 2 } identifier
            && document.Resolve(identifier[0]) is CosString first && document.Resolve(identifier[1]) is CosString second)
        {
            xml.Start("ids");
            xml.Attribute("original", Convert.ToHexString(first.Bytes));
            xml.Attribute("modified", Convert.ToHexString(second.Bytes));
            xml.End();
        }

        if (fields.Count > 0)
        {
            xml.Start("fields");
            WriteXfdfFields(xml, fields);
            xml.End();
        }

        var markup = annotations.Where(static item => item.Annotation is PdfMarkupAnnotation).ToList();
        if (markup.Count > 0)
        {
            xml.Start("annots");
            foreach ((int page, PdfAnnotation annotation) in markup)
            {
                XfdfAnnotationWriter.Write(xml, document, page, (PdfMarkupAnnotation)annotation);
            }

            xml.End();
        }

        xml.End();
        xml.Raw("\n");
        return Encoding.UTF8.GetBytes(xml.ToString());
    }

    private static void WriteXfdfFields(XfdfWriter xml, List<ExportedField> fields)
    {
        // Fields sharing a path prefix are written inside one element per partial name, in the order first seen.
        var open = new List<string>();
        foreach (ExportedField field in fields)
        {
            int common = 0;
            while (common < open.Count && common < field.Path.Count - 1 && open[common] == field.Path[common])
            {
                common++;
            }

            while (open.Count > common)
            {
                xml.End();
                open.RemoveAt(open.Count - 1);
            }

            for (int level = common; level < field.Path.Count - 1; level++)
            {
                xml.Start("field");
                xml.Attribute("name", field.Path[level]);
                open.Add(field.Path[level]);
            }

            xml.Start("field");
            xml.Attribute("name", field.Path[^1]);
            IEnumerable<CosObject> values = field.Value is CosArray array ? array : [field.Value];
            foreach (CosObject value in values)
            {
                xml.Start("value");
                xml.Text(value is CosName name ? name.Value : ((CosString)value).DecodeText());
                xml.End();
            }

            if (field.RichValue is { } rich && XfdfWriter.WellFormedContent(rich) is { } content)
            {
                xml.Start("value-richtext");
                xml.RawContent(content);
                xml.End();
            }

            xml.End();
        }

        while (open.Count > 0)
        {
            xml.End();
            open.RemoveAt(open.Count - 1);
        }
    }

    private sealed record ExportedField(IReadOnlyList<string> Path, CosObject Value, string? RichValue);
}
