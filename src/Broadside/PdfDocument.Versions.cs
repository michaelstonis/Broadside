using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <content>
/// The version a save writes (issue #141): every writable feature declares its minimum PDF version, and a save raises the version
/// to the highest one used, or refuses in strict mode. ISO 32000-2 §7.5.2, §7.7.2 (Table 29), §7.12; ADR 0003.
/// </content>
public sealed partial class PdfDocument
{
    private readonly Lock _featureGate = new();
    private readonly HashSet<VersionedFeature> _features = [];

    /// <summary>
    /// Records that the document now uses <paramref name="feature"/>, through an API that wrote it; every later save raises the version
    /// to the feature's minimum (or refuses in strict mode) and declares its developer extension.
    /// </summary>
    /// <param name="feature">A row of <see cref="VersionedFeatures"/>.</param>
    /// <remarks>A mutation: called by the writing APIs, which no read runs concurrently with.</remarks>
    internal void RequireFeature(VersionedFeature feature)
    {
        lock (_featureGate)
        {
            _features.Add(feature);
        }
    }

    /// <summary>
    /// The first half of planning a save, which changes nothing in the document (issue #300): the writer for the document as it is,
    /// which refuses what it cannot write, and the version the save needs, the highest minimum version of its features. A version
    /// later than the document's is recorded as <c>VersionRaised</c> (Information), or refused in strict mode, before anything is
    /// written or changed.
    /// </summary>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a version later than the document's.</exception>
    /// <param name="options">The save's options.</param>
    /// <param name="archival">Whether the save writes a PDF/A conformance (#201), which adds <see cref="VersionedFeatures.PdfAOutput"/> (and, for PDF/A-3, takes out associated files, #202).</param>
    private VersionedSave CheckVersionedSave(PdfSaveOptions options, bool archival = false)
    {
        var features = new List<VersionedFeature>();
        FileWriter writer = PlanWriter(options, features);
        AddDocumentFeatures(features);
        if (archival)
        {
            AddArchivalFeatures(features, options);
        }

        PdfVersion needed = Writing.VersionPlan.Check(Version, features, _diagnostics);
        return new VersionedSave(writer, features, needed);
    }

    /// <summary>
    /// The second half of planning a save, once it has passed every check (issue #300): declares in the catalog what
    /// <paramref name="planned"/> decided, the developer extensions ISO/TS features declare (§7.12) and, for an incremental update,
    /// which writes no header, the raised version in the catalog's <c>Version</c> entry (§7.5.6, §7.7.2 Table 29); plans the writer
    /// again when the document changed since <paramref name="planned"/> (<paramref name="changed"/>, or the catalog here); and raises
    /// a full save's header to the highest minimum version the save's features need.
    /// </summary>
    /// <param name="options">The save's options.</param>
    /// <param name="planned">What <see cref="CheckVersionedSave"/> planned.</param>
    /// <param name="changed">Whether the save changed the document since (dated it, completed font subsets, laid its metadata out for PDF 2.0).</param>
    /// <param name="ceiling">
    /// The latest version a full save's header may state, for a conformance level based on an earlier version (PDF/A-2: 1.7, #201);
    /// the check step made sure no feature of the save needs more, so a new document's 2.0 is lowered to it.
    /// </param>
    private FileWriter ApplyVersionedSave(PdfSaveOptions options, VersionedSave planned, bool changed, PdfVersion? ceiling = null)
    {
        List<VersionedFeature> features = planned.Features;
        changed |= Writing.VersionPlan.AddExtensions(Catalog, features, Resolve);
        if (options.IncrementalUpdate && planned.Needed > Version)
        {
            Catalog[KnownNames.Version] = new CosName(planned.Needed.ToString());
            changed = true;
        }

        FileWriter writer = planned.Writer;
        if (changed)
        {
            // The document changed after it was planned: plan again, so the update writes what changed and a full save serializes
            // it. Dating, completing font subsets and the metadata layout add no feature, so the version decided stands.
            bool archival = features.Contains(VersionedFeatures.PdfAOutput);
            features.Clear();
            writer = PlanWriter(options, features);
            AddDocumentFeatures(features);
            if (archival)
            {
                AddArchivalFeatures(features, options);
            }
        }

        if (!options.IncrementalUpdate)
        {
            writer.Version = Writing.VersionPlan.Highest(features, writer.Version);
            if (ceiling is { } latest && writer.Version > latest)
            {
                writer.Version = latest;
            }
        }

        return writer;
    }

    /// <summary>What <see cref="CheckVersionedSave"/> planned: the writer, the features the save uses and the version they need.</summary>
    private readonly record struct VersionedSave(FileWriter Writer, List<VersionedFeature> Features, PdfVersion Needed);

    private FileWriter PlanWriter(PdfSaveOptions options, List<VersionedFeature> features) => options.IncrementalUpdate
        ? SavePlan.CreateIncremental(_source, _loader, options, StreamEncoders, Security, features)
        : SavePlan.Create(_source, _loader, Linearization, options, StreamEncoders, Security, features);

    /// <summary>
    /// Adds the features the document itself uses: those its writing APIs recorded (<see cref="RequireFeature"/>), its structure tree's
    /// (<see cref="Structure.PdfStructureTreeRoot.MinimumVersion"/>, once the tree has been read or written), and each developer
    /// extension it declares, which needs its <c>BaseVersion</c> (§7.12.4).
    /// </summary>
    private void AddDocumentFeatures(List<VersionedFeature> features)
    {
        lock (_featureGate)
        {
            features.AddRange(_features);
        }

        // The structure tree: what it uses, namespaces (PDF 2.0) included, read from the tree rather than recorded by every setter.
        // Only once the tree has been read or written in this session, so a save of a document nobody looked into the tree of
        // neither walks it nor reports what reading it repairs (§14.7.2).
        if (Volatile.Read(ref _structure) is not null && StructureTree is { } structure)
        {
            features.Add(VersionedFeatures.StructureTree(structure.MinimumVersion));
        }

        if (Resolve(Catalog.GetValueOrDefault(KnownNames.Extensions)) is not CosDictionary extensions)
        {
            return;
        }

        foreach ((CosName prefix, CosObject value) in extensions)
        {
            if (KnownNames.Type.Equals(prefix))
            {
                continue;
            }

            CosObject resolved = Resolve(value);
            IEnumerable<CosObject> items = resolved is CosArray array ? array : [resolved];
            foreach (CosObject item in items)
            {
                if (Resolve(item) is CosDictionary dictionary && PdfDeveloperExtension.ReadBaseVersion(this, dictionary) is { } baseVersion)
                {
                    features.Add(VersionedFeatures.DeveloperExtension(prefix, PdfDeveloperExtension.ReadExtensionLevel(this, dictionary), baseVersion));
                }
            }
        }
    }
}
