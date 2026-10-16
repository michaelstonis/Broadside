using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Writing;

/// <summary>
/// Decides the version a save writes from the features it uses: the highest minimum, raised to in lenient mode and refused in
/// strict mode, and the developer extensions its ISO/TS features declare.
/// </summary>
/// <remarks>
/// ADR 0003; ISO 32000-2 §7.5.2 (the version is identified in the header or in the catalog's <c>Version</c> entry), §7.7.2,
/// Table 29 (<c>Version</c>, which an incremental update uses to raise the version, §7.5.6) and §7.12 (developer extensions).
/// </remarks>
internal static class VersionPlan
{
    /// <summary>Returns the highest minimum version of <paramref name="features"/>, or <paramref name="floor"/> when that is later.</summary>
    /// <param name="features">The features the save uses.</param>
    /// <param name="floor">The version to start from.</param>
    /// <returns>The version.</returns>
    public static PdfVersion Highest(IEnumerable<VersionedFeature> features, PdfVersion floor)
    {
        PdfVersion highest = floor;
        foreach (VersionedFeature feature in features)
        {
            if (feature.MinimumVersion > highest)
            {
                highest = feature.MinimumVersion;
            }
        }

        return highest;
    }

    /// <summary>
    /// Checks the version a save needs against the document's: when it is later, records a <c>VersionRaised</c> diagnostic naming
    /// the features that need it (<see cref="DiagnosticSeverity.Information"/>), or, in strict mode, refuses the raise by throwing it.
    /// </summary>
    /// <param name="current">The version the document conforms to.</param>
    /// <param name="features">The features the save uses.</param>
    /// <param name="diagnostics">The document's diagnostics.</param>
    /// <returns>The version the save needs, at least <paramref name="current"/>.</returns>
    /// <exception cref="DiagnosticException">In strict mode, when a feature needs a version later than <paramref name="current"/>.</exception>
    public static PdfVersion Check(PdfVersion current, IReadOnlyCollection<VersionedFeature> features, DiagnosticSink diagnostics)
    {
        PdfVersion needed = Highest(features, current);
        if (needed == current)
        {
            return current;
        }

        string names = string.Join(", ", features
            .Where(feature => feature.MinimumVersion > current)
            .OrderByDescending(feature => feature.MinimumVersion)
            .Select(feature => string.Create(CultureInfo.InvariantCulture, $"{feature} needs PDF {feature.MinimumVersion}"))
            .Distinct(StringComparer.Ordinal));
        if (diagnostics.IsStrict)
        {
            diagnostics.Report(
                DiagnosticCodes.VersionRaised,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The document is PDF {current}, but the save uses features of a later version: {names}. Strict mode does not raise the version (ADR 0003); nothing is written."));
        }
        else
        {
            diagnostics.Report(
                DiagnosticCodes.VersionRaised,
                DiagnosticSeverity.Information,
                string.Create(CultureInfo.InvariantCulture, $"The saved file is PDF {needed}, raised from {current}: {names} (ISO 32000-2 §7.5.2)."));
        }

        return needed;
    }

    /// <summary>
    /// Adds to the catalog's <c>Extensions</c> dictionary the developer extension each ISO/TS feature declares, unless one with the
    /// same prefix and extension level is there already; every other entry is kept (§7.12).
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="features">The features the save uses.</param>
    /// <param name="resolve">Resolves indirect references.</param>
    /// <returns><see langword="true"/> when the catalog or its extensions dictionary changed.</returns>
    /// <remarks>
    /// A new extensions dictionary is direct, with <c>Type</c> <c>Extensions</c>; a prefix's value becomes an array (PDF 2.0) when it
    /// held one dictionary, so the existing declaration is kept beside the new one (§7.12.2, Table 48). An <c>Extensions</c> entry
    /// that is not a dictionary, which a reader ignores, is replaced.
    /// </remarks>
    public static bool AddExtensions(CosDictionary catalog, IEnumerable<VersionedFeature> features, Func<CosObject?, CosObject> resolve)
    {
        bool changed = false;
        foreach (DeveloperExtensionDeclaration declaration in features.Select(feature => feature.Extension).OfType<DeveloperExtensionDeclaration>().Distinct())
        {
            if (resolve(catalog.GetValueOrDefault(KnownNames.Extensions)) is not CosDictionary extensions)
            {
                extensions = new CosDictionary { [KnownNames.Type] = KnownNames.Extensions };
                catalog[KnownNames.Extensions] = extensions;
            }

            CosObject? entry = extensions.GetValueOrDefault(declaration.Prefix);
            switch (resolve(entry))
            {
                case CosArray array:
                    if (!array.Any(item => Declares(resolve(item), declaration, resolve)))
                    {
                        array.Add(declaration.ToDictionary());
                        changed = true;
                    }

                    continue;
                case CosDictionary single when Declares(single, declaration, resolve):
                    continue;
                case CosDictionary:
                    extensions[declaration.Prefix] = new CosArray([entry!, declaration.ToDictionary()]);
                    break;
                default:
                    extensions[declaration.Prefix] = new CosArray([declaration.ToDictionary()]);
                    break;
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>Whether a developer extensions dictionary already declares <paramref name="declaration"/>'s extension level.</summary>
    private static bool Declares(CosObject value, DeveloperExtensionDeclaration declaration, Func<CosObject?, CosObject> resolve) =>
        value is CosDictionary dictionary
        && resolve(dictionary.GetValueOrDefault(KnownNames.ExtensionLevel)) is CosInteger level
        && level.Value == declaration.ExtensionLevel;
}
