using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Writing;

namespace Broadside;

/// <content>
/// PDF/UA and Well-Tagged PDF output (issues #204, #205): a save with <see cref="PdfSaveOptions.PdfUAConformance"/> or
/// <see cref="PdfSaveOptions.WellTaggedConformance"/> checks the document against the failure conditions of ISO 14289-1 or ISO 14289-2
/// (which WTPDF 1.0 shares), then writes what the file itself must carry. Cited by veraPDF validation-profile rule identifiers (open
/// decision 12); PDF/UA-1's follow the Matterhorn Protocol 1.1.
/// </content>
public sealed partial class PdfDocument
{
    private const string PdfAExtensionNamespace = "http://www.aiim.org/pdfa/ns/extension/";
    private const string PdfASchemaNamespace = "http://www.aiim.org/pdfa/ns/schema#";
    private const string PdfAPropertyNamespace = "http://www.aiim.org/pdfa/ns/property#";

    /// <summary>The year of ISO 14289-2's edition a PDF/UA-2 save identifies, <c>pdfuaid:rev</c> (ISO 14289-2:2024 §5).</summary>
    private const string PdfUA2Revision = "2024";

    private static readonly PdfVersion PdfUA2Version = new(2, 0);
    private static readonly CosName TabsKey = new("Tabs");
    private static readonly CosName AnnotsKey = new("Annots");
    private static readonly CosName StructureOrder = new("S");
    private static readonly CosName DisplayDocTitleKey = new("DisplayDocTitle");

    /// <summary>
    /// The Figure elements auto-tagging gave placeholder alternate text, with that text: while a figure still has it, an accessible
    /// save reports the figure as having none (veraPDF rule 7.3-1), so a placeholder is never taken for a description.
    /// </summary>
    internal Dictionary<CosDictionary, string>? PlaceholderAlternateText { get; set; }

    /// <summary>
    /// The check step of a PDF/UA or Well-Tagged PDF save, on the document as it is (issue #300): reports every failure condition found
    /// as a <c>PdfUANonConformant</c> or <c>WellTaggedNonConformant</c> warning, which strict mode throws before anything is written or
    /// changed. The document is checked once per part of ISO 14289 asked for (WTPDF 1.0 is checked as PDF/UA-2, whose rule numbers it
    /// shares; its reuse level does not require figures' alternate text, rule 8.2.5.28.2-1).
    /// </summary>
    /// <returns>What the save writes: the PDF/UA conformance and the WTPDF levels asked for that no failure condition rules out.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first failure condition.</exception>
    private AccessibleSave CheckAccessibleSave(PdfSaveOptions options)
    {
        PdfUAConformance conformance = options.PdfUAConformance;
        PdfWellTaggedConformance declared = options.WellTaggedConformance;
        IReadOnlyList<PdfAViolation>? part2 = null;
        if (conformance != PdfUAConformance.None)
        {
            IReadOnlyList<PdfAViolation> violations = conformance == PdfUAConformance.PdfUA2 ? part2 = PdfUAChecker.Check(this, 2) : PdfUAChecker.Check(this);
            foreach (PdfAViolation violation in violations)
            {
                ReportAccessibilityNonConformance(conformance, violation);
            }

            conformance = violations.Count == 0 ? conformance : PdfUAConformance.None;
        }

        if (declared != PdfWellTaggedConformance.None)
        {
            part2 ??= PdfUAChecker.Check(this, 2);
            foreach (PdfWellTaggedConformance level in Levels(declared))
            {
                IReadOnlyList<PdfAViolation> violations = [.. part2.Where(violation => level != PdfWellTaggedConformance.Reuse || violation.Rule != "8.2.5.28.2-1")];
                foreach (PdfAViolation violation in violations)
                {
                    ReportWellTaggedNonConformance(level, violation);
                }

                if (violations.Count > 0)
                {
                    declared &= ~level;
                }
            }
        }

        return new AccessibleSave(conformance, declared);
    }

    /// <summary>
    /// Checks the version the save needs against PDF/UA-1's header, <c>%PDF-1.n</c> (veraPDF rule 6.1-1): a feature the save writes
    /// that needs PDF 2.0, such as the PDF 2.0 structure namespace, a PDF/A-4 or a PDF/UA-2 output, cannot conform. PDF/UA-2 and WTPDF
    /// are PDF 2.0 based and have no such limit.
    /// </summary>
    /// <returns>What the save still writes.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for PDF/UA-1, when a feature needs a version later than 1.7.</exception>
    private AccessibleSave CheckAccessibleVersion(VersionedSave planned, AccessibleSave accessible)
    {
        if (accessible.Conformance != PdfUAConformance.PdfUA1)
        {
            return accessible;
        }

        bool conformant = true;
        foreach (VersionedFeature feature in planned.Features.Where(feature => feature.MinimumVersion > PdfA2Version).Distinct())
        {
            ReportAccessibilityNonConformance(PdfUAConformance.PdfUA1, new PdfAViolation("6.1-1", string.Create(CultureInfo.InvariantCulture, $"{feature} needs PDF {feature.MinimumVersion}; a PDF/UA-1 file has a PDF 1.n header"), null));
            conformant = false;
        }

        return conformant ? accessible : accessible with { Conformance = PdfUAConformance.None };
    }

    /// <summary>Records, for a lenient save, each conformance asked for that the check step ruled out (not identified, not declared).</summary>
    private void ReportAccessibilityNotWritten(PdfSaveOptions options, AccessibleSave accessible)
    {
        if (options.PdfUAConformance != PdfUAConformance.None && accessible.Conformance == PdfUAConformance.None)
        {
            string name = Describe(options.PdfUAConformance);
            _diagnostics.Report(
                DiagnosticCodes.PdfUANotIdentified,
                DiagnosticSeverity.Information,
                $"The file is saved without the {name} identification or other PDF/UA changes: the document cannot conform (see the PdfUANonConformant diagnostics).");
        }

        foreach (PdfWellTaggedConformance level in Levels(options.WellTaggedConformance & ~accessible.WellTagged))
        {
            _diagnostics.Report(
                DiagnosticCodes.WellTaggedNotDeclared,
                DiagnosticSeverity.Information,
                $"The file is saved without the PDF Declaration of {Describe(level)}: the document cannot conform (see the WellTaggedNonConformant diagnostics).");
        }
    }

    private void ReportAccessibilityNonConformance(PdfUAConformance conformance, PdfAViolation violation) =>
        _diagnostics.Report(
            DiagnosticCodes.PdfUANonConformant,
            DiagnosticSeverity.Warning,
            $"The file cannot conform to {Describe(conformance)}: {violation.Message} (veraPDF rule {violation.Rule}, {Clause(conformance == PdfUAConformance.PdfUA2 ? "ISO 14289-2" : "ISO 14289-1", violation.Rule)}).",
            objectReference: violation.Reference);

    private void ReportWellTaggedNonConformance(PdfWellTaggedConformance level, PdfAViolation violation) =>
        _diagnostics.Report(
            DiagnosticCodes.WellTaggedNonConformant,
            DiagnosticSeverity.Warning,
            $"The file cannot conform to {Describe(level)}: {violation.Message} (veraPDF rule {violation.Rule}, {Clause("WTPDF 1.0", violation.Rule)}).",
            objectReference: violation.Reference);

    /// <summary>The clause a veraPDF rule identifier cites: <c>ISO 14289-2 clause 8.2.5.25</c>, or ISO/TS 32005's <c>Table 5</c>.</summary>
    private static string Clause(string standard, string rule) =>
        rule.StartsWith("Table 5.", StringComparison.Ordinal) ? "ISO/TS 32005 Table 5" : $"{standard} clause {rule[..rule.LastIndexOf('-')]}";

    private static string Describe(PdfUAConformance conformance) => conformance == PdfUAConformance.PdfUA2 ? "PDF/UA-2" : "PDF/UA-1";

    private static string Describe(PdfWellTaggedConformance level) =>
        level == PdfWellTaggedConformance.Reuse ? "WTPDF 1.0 (conformance level for reuse)" : "WTPDF 1.0 (conformance level for accessibility)";

    private static IEnumerable<PdfWellTaggedConformance> Levels(PdfWellTaggedConformance levels) =>
        new[] { PdfWellTaggedConformance.Reuse, PdfWellTaggedConformance.Accessibility }.Where(level => levels.HasFlag(level));

    /// <summary>
    /// The change step of a conformant PDF/UA or WTPDF save, after the checks (issue #300) and recorded by the save's change log when it
    /// has one (issue #306): <c>MarkInfo</c> <c>Marked</c> true (veraPDF PDF/UA-1 rule 6.2-1, PDF/UA-2 6.2-1), <c>DisplayDocTitle</c>
    /// true (7.1-10, 8.11.2-1), <c>Tabs</c> <c>/S</c> on every page with annotations (7.18.3-1, 8.9.3.3-1) and, unless the PDF/A change
    /// step already wrote it, the XMP packet with the identification and declarations (<see cref="IdentifyAccessibility"/>) and the
    /// title (7.1-8, 8.11.1-1), in the layout of the PDF 1.7 file a PDF/UA-1 save writes or the PDF 2.0 file of PDF/UA-2 and WTPDF.
    /// </summary>
    /// <param name="archival">Whether the PDF/A change step wrote the packet, the PDF/UA identification included.</param>
    /// <param name="accessible">What the save writes.</param>
    /// <returns><see langword="true"/>: the document changed.</returns>
    private bool ApplyAccessibleSave(bool archival, AccessibleSave accessible)
    {
        if (!MarkInfo.Marked)
        {
            MarkInfo.Marked = true;
        }

        CosDictionary? preferences = ViewerPreferences?.Dictionary;
        if (preferences is null)
        {
            preferences = new CosDictionary();
            Catalog[ViewerPreferencesKey] = preferences;
        }

        if (Resolve(preferences.GetValueOrDefault(DisplayDocTitleKey)) is not CosBoolean { Value: true })
        {
            preferences[DisplayDocTitleKey] = CosBoolean.True;
        }

        foreach (PdfPage page in Pages)
        {
            if (Resolve(page.Dictionary.GetValueOrDefault(AnnotsKey)) is CosArray { Count: > 0 }
                && !StructureOrder.Equals(Resolve(page.Dictionary.GetValueOrDefault(TabsKey))))
            {
                page.Dictionary[TabsKey] = StructureOrder;
            }
        }

        if (!archival)
        {
            var writer = new DocumentMetadataWriter(this);
            IdentifyAccessibility(writer, accessible, extensionSchema: false);
            writer.Commit(Now(), createPacket: true, layout: accessible.Conformance == PdfUAConformance.PdfUA1 ? PdfA2Version : PdfUA2Version);
        }

        return true;
    }

    /// <summary>
    /// Adds to a metadata edit the PDF/UA identification, <c>pdfuaid:part</c> 1 (ISO 14289-1 §5) or <c>pdfuaid:part</c> 2 and
    /// <c>pdfuaid:rev</c> 2024 (ISO 14289-2 §5, veraPDF rules 5-1 to 5-5), and the PDF Declaration of each WTPDF level (WTPDF 1.0 §6.1,
    /// rules 6.1.2-1 and 6.1.3-1; declarations the packet already makes are kept). For a file that is also PDF/A-2 or PDF/A-3 it adds
    /// the extension schema describing <c>pdfuaid</c> (ISO 19005-2 6.6.2.3.2, veraPDF rules 6.6.2.3.1-1 and 6.6.2.3.3), unless the
    /// packet's extension schemas already describe the namespace; PDF/A-4 needs none.
    /// </summary>
    private void IdentifyAccessibility(DocumentMetadataWriter writer, AccessibleSave accessible, bool extensionSchema)
    {
        if (accessible.WellTagged != PdfWellTaggedConformance.None)
        {
            DeclareWellTagged(writer, accessible.WellTagged);
        }

        if (accessible.Conformance == PdfUAConformance.None)
        {
            return;
        }

        bool part2 = accessible.Conformance == PdfUAConformance.PdfUA2;
        writer
            .SetProperty(XmpNamespaces.PdfUAIdentification, "part", part2 ? "2" : "1")
            .SetProperty(XmpNamespaces.PdfUAIdentification, "rev", part2 ? PdfUA2Revision : null);
        if (!extensionSchema)
        {
            return;
        }

        XmpProperty? schemas = Metadata?.Packet?.Properties.FirstOrDefault(property => property.NamespaceName == PdfAExtensionNamespace && property.Name == "schemas");
        List<XmpProperty> items = schemas is { Kind: XmpPropertyKind.UnorderedArray } ? [.. schemas.Items] : [];
        if (items.Any(item => item.GetField(PdfASchemaNamespace, "namespaceURI")?.Value == XmpNamespaces.PdfUAIdentification))
        {
            return;
        }

        XmpProperty part = Structure(
            XmpProperty.Simple(PdfAPropertyNamespace, "name", "part"),
            XmpProperty.Simple(PdfAPropertyNamespace, "valueType", "Integer"),
            XmpProperty.Simple(PdfAPropertyNamespace, "category", "internal"),
            XmpProperty.Simple(PdfAPropertyNamespace, "description", "Indicates, which part of ISO 14289 standard is followed"));
        items.Add(Structure(
            XmpProperty.Simple(PdfASchemaNamespace, "schema", "PDF/UA Universal Accessibility Schema"),
            XmpProperty.Simple(PdfASchemaNamespace, "namespaceURI", XmpNamespaces.PdfUAIdentification),
            XmpProperty.Simple(PdfASchemaNamespace, "prefix", "pdfuaid"),
            XmpProperty.Array(PdfASchemaNamespace, "property", XmpPropertyKind.OrderedArray, [part])));
        writer.SetProperty(XmpProperty.Array(PdfAExtensionNamespace, "schemas", XmpPropertyKind.UnorderedArray, items));
    }

    /// <summary>
    /// Adds a PDF Declaration (<c>pdfd:declarations</c>, PDF Declarations §8.1 and §8.2, Table 2) for each WTPDF level not yet declared,
    /// keeping the declarations the packet already makes.
    /// </summary>
    private void DeclareWellTagged(DocumentMetadataWriter writer, PdfWellTaggedConformance levels)
    {
        XmpProperty? existing = Metadata?.Packet?.Properties.FirstOrDefault(property => property.NamespaceName == PdfDeclaration.Namespace && property.Name == "declarations");
        List<XmpProperty> items = existing is { Kind: XmpPropertyKind.UnorderedArray } ? [.. existing.Items] : [];
        HashSet<string> declared = [.. Declarations.Select(declaration => declaration.ConformsTo)];
        foreach (PdfWellTaggedConformance level in Levels(levels))
        {
            string uri = level == PdfWellTaggedConformance.Reuse ? PdfDeclaration.WellTaggedReuse : PdfDeclaration.WellTaggedAccessibility;
            if (declared.Add(uri))
            {
                items.Add(Structure(XmpProperty.Simple(PdfDeclaration.Namespace, "conformsTo", uri)));
            }
        }

        writer.SetProperty(XmpProperty.Array(PdfDeclaration.Namespace, "declarations", XmpPropertyKind.UnorderedArray, items));
    }

    private static XmpProperty Structure(params XmpProperty[] fields) =>
        new(XmpNamespaces.Rdf, "li", XmpPropertyKind.Structure, null, null, [], fields, []);

    /// <summary>What a PDF/UA or WTPDF save writes, once checked: the PDF/UA conformance (or none) and the WTPDF levels it declares.</summary>
    private readonly record struct AccessibleSave(PdfUAConformance Conformance, PdfWellTaggedConformance WellTagged)
    {
        /// <summary>Gets whether the save writes anything: a PDF/UA identification or a WTPDF declaration.</summary>
        public bool Any => Conformance != PdfUAConformance.None || WellTagged != PdfWellTaggedConformance.None;

        /// <summary>Gets whether the save writes a PDF 2.0 based conformance, PDF/UA-2 or WTPDF (<see cref="VersionedFeatures.PdfUA2Output"/>).</summary>
        public bool Pdf20 => Conformance == PdfUAConformance.PdfUA2 || WellTagged != PdfWellTaggedConformance.None;
    }
}
