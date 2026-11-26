using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms.FormData;

/// <summary>
/// Reads an FDF file through the same structure reader and repair paths as a PDF file: header, cross-reference information (optional
/// in FDF, rebuilt by scanning when absent or unusable), trailer, catalog and the FDF dictionary's fields and annotations.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7.8. The header is <c>%FDF-</c> (§12.7.8.2.2); the trailer's <c>Root</c> leads to a catalog whose <c>FDF</c>
/// entry is the FDF dictionary (§12.7.8.2.4, §12.7.8.3.1, Tables 244 to 246). A file without a cross-reference table is legal and
/// read by scanning without a diagnostic; every other repair is recorded. An <c>Encrypt</c> entry is refused.
/// </para>
/// <para>
/// Field dictionaries (§12.7.8.3.2, Table 249) are walked through <c>Kids</c>, direct or indirect, building fully qualified names
/// from <c>T</c>; text values are decoded with the FDF dictionary's <c>Encoding</c> (PDFDocEncoding, <c>utf_8</c> or <c>utf_16</c>).
/// <c>Pages</c> (template pages, §12.7.8.3.3), <c>JavaScript</c> and <c>EmbeddedFDFs</c> are preserved in the file but not
/// applied: a <see cref="FormDiagnostics.FormDataNotApplied"/> information records them. Annotation dictionaries (§12.7.8.3.4,
/// Table 254) are kept as the file holds them, each with the 0-based page its required <c>Page</c> names.
/// </para>
/// </remarks>
internal static class FdfReader
{
    private const int MaxFieldDepth = 64;

    private static readonly CosName Fdf = new("FDF");
    private static readonly CosName Annots = new("Annots");
    private static readonly CosName Page = new("Page");
    private static readonly CosName Encoding = new("Encoding");
    private static readonly CosName Pages = new("Pages");
    private static readonly CosName JavaScript = new("JavaScript");
    private static readonly CosName EmbeddedFdfs = new("EmbeddedFDFs");
    private static readonly CosName SetFf = new("SetFf");
    private static readonly CosName ClrFf = new("ClrFf");
    private static readonly CosName SetF = new("SetF");
    private static readonly CosName ClrF = new("ClrF");

    /// <summary>Reads the file.</summary>
    /// <param name="bytes">The FDF file.</param>
    /// <param name="configuration">The engine's configuration: filters and decoding limits.</param>
    /// <param name="diagnostics">Where repairs and skipped entries are recorded.</param>
    /// <returns>The fields and annotations the file holds.</returns>
    /// <exception cref="NotSupportedException">The file is encrypted.</exception>
    /// <exception cref="DiagnosticException">The file holds no FDF catalog, or (strict mode) it deviates from §12.7.8.</exception>
    public static FormDataSet Read(ReadOnlyMemory<byte> bytes, EngineConfiguration configuration, DiagnosticSink diagnostics)
    {
        PdfSource? source = PdfSource.FromMemory(bytes);
        try
        {
            FormDataSet set = Read(source, bytes, configuration, diagnostics);
            set.Source = source;
            source = null;
            return set;
        }
        finally
        {
            source?.Dispose();
        }
    }

    private static FormDataSet Read(PdfSource source, ReadOnlyMemory<byte> bytes, EngineConfiguration configuration, DiagnosticSink diagnostics)
    {
        FileHeader header = FileHeader.Locate(source, diagnostics, fdf: true);
        ObjectLoader? loader = null;
        var streams = new StreamDecoder(
            configuration.Filters,
            configuration.MaxDecodedLength,
            diagnostics,
            value => loader is not null ? loader.Resolve(value) : value is null or CosReference ? CosNull.Instance : value)
        {
            MaxImagePixels = configuration.MaxImagePixels,
        };
        var scan = new Lazy<FileScan>(() => FileScan.Run(source), LazyThreadSafetyMode.PublicationOnly);

        // §12.7.8.1: "The cross-reference table is optional". Without a startxref there is none to read, so the objects are indexed
        // by scanning, which is not a repair.
        bool hasTable = bytes.Span.LastIndexOf("startxref"u8) >= 0;
        CrossReference? crossReference = hasTable ? CrossReferenceReader.Read(source, header, streams, diagnostics, scan) : null;
        if (crossReference is not null)
        {
            loader = new ObjectLoader(source, header, crossReference, diagnostics, new ObjectLoaderHooks(), streams);
            if (!IsFdfCatalog(loader, loader.Resolve(crossReference.Trailer.TryGetValue(KnownNames.Root, out CosObject? root) ? root : null)))
            {
                diagnostics.Report(
                    FileStructureDiagnostics.RootMissing,
                    DiagnosticSeverity.Warning,
                    "The trailer has no Root entry that resolves to an FDF catalog (ISO 32000-2 §12.7.8.2.4, Table 244); the objects are indexed by scanning the file.");
                crossReference = null;
            }
        }

        if (crossReference is null)
        {
            crossReference = CrossReferenceReconstructor.Reconstruct(source, header, scan.Value, streams, diagnostics, reportMissingTrailer: true, IsFdfCatalog);
            if (crossReference is null)
            {
                diagnostics.Fail(FormDiagnostics.FormDataInvalid, "The file holds no FDF catalog, a dictionary with an FDF entry (ISO 32000-2 §12.7.8.3.1, Table 245): it is not an FDF file.");
            }

            loader = new ObjectLoader(source, header, crossReference, diagnostics, new ObjectLoaderHooks(), streams);
        }

        ObjectLoader objects = loader!;
        CosDictionary trailer = crossReference.Trailer;
        if (trailer.ContainsKey(KnownNames.Encrypt))
        {
            throw new NotSupportedException("The FDF file is encrypted (its trailer has an Encrypt entry); encrypted FDF files are not supported.");
        }

        trailer.TryGetValue(KnownNames.Root, out CosObject? catalogEntry);
        var catalog = (CosDictionary)objects.Resolve(catalogEntry);
        var fdf = (CosDictionary)objects.Resolve(catalog[Fdf]);
        var set = new FormDataSet(objects.Resolve);
        var walker = new FieldWalker(objects, streams, diagnostics, ReadEncoding(objects, fdf, diagnostics));
        if (fdf.ContainsKey(Pages) || fdf.ContainsKey(JavaScript) || fdf.ContainsKey(EmbeddedFdfs))
        {
            diagnostics.Report(
                FormDiagnostics.FormDataNotApplied,
                DiagnosticSeverity.Information,
                "The FDF dictionary has Pages, JavaScript or EmbeddedFDFs entries; they are preserved in the file but not applied (template instantiation and scripts are out of scope).");
        }

        if (fdf.TryGetValue(FormNames.Fields, out CosObject? fields))
        {
            if (objects.Resolve(fields) is CosArray roots)
            {
                walker.ReadFields(roots, set.Fields);
            }
            else
            {
                Invalid(diagnostics, "The FDF dictionary's Fields entry is not an array (Table 246); no field is imported.");
            }
        }

        if (fdf.TryGetValue(Annots, out CosObject? annots))
        {
            if (objects.Resolve(annots) is CosArray array)
            {
                ReadAnnotations(objects, array, set.Annotations, diagnostics);
            }
            else
            {
                Invalid(diagnostics, "The FDF dictionary's Annots entry is not an array (Table 246); no annotation is imported.");
            }
        }

        return set;
    }

    /// <summary>Whether <paramref name="value"/> is an FDF catalog: a dictionary whose <c>FDF</c> entry is a dictionary (Table 245).</summary>
    private static bool IsFdfCatalog(ObjectLoader loader, CosObject value) =>
        value is CosDictionary catalog && catalog.TryGetValue(Fdf, out CosObject? fdf) && loader.Resolve(fdf) is CosDictionary;

    private static TextEncoding ReadEncoding(ObjectLoader loader, CosDictionary fdf, DiagnosticSink diagnostics)
    {
        if (!fdf.TryGetValue(Encoding, out CosObject? entry))
        {
            return TextEncoding.PdfDoc;
        }

        switch (loader.Resolve(entry))
        {
            case CosName { Value: "utf_8" }:
                return TextEncoding.Utf8;
            case CosName { Value: "utf_16" }:
                return TextEncoding.Utf16;
            case CosName { Value: "PDFDocEncoding" }:
                return TextEncoding.PdfDoc;
            case CosName name:
                Invalid(diagnostics, $"The FDF dictionary's Encoding {name.Value} is not supported (Table 246 also names Shift_JIS, BigFive, GBK and UHC); strings are read as PDFDocEncoding.");
                return TextEncoding.PdfDoc;
            default:
                Invalid(diagnostics, "The FDF dictionary's Encoding entry is not a name (Table 246); strings are read as PDFDocEncoding.");
                return TextEncoding.PdfDoc;
        }
    }

    private static void ReadAnnotations(ObjectLoader loader, CosArray array, List<FormDataAnnotation> annotations, DiagnosticSink diagnostics)
    {
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (CosObject element in array)
        {
            if (loader.Resolve(element) is not CosDictionary dictionary || !seen.Add(dictionary))
            {
                diagnostics.Report(
                    FormDiagnostics.FormDataAnnotationSkipped,
                    DiagnosticSeverity.Warning,
                    "An element of the FDF Annots array is not an annotation dictionary, or names one twice; it is skipped.");
                continue;
            }

            if (!dictionary.TryGetValue(Page, out CosObject? page) || loader.Resolve(page) is not CosInteger { Value: >= 0 and <= int.MaxValue } number)
            {
                diagnostics.Report(
                    FormDiagnostics.FormDataAnnotationSkipped,
                    DiagnosticSeverity.Warning,
                    "An FDF annotation has no Page entry with a page number (ISO 32000-2 §12.7.8.3.4, Table 254: required); it is skipped.",
                    objectReference: element as CosReference);
                continue;
            }

            annotations.Add(new FormDataAnnotation((int)number.Value, dictionary, element as CosReference));
        }
    }

    private static void Invalid(DiagnosticSink diagnostics, string message, CosReference? reference = null) =>
        diagnostics.Report(FormDiagnostics.FormDataInvalid, DiagnosticSeverity.Warning, message, objectReference: reference);

    private enum TextEncoding
    {
        PdfDoc,
        Utf8,
        Utf16,
    }

    /// <summary>Walks the field dictionaries and decodes their text.</summary>
    private sealed class FieldWalker(ObjectLoader loader, StreamDecoder streams, DiagnosticSink diagnostics, TextEncoding encoding)
    {
        public void ReadFields(CosArray roots, List<FormDataField> fields)
        {
            var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
            var work = new Stack<(CosObject Node, string? Parent, int Depth)>();
            for (int index = roots.Count - 1; index >= 0; index--)
            {
                work.Push((roots[index], null, 0));
            }

            while (work.TryPop(out (CosObject Node, string? Parent, int Depth) item))
            {
                if (loader.Resolve(item.Node) is not CosDictionary node || !seen.Add(node))
                {
                    Invalid(diagnostics, "An element of an FDF Fields or Kids array is not a field dictionary, or names one already read; it is skipped.", item.Node as CosReference);
                    continue;
                }

                if (item.Depth >= MaxFieldDepth)
                {
                    Invalid(diagnostics, $"The FDF field tree is nested deeper than {MaxFieldDepth} levels; the fields below are not read.", item.Node as CosReference);
                    continue;
                }

                string? partial = node.TryGetValue(FormNames.T, out CosObject? t) ? Text(loader.Resolve(t)) : null;
                if (partial is null)
                {
                    Invalid(diagnostics, "An FDF field dictionary has no T entry with its partial name (Table 249: required); its entries are read under its parent's name.", item.Node as CosReference);
                }

                string? name = partial is null ? item.Parent : item.Parent is null ? partial : $"{item.Parent}.{partial}";
                if (name is not null)
                {
                    FormDataField field = ReadEntry(node, name, item.Node as CosReference);
                    if (field.HasChanges)
                    {
                        fields.Add(field);
                    }
                }

                if (node.TryGetValue(FormNames.Kids, out CosObject? kids))
                {
                    if (loader.Resolve(kids) is CosArray array)
                    {
                        for (int index = array.Count - 1; index >= 0; index--)
                        {
                            work.Push((array[index], name, item.Depth + 1));
                        }
                    }
                    else
                    {
                        Invalid(diagnostics, "An FDF field's Kids entry is not an array (Table 249); its kids are not read.", item.Node as CosReference);
                    }
                }
            }
        }

        private FormDataField ReadEntry(CosDictionary node, string name, CosReference? reference)
        {
            var field = new FormDataField(name);
            if (node.TryGetValue(FormNames.V, out CosObject? value))
            {
                field.Value = Value(loader.Resolve(value), reference);
            }

            if (node.TryGetValue(FormNames.RV, out CosObject? rich))
            {
                field.RichValue = Text(loader.Resolve(rich));
                if (field.RichValue is null)
                {
                    Invalid(diagnostics, "An FDF field's RV entry is not a text string or text stream (Table 249); it is ignored.", reference);
                }
            }

            field.FieldFlags = Flags(node, FormNames.Ff, reference);
            field.SetFieldFlags = Flags(node, SetFf, reference);
            field.ClearFieldFlags = Flags(node, ClrFf, reference);
            field.WidgetFlags = Flags(node, AnnotationNames.F, reference);
            field.SetWidgetFlags = Flags(node, SetF, reference);
            field.ClearWidgetFlags = Flags(node, ClrF, reference);
            return field;
        }

        private uint? Flags(CosDictionary node, CosName key, CosReference? reference)
        {
            if (!node.TryGetValue(key, out CosObject? entry))
            {
                return null;
            }

            if (loader.Resolve(entry) is CosInteger { Value: >= int.MinValue and <= uint.MaxValue } integer)
            {
                return unchecked((uint)integer.Value);
            }

            Invalid(diagnostics, $"An FDF field's {key.Value} entry is not a 32-bit integer (Table 249); it is ignored.", reference);
            return null;
        }

        /// <summary>The value with its strings decoded and stored as text strings, so the importer reads every encoding the same way.</summary>
        private CosObject? Value(CosObject value, CosReference? reference)
        {
            switch (value)
            {
                case CosName:
                    return value;
                case CosString or CosStream:
                    return Text(value) is { } text ? new CosString(TextStringEncoder.Encode(text)) : null;
                case CosArray array:
                    var strings = new CosArray();
                    foreach (CosObject element in array)
                    {
                        CosObject resolved = loader.Resolve(element);
                        if (resolved is CosName)
                        {
                            strings.Add(resolved);
                        }
                        else if (Text(resolved) is { } item)
                        {
                            strings.Add(new CosString(TextStringEncoder.Encode(item)));
                        }
                    }

                    return strings;
                case CosNull:
                    return null;
                default:
                    // The importer reports a value of the wrong type for the field it names.
                    return value;
            }
        }

        private string? Text(CosObject value) => value switch
        {
            CosString text => Decode(text.Bytes),
            CosStream stream => Decode(streams.Decode(stream).Span),
            _ => null,
        };

        private string Decode(ReadOnlySpan<byte> bytes)
        {
            // Strings that begin with a byte order mark are Unicode whatever the Encoding says (Table 246).
            if (encoding == TextEncoding.PdfDoc || bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            {
                return TextStringDecoder.Decode(bytes);
            }

            return encoding == TextEncoding.Utf8 ? System.Text.Encoding.UTF8.GetString(bytes) : System.Text.Encoding.BigEndianUnicode.GetString(bytes);
        }
    }
}
