using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Writing;

namespace Broadside;

/// <content>
/// PDF/UA output (issue #204): a save with <see cref="PdfSaveOptions.PdfUAConformance"/> checks the document against the failure
/// conditions of ISO 14289-1, then writes what the file itself must carry. Cited by veraPDF PDF/UA-1 validation-profile rule identifiers
/// (open decision 12), which follow the Matterhorn Protocol 1.1.
/// </content>
public sealed partial class PdfDocument
{
    private const string PdfAExtensionNamespace = "http://www.aiim.org/pdfa/ns/extension/";
    private const string PdfASchemaNamespace = "http://www.aiim.org/pdfa/ns/schema#";
    private const string PdfAPropertyNamespace = "http://www.aiim.org/pdfa/ns/property#";
    private static readonly CosName TabsKey = new("Tabs");
    private static readonly CosName AnnotsKey = new("Annots");
    private static readonly CosName StructureOrder = new("S");
    private static readonly CosName DisplayDocTitleKey = new("DisplayDocTitle");

    /// <summary>
    /// The check step of a PDF/UA save, on the document as it is (issue #300): reports every failure condition found as a
    /// <c>PdfUANonConformant</c> warning, which strict mode throws before anything is written or changed.
    /// </summary>
    /// <returns>Whether the save writes the conformance: one is asked for and no failure condition was found.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first failure condition.</exception>
    private bool CheckAccessibleSave(PdfSaveOptions options)
    {
        if (options.PdfUAConformance == PdfUAConformance.None)
        {
            return false;
        }

        IReadOnlyList<PdfAViolation> violations = PdfUAChecker.Check(this);
        foreach (PdfAViolation violation in violations)
        {
            ReportAccessibilityNonConformance(violation);
        }

        return violations.Count == 0;
    }

    /// <summary>
    /// Checks the version the save needs against PDF/UA-1's header, <c>%PDF-1.n</c> (veraPDF rule 6.1-1): a feature the save writes
    /// that needs PDF 2.0, such as the PDF 2.0 structure namespace, cannot conform.
    /// </summary>
    /// <returns>Whether the save still writes the conformance.</returns>
    /// <exception cref="DiagnosticException">In strict mode, when a feature needs a version later than 1.7.</exception>
    private bool CheckAccessibleVersion(VersionedSave planned)
    {
        bool conformant = true;
        foreach (VersionedFeature feature in planned.Features.Where(feature => feature.MinimumVersion > PdfA2Version).Distinct())
        {
            ReportAccessibilityNonConformance(new PdfAViolation("6.1-1", string.Create(CultureInfo.InvariantCulture, $"{feature} needs PDF {feature.MinimumVersion}; a PDF/UA-1 file has a PDF 1.n header"), null));
            conformant = false;
        }

        return conformant;
    }

    private void ReportAccessibilityNonConformance(PdfAViolation violation) =>
        _diagnostics.Report(
            DiagnosticCodes.PdfUANonConformant,
            DiagnosticSeverity.Warning,
            $"The file cannot conform to PDF/UA-1: {violation.Message} (veraPDF rule {violation.Rule}, ISO 14289-1 clause {violation.Rule[..violation.Rule.LastIndexOf('-')]}).",
            objectReference: violation.Reference);

    /// <summary>Records that a lenient save does not claim the PDF/UA conformance it could not meet.</summary>
    private void ReportAccessibilityNotIdentified() =>
        _diagnostics.Report(
            DiagnosticCodes.PdfUANotIdentified,
            DiagnosticSeverity.Information,
            "The file is saved without the PDF/UA-1 identification or other PDF/UA changes: the document cannot conform (see the PdfUANonConformant diagnostics).");

    /// <summary>
    /// The change step of a conformant PDF/UA-1 save, after the checks (issue #300) and recorded by the save's change log when it has
    /// one (issue #306): <c>MarkInfo</c> <c>Marked</c> true (veraPDF rule 6.2-1), <c>DisplayDocTitle</c> true (7.1-10), <c>Tabs</c>
    /// <c>/S</c> on every page with annotations (7.18.3-1) and, unless the PDF/A change step already wrote it, the XMP packet with
    /// <c>pdfuaid:part</c> 1 (5-1, 5-2) and the title (7.1-8, 7.1-9) in the layout of the PDF 1.7 file the save writes.
    /// </summary>
    /// <param name="archival">Whether the PDF/A change step wrote the packet, the PDF/UA identification included.</param>
    /// <returns><see langword="true"/>: the document changed.</returns>
    private bool ApplyAccessibleSave(bool archival)
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
            IdentifyAccessibility(writer, extensionSchema: false);
            writer.Commit(Now(), createPacket: true, layout: PdfA2Version);
        }

        return true;
    }

    /// <summary>
    /// Adds the PDF/UA identification, <c>pdfuaid:part</c> 1 (ISO 14289-1 §5), to a metadata edit and, for a file that is also PDF/A,
    /// the extension schema describing it (ISO 19005-2 6.6.2.3.2, veraPDF rules 6.6.2.3.1-1 and 6.6.2.3.3), unless the packet's
    /// extension schemas already describe the namespace.
    /// </summary>
    private void IdentifyAccessibility(DocumentMetadataWriter writer, bool extensionSchema)
    {
        writer.SetProperty(XmpNamespaces.PdfUAIdentification, "part", "1");
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

        static XmpProperty Structure(params XmpProperty[] fields) =>
            new(XmpNamespaces.Rdf, "li", XmpPropertyKind.Structure, null, null, [], fields, []);
    }
}
