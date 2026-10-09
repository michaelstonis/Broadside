using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <content>The document-level entries of the catalog and the trailer (issue #69): ISO 32000-2 §7.7.2, §7.12, §12.2, §12.11, §14.3, §14.4.</content>
public sealed partial class PdfDocument
{
    private static readonly CosName PageLayoutKey = new("PageLayout");
    private static readonly CosName PageModeKey = new("PageMode");
    private static readonly CosName ViewerPreferencesKey = new("ViewerPreferences");
    private static readonly CosName LangKey = new("Lang");
    private static readonly CosName PageLabelsKey = new("PageLabels");
    private static readonly CosName MetadataKey = new("Metadata");
    private static readonly CosName RequirementsKey = new("Requirements");
    private static readonly CosName MetadataType = new("Metadata");
    private static readonly CosName SubtypeKey = new("Subtype");
    private static readonly CosName XmlSubtype = new("XML");

    private readonly OnceCache<CosStream, XmpPacket?> _xmpPackets = new();

    /// <summary>Gets the version the file header states, or <see langword="null"/> when the header has no valid version.</summary>
    /// <remarks>ISO 32000-2 §7.5.2. <see cref="Version"/> is the version the document conforms to.</remarks>
    public PdfVersion? HeaderVersion => _loader.Header.Version;

    /// <summary>Gets the catalog's <c>Version</c> entry, or <see langword="null"/> when it has none (or none that is a version).</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29 (PDF 1.4): it overrides the header when later, so an incremental update can raise the version
    /// without rewriting the header. Read from the catalog on every call. <see cref="Version"/> is the version the document conforms to.
    /// </remarks>
    public PdfVersion? CatalogVersion => ReadCatalogVersion(Catalog, diagnostics: null);

    /// <summary>Gets how a viewer lays the pages out when the document opens (<c>PageLayout</c>). Default <see cref="PdfPageLayout.SinglePage"/>.</summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29. An unknown name reads as the default with a <c>PageLayoutInvalid</c> diagnostic.</remarks>
    public PdfPageLayout PageLayout => CatalogView.ReadName(
        PageLayoutKey,
        DiagnosticCodes.PageLayoutInvalid,
        PdfPageLayout.SinglePage,
        ("SinglePage", PdfPageLayout.SinglePage),
        ("OneColumn", PdfPageLayout.OneColumn),
        ("TwoColumnLeft", PdfPageLayout.TwoColumnLeft),
        ("TwoColumnRight", PdfPageLayout.TwoColumnRight),
        ("TwoPageLeft", PdfPageLayout.TwoPageLeft),
        ("TwoPageRight", PdfPageLayout.TwoPageRight))!.Value;

    /// <summary>Gets which panel a viewer shows when the document opens (<c>PageMode</c>). Default <see cref="PdfPageMode.UseNone"/>.</summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29. An unknown name reads as the default with a <c>PageModeInvalid</c> diagnostic.</remarks>
    public PdfPageMode PageMode => CatalogView.ReadName(
        PageModeKey,
        DiagnosticCodes.PageModeInvalid,
        PdfPageMode.UseNone,
        ("UseNone", PdfPageMode.UseNone),
        ("UseOutlines", PdfPageMode.UseOutlines),
        ("UseThumbs", PdfPageMode.UseThumbs),
        ("FullScreen", PdfPageMode.FullScreen),
        ("UseOC", PdfPageMode.UseOC),
        ("UseAttachments", PdfPageMode.UseAttachments))!.Value;

    /// <summary>
    /// Gets the viewer preferences (<c>ViewerPreferences</c>), or <see langword="null"/> when the catalog has none: the viewer's own
    /// preferences then apply.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29, and §12.2. An entry that is not a dictionary is ignored with a <c>ViewerPreferencesInvalid</c> diagnostic.</remarks>
    public PdfViewerPreferences? ViewerPreferences
    {
        get
        {
            Catalog.TryGetValue(ViewerPreferencesKey, out CosObject? entry);
            switch (Resolve(entry))
            {
                case CosNull:
                    return null;
                case CosDictionary preferences:
                    return new PdfViewerPreferences(this, preferences, entry as CosReference ?? CatalogReference);
                default:
                    CatalogView.Report(DiagnosticCodes.ViewerPreferencesInvalid, "The catalog's ViewerPreferences entry shall be a dictionary; it is ignored.");
                    return null;
            }
        }
    }

    /// <summary>Gets the page labels (<c>PageLabels</c>, PDF 1.3), or <see langword="null"/> when the catalog has none: pages are then numbered from 1.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29, and §12.4.2. An entry that is not a number tree dictionary is ignored with a
    /// <c>NumberTreeNodeInvalid</c> diagnostic.
    /// </remarks>
    public PdfPageLabels? PageLabels =>
        GetNumberTreeReader(Catalog.GetValueOrDefault(PageLabelsKey)) is { } tree ? new PdfPageLabels(this, tree) : null;

    /// <summary>Gets the natural language of the document's text (<c>Lang</c>), such as <c>en-US</c>, or <see langword="null"/> when unknown.</summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29 (PDF 1.4), and §14.9.2. Not checked against BCP 47.</remarks>
    public string? Language => CatalogView.ReadText(LangKey, DiagnosticCodes.CatalogEntryInvalid);

    /// <summary>Gets the developer extensions the document declares (<c>Extensions</c>), in the order written; empty when none.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.12, Tables 48 and 49. A prefix whose value is an array (PDF 2.0) contributes one extension per dictionary in it.
    /// Deviations are reported when this property is read: an entry that is not a dictionary (<c>ExtensionsInvalid</c>), a prefix value
    /// that is neither a developer extensions dictionary nor an array of them or that lacks a required entry
    /// (<c>DeveloperExtensionInvalid</c>), and indirect references where everything "shall be direct objects" (<c>ExtensionsNotDirect</c>).
    /// </remarks>
    public IReadOnlyList<PdfDeveloperExtension> Extensions
    {
        get
        {
            Catalog.TryGetValue(KnownNames.Extensions, out CosObject? entry);
            CosObject value = Resolve(entry);
            if (value is CosNull)
            {
                return [];
            }

            DictionaryView catalog = CatalogView;
            if (value is not CosDictionary extensions)
            {
                catalog.Report(DiagnosticCodes.ExtensionsInvalid, "The catalog's Extensions entry shall be a dictionary; it is ignored.");
                return [];
            }

            bool indirect = entry is CosReference;
            var result = new List<PdfDeveloperExtension>();
            foreach (KeyValuePair<CosName, CosObject> pair in extensions)
            {
                if (KnownNames.Type.Equals(pair.Key))
                {
                    continue;
                }

                indirect |= pair.Value is CosReference;
                switch (Resolve(pair.Value))
                {
                    case CosDictionary dictionary:
                        AddExtension(pair.Key, dictionary);
                        break;
                    case CosArray array:
                        foreach (CosObject element in array)
                        {
                            indirect |= element is CosReference;
                            if (Resolve(element) is CosDictionary item)
                            {
                                AddExtension(pair.Key, item);
                            }
                            else
                            {
                                ReportExtension(pair.Key);
                            }
                        }

                        break;
                    default:
                        ReportExtension(pair.Key);
                        break;
                }
            }

            if (indirect)
            {
                catalog.Report(DiagnosticCodes.ExtensionsNotDirect, "The extensions dictionary and the developer extensions dictionaries shall be direct objects; they are read through their references.");
            }

            return result;

            void AddExtension(CosName prefix, CosDictionary dictionary)
            {
                if (PdfDeveloperExtension.ReadBaseVersion(this, dictionary) is null || PdfDeveloperExtension.ReadExtensionLevel(this, dictionary) is null)
                {
                    ReportExtension(prefix);
                }

                result.Add(new PdfDeveloperExtension(this, prefix, dictionary));
            }

            void ReportExtension(CosName prefix) => catalog.Report(
                DiagnosticCodes.DeveloperExtensionInvalid,
                $"The developer extension {prefix.Value} shall be a dictionary, or an array of dictionaries, with a BaseVersion name and an ExtensionLevel integer.");
        }
    }

    /// <summary>Gets the requirements the document places on an interactive processor (<c>Requirements</c>, PDF 1.7); empty when none.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29, and §12.11. Exposed, never evaluated. An entry that is not an array of requirement dictionaries,
    /// a requirement without its type or with a penalty outside 0 to 100, and handlers that are not dictionaries are reported as
    /// <c>RequirementInvalid</c> when this property is read; elements that are not dictionaries are skipped.
    /// </remarks>
    public IReadOnlyList<PdfRequirement> Requirements
    {
        get
        {
            CosObject value = Resolve(Catalog.GetValueOrDefault(RequirementsKey));
            if (value is CosNull)
            {
                return [];
            }

            const string message = "The catalog's Requirements entry shall be an array of requirement dictionaries, each with a type and a penalty from 0 to 100 (Table 273).";
            if (value is not CosArray array)
            {
                CatalogView.Report(DiagnosticCodes.RequirementInvalid, message);
                return [];
            }

            var requirements = new List<PdfRequirement>(array.Count);
            bool invalid = false;
            foreach (CosObject element in array)
            {
                if (Resolve(element) is CosDictionary dictionary)
                {
                    invalid |= !PdfRequirement.IsWellFormed(this, dictionary);
                    requirements.Add(new PdfRequirement(this, dictionary));
                }
                else
                {
                    invalid = true;
                }
            }

            if (invalid)
            {
                CatalogView.Report(DiagnosticCodes.RequirementInvalid, message);
            }

            return requirements;
        }
    }

    /// <summary>Gets the document information dictionary (the trailer's <c>Info</c>), or <see langword="null"/> when the document has none.</summary>
    /// <remarks>
    /// ISO 32000-2 §14.3.3 and §7.5.5, Table 15. An entry that is not a dictionary is ignored with an <c>InfoDictionaryInvalid</c>
    /// diagnostic. <see cref="Properties"/> resolves it against the XMP metadata.
    /// </remarks>
    public PdfDocumentInformation? Information
    {
        get
        {
            Trailer.TryGetValue(KnownNames.Info, out CosObject? entry);
            switch (Resolve(entry))
            {
                case CosNull:
                    return null;
                case CosDictionary info:
                    return new PdfDocumentInformation(this, info, entry as CosReference);
                default:
                    _diagnostics.Report(
                        DiagnosticCodes.InfoDictionaryInvalid,
                        DiagnosticSeverity.Warning,
                        "The trailer's Info entry shall refer to the document information dictionary; it is ignored.",
                        objectReference: entry as CosReference);
                    return null;
            }
        }
    }

    /// <summary>Gets the document's metadata stream (the catalog's <c>Metadata</c>), or <see langword="null"/> when the document has none.</summary>
    /// <remarks>
    /// ISO 32000-2 §14.3.2, Table 347. An entry that is not a stream is ignored with a <c>MetadataStreamInvalid</c> diagnostic; a
    /// stream whose <c>Type</c> is not <c>Metadata</c> or whose <c>Subtype</c> is not <c>XML</c> is read with a
    /// <c>MetadataStreamTypeInvalid</c> diagnostic.
    /// </remarks>
    public PdfMetadata? Metadata
    {
        get
        {
            Catalog.TryGetValue(MetadataKey, out CosObject? entry);
            switch (Resolve(entry))
            {
                case CosNull:
                    return null;
                case CosStream stream:
                    CosReference? reference = entry as CosReference;
                    if (!MetadataType.Equals(Resolve(stream.Dictionary.GetValueOrDefault(KnownNames.Type)))
                        || !XmlSubtype.Equals(Resolve(stream.Dictionary.GetValueOrDefault(SubtypeKey))))
                    {
                        _diagnostics.Report(
                            DiagnosticCodes.MetadataStreamTypeInvalid,
                            DiagnosticSeverity.Warning,
                            "A metadata stream shall have Type Metadata and Subtype XML; it is read as XMP anyway.",
                            objectReference: reference);
                    }

                    return new PdfMetadata(this, stream, reference);
                default:
                    CatalogView.Report(DiagnosticCodes.MetadataStreamInvalid, "The catalog's Metadata entry shall be a metadata stream; it is ignored.");
                    return null;
            }
        }
    }

    /// <summary>Gets the common document properties, each from the XMP metadata when it has it and from the Info dictionary otherwise.</summary>
    /// <remarks>ISO 32000-2 §14.3.1, §14.3.3 and §14.3.4.</remarks>
    public PdfDocumentProperties Properties => new(this);

    /// <summary>Gets the file identifier (the trailer's <c>ID</c>), or <see langword="null"/> when the trailer has none or it is not two strings.</summary>
    /// <remarks>
    /// ISO 32000-2 §14.4 and §7.5.5, Table 15: an array of two byte strings, each at least 16 bytes. Shorter strings are kept as they
    /// are; an entry that is not two strings is ignored. Both are reported as <c>FileIdentifierInvalid</c>.
    /// </remarks>
    public PdfFileIdentifier? FileIdentifier
    {
        get
        {
            CosObject value = Resolve(Trailer.GetValueOrDefault(KnownNames.ID));
            if (value is CosNull)
            {
                return null;
            }

            if (value is not CosArray { Count: 2 } array || Resolve(array[0]) is not CosString permanent || Resolve(array[1]) is not CosString changing)
            {
                _diagnostics.Report(DiagnosticCodes.FileIdentifierInvalid, DiagnosticSeverity.Warning, "The trailer's ID entry shall be an array of two byte strings; it is ignored.");
                return null;
            }

            if (permanent.Bytes.Length < 16 || changing.Bytes.Length < 16)
            {
                _diagnostics.Report(DiagnosticCodes.FileIdentifierInvalid, DiagnosticSeverity.Warning, "Each file identifier shall be at least 16 bytes long; the shorter ones are used as they are.");
            }

            return new PdfFileIdentifier(permanent.Bytes.ToArray(), changing.Bytes.ToArray());
        }
    }

    /// <summary>Gets the indirect reference to the catalog, when the trailer has one.</summary>
    internal CosReference? CatalogReference => Trailer.GetValueOrDefault(KnownNames.Root) as CosReference;

    private DictionaryView CatalogView => new(this, Catalog, CatalogReference);

    /// <summary>
    /// Returns the XMP packet of a metadata stream: parsed once and kept while the stream is unchanged, parsed again on every read once it
    /// has been changed (dirty tracking is sticky, so a changed stream is never served from the cache).
    /// </summary>
    internal XmpPacket? ReadXmpPacket(CosStream stream, CosReference? reference)
    {
        return stream.IsDirty
            ? Parse((stream, reference, this))
            : _xmpPackets.GetOrCreate(
                stream,
                (stream, reference, this),
                static (_, state) => new Created<XmpPacket?>(Parse(state)),
                static (_, _) => null);

        static XmpPacket? Parse((CosStream Stream, CosReference? Reference, PdfDocument Document) state)
        {
            ReadOnlyMemory<byte> data = state.Document.DecodeStream(state.Stream);
            DiagnosticSink sink = state.Document._diagnostics;
            return XmpPacketReader.Read(
                data.Span,
                (code, severity, message) => sink.Report(code, severity, message, objectReference: state.Reference));
        }
    }
}
