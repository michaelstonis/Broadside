using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Writing;

namespace Broadside;

/// <content>
/// PDF/A output (issues #201, #202, #203): a save with <see cref="PdfSaveOptions.PdfAConformance"/> checks the document against the
/// conformance level, then adds what the level requires. ISO 19005-2, ISO 19005-3 and ISO 19005-4, cited by veraPDF
/// validation-profile rule identifiers (open decision 12).
/// </content>
public sealed partial class PdfDocument
{
    private static readonly PdfVersion PdfA2Version = new(1, 7);
    private static readonly CosName OutputIntentType = new("OutputIntent");
    private static readonly CosName PdfAOutputIntent = new("GTS_PDFA1");
    private static readonly CosName OutputIntentSubtypeKey = new("S");
    private static readonly CosName OutputConditionIdentifierKey = new("OutputConditionIdentifier");
    private static readonly CosName RegistryNameKey = new("RegistryName");
    private static readonly CosName OutputIntentInfoKey = new("Info");
    private static readonly CosName DestOutputProfileKey = new("DestOutputProfile");
    private static readonly CosName OutputIntentsCatalogKey = new("OutputIntents");
    private static readonly CosName PieceInfoKey = new("PieceInfo");
    private static readonly CosName ModDateKey = new("ModDate");

    /// <summary>
    /// The check step of a PDF/A save, on the document as it is (issue #300): reports every rule the save cannot meet as a
    /// <c>PdfANonConformant</c> warning, which strict mode throws before anything is written or changed.
    /// </summary>
    /// <returns>Whether the save writes the conformance: a level is asked for and every rule checked here can be met.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first rule the document cannot meet.</exception>
    private bool CheckArchivalSave(PdfSaveOptions options)
    {
        if (options.PdfAConformance == PdfAConformance.None)
        {
            return false;
        }

        IReadOnlyList<PdfAViolation> violations = PdfAChecker.Check(this, options);
        foreach (PdfAViolation violation in violations)
        {
            ReportNonConformance(options, violation);
        }

        return violations.Count == 0;
    }

    /// <summary>
    /// Checks the version the save needs against the one the part is based on (veraPDF rule 6.1.2-1): PDF 1.7 for PDF/A-2 and PDF/A-3,
    /// PDF 2.0 for PDF/A-4. A feature the save writes that needs a later version cannot conform. PDF/A-3's associated files are not
    /// among them (see <see cref="AddArchivalFeatures"/>).
    /// </summary>
    /// <returns>Whether the save still writes the conformance.</returns>
    /// <exception cref="DiagnosticException">In strict mode, when a feature needs a version later than the part's.</exception>
    private bool CheckArchivalVersion(PdfSaveOptions options, VersionedSave planned)
    {
        PdfVersion based = options.PdfAConformance.BaseVersion();
        IEnumerable<VersionedFeature> later = planned.Features.Where(feature => feature.MinimumVersion > based).Distinct();
        bool conformant = true;
        foreach (VersionedFeature feature in later)
        {
            ReportNonConformance(options, new PdfAViolation("6.1.2-1", string.Create(CultureInfo.InvariantCulture, $"{feature} needs PDF {feature.MinimumVersion}; PDF/A-{options.PdfAConformance.Part()} is based on PDF {based}"), null));
            conformant = false;
        }

        return conformant;
    }

    private void ReportNonConformance(PdfSaveOptions options, PdfAViolation violation) =>
        _diagnostics.Report(
            DiagnosticCodes.PdfANonConformant,
            DiagnosticSeverity.Warning,
            $"The file cannot conform to {options.PdfAConformance.Describe()}: {violation.Message} (veraPDF rule {violation.Rule}, ISO 19005-{options.PdfAConformance.Part()} clause {violation.Rule[..violation.Rule.LastIndexOf('-')]}).",
            objectReference: violation.Reference);

    /// <summary>Records that a lenient save does not claim the conformance it could not meet.</summary>
    private void ReportNotIdentified(PdfSaveOptions options) =>
        _diagnostics.Report(
            DiagnosticCodes.PdfANotIdentified,
            DiagnosticSeverity.Information,
            $"The file is saved without the {options.PdfAConformance.Describe()} identification, output intent or other PDF/A changes: the document cannot conform (see the PdfANonConformant diagnostics).");

    /// <summary>
    /// Adds to <paramref name="features"/> what a conformant PDF/A save writes (<see cref="VersionedFeatures.PdfAOutput"/>) and, for
    /// PDF/A-3, takes associated files out: ISO 19005-3 adds <c>AF</c> and <c>AFRelationship</c> to its PDF 1.7 base (6.8, veraPDF
    /// rules 6.8-3 and 6.8-4), so they neither raise the version nor break rule 6.1.2-1. PDF/A-4 adds
    /// <see cref="VersionedFeatures.PdfA4Output"/>: its file has a <c>%PDF-2.n</c> header (ISO 19005-4, rule 6.1.2-1).
    /// </summary>
    private static void AddArchivalFeatures(List<VersionedFeature> features, PdfSaveOptions options)
    {
        features.Add(VersionedFeatures.PdfAOutput);
        if (options.PdfAConformance.Part() == 4)
        {
            features.Add(VersionedFeatures.PdfA4Output);
        }
        else if (options.PdfAConformance.AllowsAssociatedFiles())
        {
            features.RemoveAll(feature => feature == VersionedFeatures.AssociatedFiles);
        }
    }

    /// <summary>
    /// The change step of a conformant PDF/A save, after the checks (issue #300) and recorded by the save's change log when it has
    /// one (issue #306): adds a <c>GTS_PDFA1</c> output intent with Broadside's sRGB profile unless an output intent already has a
    /// destination profile (which it then shares, veraPDF rule 6.2.3-2), and writes the XMP packet with the PDF/A identification of
    /// the level, its part and conformance (rules 6.6.2.1-1, 6.6.4-1 to 6.6.4-5), without <c>pdf:Trapped</c> (6.6.2.3.1-1) in the
    /// layout of the PDF 1.7 file the save writes. A PDF/A-4 save (issue #203) identifies the part, the revision and, for 4f, the
    /// conformance (ISO 19005-4 rules 6.7.3-2 to 6.7.3-5) in the PDF 2.0 layout, keeps <c>pdf:Trapped</c> (PDF/A-4 needs no
    /// extension schema), takes the document information dictionary out of the trailer once its entries are in the packet, or keeps
    /// only its <c>ModDate</c> when the catalog has <c>PieceInfo</c> (rules 6.1.3-4, 6.1.3-5), and drops a catalog <c>Version</c>
    /// that is not 2.n (rule 6.1.12-1).
    /// </summary>
    /// <param name="options">The save's options.</param>
    /// <param name="accessible">Whether the save is also a conformant PDF/UA one, whose identification this packet carries.</param>
    /// <returns><see langword="true"/>: the document changed.</returns>
    private bool ApplyArchivalSave(PdfSaveOptions options, bool accessible)
    {
        AddPdfAOutputIntent();
        var writer = new DocumentMetadataWriter(this);
        if (accessible)
        {
            // Issue #204: the PDF/UA identification goes in the same packet, its schema described for PDF/A (6.6.2.3.1).
            IdentifyAccessibility(writer, extensionSchema: true);
        }

        PdfAConformance level = options.PdfAConformance;
        writer
            .SetProperty(XmpNamespaces.PdfAIdentification, "part", level.Part().ToString(CultureInfo.InvariantCulture))
            .SetProperty(XmpNamespaces.PdfAIdentification, "conformance", level.Level())
            .SetProperty(XmpNamespaces.PdfAIdentification, "rev", level.Revision())
            .SetProperty(XmpNamespaces.PdfAIdentification, "amd", null)
            .SetProperty(XmpNamespaces.PdfAIdentification, "corr", null);
        if (level.Part() == 4)
        {
            writer.Commit(Now(), createPacket: true, layout: level.BaseVersion());
            KeepPdfA4Information();
            return true;
        }

        // pdf:Trapped is not in the XMP 2005 Adobe PDF schema PDF/A-2 predefines (veraPDF rules 6.6.2.3.1-1, -2) and would need an
        // extension schema; PDF/A-2 does not require the Info dictionary and the packet to agree, so Trapped stays in Info only.
        writer
            .SetProperty(XmpNamespaces.Pdf, "Trapped", null)
            .Commit(Now(), createPacket: true, layout: PdfA2Version);
        return true;
    }

    /// <summary>
    /// What PDF/A-4 allows of the document information dictionary and the catalog's version, once the PDF 2.0 layout has put the
    /// metadata in the XMP packet: no <c>Info</c> in the trailer unless the catalog has <c>PieceInfo</c>, and then only its
    /// <c>ModDate</c> (ISO 19005-4, veraPDF rules 6.1.3-4 and 6.1.3-5); no catalog <c>Version</c> other than 2.n (rule 6.1.12-1).
    /// </summary>
    private void KeepPdfA4Information()
    {
        if (Trailer.TryGetValue(KnownNames.Info, out CosObject? entry))
        {
            if (Catalog.ContainsKey(PieceInfoKey) && Resolve(entry) is CosDictionary info)
            {
                foreach (CosName key in info.Keys.Where(key => !key.Equals(ModDateKey)).ToArray())
                {
                    _ = info.Remove(key);
                }
            }
            else
            {
                _ = Trailer.Remove(KnownNames.Info);
            }
        }

        if (Catalog.ContainsKey(KnownNames.Version) && ReadCatalogVersion(Catalog, diagnostics: null) is not { Major: 2 })
        {
            _ = Catalog.Remove(KnownNames.Version);
        }
    }

    /// <summary>Adds the PDF/A output intent (ISO 32000-2 §14.11.5, Table 401) unless the catalog already has a <c>GTS_PDFA1</c> one with a profile.</summary>
    private void AddPdfAOutputIntent()
    {
        CosArray? intents = Resolve(Catalog.GetValueOrDefault(OutputIntentsCatalogKey)) as CosArray;
        if (intents is not null && intents.Any(item => Resolve(item) is CosDictionary intent
            && PdfAOutputIntent.Equals(Resolve(intent.GetValueOrDefault(OutputIntentSubtypeKey)))
            && Resolve(intent.GetValueOrDefault(DestOutputProfileKey)) is CosStream))
        {
            return;
        }

        // An output intent of another kind (PDF/X) with a profile: the PDF/A one shares it, all profiles being one object (6.2.3-2).
        CosObject? profile = intents?
            .Select(item => Resolve(item) is CosDictionary intent ? intent.GetValueOrDefault(DestOutputProfileKey) : null)
            .FirstOrDefault(entry => Resolve(entry) is CosStream);

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = OutputIntentType,
            [OutputIntentSubtypeKey] = PdfAOutputIntent,
        };
        if (profile is null)
        {
            var stream = new CosStream(new CosDictionary { [KnownNames.N] = new CosInteger(3) }, PdfIccBasedColorSpace.CreateSrgbProfileData());
            profile = AddObject(stream);
            dictionary[OutputConditionIdentifierKey] = Text("sRGB IEC61966-2.1");
            dictionary[RegistryNameKey] = Text("http://www.color.org");
            dictionary[OutputIntentInfoKey] = Text("sRGB IEC61966-2.1");
        }
        else
        {
            dictionary[OutputConditionIdentifierKey] = Text("Custom");
        }

        dictionary[DestOutputProfileKey] = profile;
        CosReference reference = AddObject(dictionary);
        if (intents is null)
        {
            Catalog[OutputIntentsCatalogKey] = new CosArray([reference]);
        }
        else
        {
            intents.Insert(0, reference);
        }

        static CosString Text(string text) => new(System.Text.Encoding.ASCII.GetBytes(text));
    }
}
