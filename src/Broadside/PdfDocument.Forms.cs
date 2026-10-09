using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <content>Interactive forms (issue #74): ISO 32000-2 §12.7 and the catalog's AcroForm and NeedsRendering entries (§7.7.2).</content>
public sealed partial class PdfDocument
{
    private PdfAcroForm? _acroForm;

    /// <summary>Gets the document's interactive form, or <see langword="null"/> when the catalog has no <c>AcroForm</c> dictionary.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29 (PDF 1.2), and §12.7.3. The same view is returned while the catalog's <c>AcroForm</c> is the same
    /// dictionary. An entry that is not a dictionary reads as absent with an <c>AcroFormInvalid</c> diagnostic.
    /// </remarks>
    public PdfAcroForm? AcroForm
    {
        get
        {
            Catalog.TryGetValue(FormNames.AcroForm, out CosObject? entry);
            switch (Resolve(entry))
            {
                case CosNull:
                    return null;
                case CosDictionary dictionary:
                    PdfAcroForm? cached = Volatile.Read(ref _acroForm);
                    if (cached is not null && ReferenceEquals(cached.Dictionary, dictionary))
                    {
                        return cached;
                    }

                    var created = new PdfAcroForm(this, dictionary, entry as CosReference);
                    PdfAcroForm? raced = Interlocked.CompareExchange(ref _acroForm, created, cached);
                    return raced == cached ? created : (ReferenceEquals(raced!.Dictionary, dictionary) ? raced : created);
                default:
                    CatalogView.Report(DiagnosticCodes.AcroFormInvalid, "The catalog's AcroForm entry shall be the interactive form dictionary; it is ignored.");
                    return null;
            }
        }
    }

    /// <summary>
    /// Records, when the document is opened, that its interactive form carries an XFA resource (and whether the form is dynamic): a
    /// legal but deprecated feature Broadside keeps without rendering, so an Information diagnostic. No stream is decoded.
    /// </summary>
    /// <remarks>ISO 32000-2 Annex K and §7.7.2, Table 29 (<c>NeedsRendering</c>).</remarks>
    private void DetectXfa()
    {
        if (!Catalog.ContainsKey(FormNames.AcroForm) || Resolve(Catalog[FormNames.AcroForm]) is not CosDictionary acroForm ||
            !acroForm.TryGetValue(FormNames.XFA, out CosObject? entry))
        {
            return;
        }

        CosReference? reference = Catalog[FormNames.AcroForm] as CosReference ?? CatalogReference;
        bool present = Resolve(entry) switch
        {
            CosStream => true,
            CosArray array => array.Count > 0,
            _ => false,
        };
        if (!present)
        {
            return;
        }

        _diagnostics.Report(
            DiagnosticCodes.XfaFormPresent,
            DiagnosticSeverity.Information,
            "The interactive form has an XFA resource (Annex K, deprecated in PDF 2.0); it is kept but not used: the AcroForm fields are read.",
            objectReference: reference);
        if (Resolve(Catalog.TryGetValue(FormNames.NeedsRendering, out CosObject? needsRendering) ? needsRendering : null) is CosBoolean { Value: true })
        {
            _diagnostics.Report(
                DiagnosticCodes.XfaFormDynamic,
                DiagnosticSeverity.Information,
                "The catalog's NeedsRendering is true: the document is a dynamic XFA form whose pages are placeholders; its content is not rendered.",
                objectReference: CatalogReference);
        }
    }
}
