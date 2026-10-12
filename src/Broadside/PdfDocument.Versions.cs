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
    /// Plans a save: the writer, with its header raised to the highest minimum version the save's features need. A version later than
    /// the document's is recorded as <c>VersionRaised</c> (Information), or refused in strict mode, before anything is written; an
    /// incremental update, which writes no header, raises the catalog's <c>Version</c> entry instead (§7.5.6, §7.7.2 Table 29). The
    /// developer extensions ISO/TS features declare are added to the catalog's <c>Extensions</c> dictionary (§7.12).
    /// </summary>
    private FileWriter PlanVersionedSave(PdfSaveOptions options)
    {
        var features = new List<VersionedFeature>();
        FileWriter writer = PlanWriter(options, features);
        AddDocumentFeatures(features);

        PdfVersion current = Version;
        PdfVersion needed = Writing.VersionPlan.Check(current, features, _diagnostics);
        bool changed = Writing.VersionPlan.AddExtensions(Catalog, features, Resolve);
        if (options.IncrementalUpdate && needed > current)
        {
            Catalog[KnownNames.Version] = new CosName(needed.ToString());
            changed = true;
        }

        if (changed)
        {
            // The catalog changed after it was planned: plan again, so the update writes it and a full save serializes it.
            features.Clear();
            writer = PlanWriter(options, features);
            AddDocumentFeatures(features);
        }

        if (!options.IncrementalUpdate)
        {
            writer.Version = Writing.VersionPlan.Highest(features, writer.Version);
        }

        return writer;
    }

    private FileWriter PlanWriter(PdfSaveOptions options, List<VersionedFeature> features) => options.IncrementalUpdate
        ? SavePlan.CreateIncremental(_source, _loader, options, StreamEncoders, Security, features)
        : SavePlan.Create(_source, _loader, Linearization, options, StreamEncoders, Security, features);

    /// <summary>
    /// Adds the features the document itself uses: those its writing APIs recorded (<see cref="RequireFeature"/>), and each developer
    /// extension it declares, which needs its <c>BaseVersion</c> (§7.12.4).
    /// </summary>
    private void AddDocumentFeatures(List<VersionedFeature> features)
    {
        lock (_featureGate)
        {
            features.AddRange(_features);
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
