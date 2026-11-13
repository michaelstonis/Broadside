using System.Text;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Writing;

namespace Broadside;

/// <summary>Unencrypted wrapper documents: reading the encrypted payload a wrapper carries (issue #234) and writing one (issue #235).</summary>
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

    /// <summary>
    /// Creates an unencrypted wrapper document around <paramref name="payload"/>, an encrypted PDF file, with one blank cover page,
    /// on an engine with default options.
    /// </summary>
    /// <param name="payload">The encrypted PDF file's bytes, embedded unchanged; copied.</param>
    /// <returns>The wrapper, not yet saved; dispose it when done.</returns>
    /// <exception cref="ArgumentException">The payload is not encrypted, so names no cryptographic filter, and none is given.</exception>
    /// <remarks>ISO 32000-2 §7.6.7 (PDF 2.0). As <see cref="CreateUnencryptedWrapper(ReadOnlySpan{byte}, PdfUnencryptedWrapperOptions)"/> with default options.</remarks>
    public static PdfDocument CreateUnencryptedWrapper(ReadOnlySpan<byte> payload) => PdfEngine.Default.CreateUnencryptedWrapper(payload);

    /// <summary>
    /// Creates an unencrypted wrapper document around <paramref name="payload"/>, an encrypted PDF file, on an engine with default
    /// options: a PDF 2.0 document whose collection presents the payload to readers that have its cryptographic filter, and whose
    /// pages, the cover, are shown by readers that do not.
    /// </summary>
    /// <param name="payload">The encrypted PDF file's bytes, embedded unchanged; copied.</param>
    /// <param name="options">The cover, the cryptographic filter, and the payload's name, description and date.</param>
    /// <returns>The wrapper, not yet saved; dispose it when done.</returns>
    /// <exception cref="ArgumentException">
    /// The payload is not encrypted, so names no cryptographic filter, and <see cref="PdfUnencryptedWrapperOptions.CryptographicFilter"/>
    /// is not set.
    /// </exception>
    /// <exception cref="NotSupportedException">The cover is encrypted and its streams cannot be copied (see <see cref="AppendPages(PdfDocument)"/>).</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.6.7 (PDF 2.0) and Table 28. The payload is embedded (<c>application/pdf</c>, with <c>Params</c> <c>ModDate</c>,
    /// §14.13.2) under a file specification with <c>AFRelationship</c> <c>EncryptedPayload</c> and an encrypted payload dictionary
    /// (<c>EP</c>: <c>Type</c> <c>EncryptedPayload</c>, <c>Subtype</c> the cryptographic filter, <c>Version</c> when given); the
    /// specification is the one entry of the <c>EmbeddedFiles</c> name tree and is listed in the catalog's <c>AF</c> array; the
    /// catalog's <c>Collection</c> names it as the initial document (<c>D</c>) with <c>View</c> <c>H</c>. <c>F</c> and <c>UF</c> name
    /// the filter, never the payload's own file name (Table 43). The wrapper reads back clean through <see cref="EncryptedPayload"/>.
    /// </para>
    /// <para>
    /// The wrapper uses PDF 2.0 features, so a save declares PDF 2.0 (the version raising every save does). A payload that is not
    /// encrypted (its trailer has no <c>Encrypt</c> entry, or it cannot be read as a PDF file) is an <c>EncryptedPayloadNotEncrypted</c>
    /// warning, and refused in strict mode (<see cref="PdfOptions.UseStrict"/>, through <see cref="PdfEngine.CreateUnencryptedWrapper(ReadOnlySpan{byte}, PdfUnencryptedWrapperOptions)"/>).
    /// </para>
    /// </remarks>
    public static PdfDocument CreateUnencryptedWrapper(ReadOnlySpan<byte> payload, PdfUnencryptedWrapperOptions options) =>
        PdfEngine.Default.CreateUnencryptedWrapper(payload, options);

    /// <summary>Creates an unencrypted wrapper on the engine whose configuration is <paramref name="configuration"/> (issue #235).</summary>
    internal static PdfDocument CreateUnencryptedWrapper(EngineConfiguration configuration, ReadOnlySpan<byte> payload, PdfUnencryptedWrapperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        byte[] contents = payload.ToArray();
        CosName? encryptedBy = ReadEncryptionFilter(contents, configuration, out bool encrypted);
        string filter = options.CryptographicFilter ?? encryptedBy?.Value ?? throw new ArgumentException(
            "The payload names no cryptographic filter (it is not encrypted); give one with PdfUnencryptedWrapperOptions.WithCryptographicFilter (ISO 32000-2 §7.6.7, Table 28: Subtype is required).",
            nameof(payload));
        string name = options.Name ?? $"{filter} Protected PDF.pdf";

        PdfDocument wrapper = options.Cover is null ? CreateNew(configuration) : CreateEmpty(configuration);
        try
        {
            if (!encrypted)
            {
                // Strict mode throws here, before anything is embedded.
                wrapper._diagnostics.Report(
                    DiagnosticCodes.EncryptedPayloadNotEncrypted,
                    DiagnosticSeverity.Warning,
                    $"The payload '{name}' of the unencrypted wrapper is not an encrypted PDF file (ISO 32000-2 §7.6.7: the wrapper embeds an encrypted payload); it is embedded anyway.");
            }

            if (options.Cover is { } cover)
            {
                wrapper.AppendPages(cover);
            }

            PdfFileSpecification file = wrapper.CreateEmbeddedFile(
                name,
                contents,
                new PdfEmbeddedFileOptions()
                    .WithMediaType("application/pdf")
                    .WithDescription(options.Description ?? $"This embedded file is encrypted using the {filter} cryptographic filter.")
                    .WithModificationDate(options.ModificationDate ?? wrapper.TimeProvider.GetUtcNow())
                    .WithRelationship(PdfFileRelationship.EncryptedPayload));
            var description = new CosDictionary
            {
                [KnownNames.Type] = FileAndLayerNames.EncryptedPayload,
                [FileAndLayerNames.Subtype] = new CosName(filter),
            };
            if (options.CryptographicFilterVersion is { } version)
            {
                description[KnownNames.Version] = new CosName(version);
            }

            file.Dictionary![FileAndLayerNames.EP] = description;
            wrapper.AddEmbeddedFile(name, file);
            wrapper.AddAssociatedFile(wrapper.Catalog, file);
            wrapper.Catalog[FileAndLayerNames.Collection] = new CosDictionary
            {
                [KnownNames.Type] = FileAndLayerNames.Collection,
                [FileAndLayerNames.D] = new CosString(TextStringEncoder.Encode(name)),
                [FileAndLayerNames.View] = HiddenView,
            };
            wrapper.RequireFeature(VersionedFeatures.UnencryptedWrapper);
            return wrapper;
        }
        catch
        {
            wrapper.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the security handler that encrypts <paramref name="file"/> from its trailer's <c>Encrypt</c> dictionary (§7.6.1,
    /// Table 20 <c>Filter</c>) without authenticating; <paramref name="encrypted"/> is whether it has one. A file that cannot be read
    /// is not encrypted; deviations met on the way are not reported (the payload is the caller's, and is embedded as it is).
    /// </summary>
    private static CosName? ReadEncryptionFilter(byte[] file, EngineConfiguration configuration, out bool encrypted)
    {
        encrypted = false;
        using PdfSource source = PdfSource.FromMemory(file);
        try
        {
            var diagnostics = new DiagnosticSink(strict: false, observer: null);
            FileHeader header = FileHeader.Locate(source, diagnostics);
            ObjectLoader? loader = null;
            var streams = new StreamDecoder(
                configuration.Filters,
                configuration.MaxDecodedStreamLength,
                diagnostics,
                value => loader is not null ? loader.Resolve(value) : value is null or CosReference ? CosNull.Instance : value);
            var scan = new Lazy<FileScan>(() => FileScan.Run(source), LazyThreadSafetyMode.None);
            CrossReference crossReference = CrossReferenceReader.Read(source, header, streams, diagnostics, scan)
                ?? Reconstruct(source, header, scan.Value, streams, diagnostics, reportMissingTrailer: false);
            loader = new ObjectLoader(source, header, crossReference, diagnostics, new ObjectLoaderHooks(), streams) { Scan = scan };
            if (!crossReference.Trailer.TryGetValue(KnownNames.Encrypt, out CosObject? entry) || loader.Resolve(entry) is not CosDictionary encryption)
            {
                return null;
            }

            encrypted = true;
            CosName? filter = loader.Resolve(encryption.TryGetValue(FilterNames.Filter, out CosObject? named) ? named : null) as CosName;
            return filter ?? (encryption.ContainsKey(KnownNames.R) ? KnownNames.Standard : null);
        }
        catch (DiagnosticException)
        {
            return null;
        }
    }

    private static readonly CosName HiddenView = new("H");
}
