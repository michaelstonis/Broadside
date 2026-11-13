using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>Unencrypted wrapper documents: reading the encrypted payload a wrapper carries (issue #234).</summary>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Gets a value indicating whether the document is an unencrypted wrapper: it embeds an encrypted payload document
    /// (<see cref="EncryptedPayload"/>).
    /// </summary>
    /// <exception cref="DiagnosticException">In strict mode, when the wrapper deviates from §7.6.7 (<c>UnencryptedWrapperInvalid</c>).</exception>
    /// <remarks>ISO 32000-2 §7.6.7 (PDF 2.0). As <see cref="EncryptedPayload"/> is not <see langword="null"/>.</remarks>
    public bool IsUnencryptedWrapper => EncryptedPayload is not null;

    /// <summary>
    /// Gets the encrypted payload this document wraps, or <see langword="null"/> when it is not an unencrypted wrapper.
    /// </summary>
    /// <exception cref="DiagnosticException">In strict mode, when the wrapper deviates from §7.6.7 (<c>UnencryptedWrapperInvalid</c>).</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.6.7 (PDF 2.0). The payload is the <c>EmbeddedFiles</c> entry whose file specification has
    /// <c>AFRelationship</c> <c>EncryptedPayload</c> or an encrypted payload dictionary (<c>EP</c>, Table 28); of several, the one the
    /// collection's <c>D</c> names, else the first. A document with no such entry is not a wrapper and is read without diagnostics.
    /// Read on every call.
    /// </para>
    /// <para>
    /// A wrapper is still read when it deviates from the clause (no collection, a <c>D</c> naming another file, a <c>View</c> other
    /// than <c>H</c>, the payload missing from the catalog's <c>AF</c>, another <c>EmbeddedFiles</c> entry, no <c>EP</c> or no
    /// <c>Subtype</c> in it); the deviations are reported together as one <c>UnencryptedWrapperInvalid</c> warning. A
    /// <c>Subtype</c> no security handler of the engine provides is an <c>EncryptedPayloadFilterUnknown</c> information: the wrapper
    /// is well formed, this engine just may not decrypt the payload.
    /// </para>
    /// </remarks>
    public PdfEncryptedPayload? EncryptedPayload
    {
        get
        {
            IReadOnlyList<PdfEmbeddedFileEntry> entries = EmbeddedFiles;
            PdfCollection? collection = Collection;
            string? initial = collection?.InitialDocument;
            PdfEmbeddedFileEntry? chosen = null;
            foreach (PdfEmbeddedFileEntry entry in entries)
            {
                if (!IsPayload(entry.File))
                {
                    continue;
                }

                if (chosen is null || (initial is not null && entry.Name == initial && chosen.Name != initial))
                {
                    chosen = entry;
                }
            }

            if (chosen is null)
            {
                return null;
            }

            PdfFileSpecification file = chosen.File;
            CosDictionary? description = file.EncryptedPayload;
            var deviations = new List<string>();
            if (collection is null)
            {
                deviations.Add("the catalog has no Collection");
            }
            else
            {
                if (initial != chosen.Name)
                {
                    deviations.Add("the collection's D does not name the payload");
                }

                if (ViewReading.Name(this, collection.Dictionary, FileAndLayerNames.View)?.Value != "H")
                {
                    deviations.Add("the collection's View is not H");
                }
            }

            if (entries.Count != 1)
            {
                deviations.Add($"the EmbeddedFiles name tree has {entries.Count} entries, not exactly one");
            }

            if (!IsListedInCatalogAssociatedFiles(file.Dictionary!))
            {
                deviations.Add("the payload is not in the catalog's AF array");
            }

            if (file.Relationship != PdfFileRelationship.EncryptedPayload)
            {
                deviations.Add("the payload's AFRelationship is not EncryptedPayload");
            }

            if (description is null)
            {
                deviations.Add("the payload has no encrypted payload dictionary (EP)");
            }
            else
            {
                if (ViewReading.Name(this, description, KnownNames.Type) is { } type && !type.Equals(FileAndLayerNames.EncryptedPayload))
                {
                    deviations.Add($"the encrypted payload dictionary's Type is {type.Value}, not EncryptedPayload");
                }

                if (ViewReading.Name(this, description, FileAndLayerNames.Subtype) is null)
                {
                    deviations.Add("the encrypted payload dictionary has no Subtype naming the cryptographic filter");
                }
            }

            if (!file.Dictionary!.ContainsKey(FileAndLayerNames.EF))
            {
                deviations.Add("the payload's file specification has no EF dictionary");
            }

            if (deviations.Count > 0)
            {
                ViewReading.Warn(
                    this,
                    DiagnosticCodes.UnencryptedWrapperInvalid,
                    $"The unencrypted wrapper deviates from ISO 32000-2 §7.6.7 ({string.Join("; ", deviations)}); its payload '{chosen.Name}' is read anyway.",
                    file.Reference ?? CatalogReference);
            }

            var payload = new PdfEncryptedPayload(this, chosen.Name, file);
            if (payload.CryptographicFilter is { } filter && !payload.IsCryptographicFilterAvailable)
            {
                DiagnosticSink.Report(
                    DiagnosticCodes.EncryptedPayloadFilterUnknown,
                    DiagnosticSeverity.Information,
                    $"The encrypted payload '{chosen.Name}' needs the cryptographic filter {filter.Value}, which no security handler of this engine provides; the wrapper is read, and the payload opens only if its own encryption dictionary names a handler the engine has.",
                    offset: null,
                    file.Reference ?? CatalogReference);
            }

            return payload;
        }
    }

    /// <summary>Whether <paramref name="file"/> is marked as an encrypted payload, by relationship or by an <c>EP</c> dictionary.</summary>
    private static bool IsPayload(PdfFileSpecification file) =>
        file.Dictionary is not null && (file.Relationship == PdfFileRelationship.EncryptedPayload || file.EncryptedPayload is not null);

    /// <summary>Whether the catalog's <c>AF</c> lists <paramref name="file"/>; compared by identity, without validating the array.</summary>
    private bool IsListedInCatalogAssociatedFiles(CosDictionary file)
    {
        CosObject? value = Catalog.TryGetValue(FileAndLayerNames.AF, out CosObject? entry) ? Resolve(entry) : null;
        if (value is CosArray array)
        {
            foreach (CosObject element in array)
            {
                if (ReferenceEquals(Resolve(element), file))
                {
                    return true;
                }
            }

            return false;
        }

        return ReferenceEquals(value, file);
    }
}
