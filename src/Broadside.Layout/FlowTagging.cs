using System.Collections.Frozen;
using Broadside.Structure;

namespace Broadside.Layout.Flow;

/// <summary>The standard structure namespace a tagged flow writes its standard structure types in.</summary>
/// <remarks>ISO 32000-2 §14.8.6.</remarks>
public enum FlowStructureNamespace
{
    /// <summary>The PDF 2.0 namespace (<c>http://iso.org/pdf2/ssn</c>, Table 364): every element whose type it defines carries <c>NS</c>; the
    /// PDF 1.7-only types (<c>Code</c>, <c>Quote</c>, <c>BlockQuote</c>, ...) stay in the default namespace. The structure tree needs PDF 2.0.</summary>
    Pdf20,

    /// <summary>The PDF 1.7 namespace, the default (<c>http://iso.org/pdf/ssn</c>): no element carries <c>NS</c>, so the tree reads in PDF 1.7
    /// readers and validators.</summary>
    Pdf17,
}

/// <summary>
/// How <see cref="FlowDocument.Render"/> tags what it lays out: a structure tree built while the flow is drawn, one element per block and
/// part, in construction order, with the page content of each tied to it by MCID and everything else (page backgrounds, watermarks,
/// headers and footers, table rules and backgrounds, repeated table headers, underlines) marked as artifacts.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8 (tagged PDF), §14.7 (structure tree), §14.8.2.2 (artifacts), §14.8.4 (standard structure types), §14.8.5.7 (table
/// attributes), §14.9.2 (natural language), §14.3.3 (title). The tree is <c>Document</c>, then a <c>Sect</c> per
/// <see cref="FlowDocument.StartSection"/>, then each block's <see cref="FlowBlock.StructureType"/>: paragraphs (<c>P</c>, <c>H1</c>...),
/// lists (<c>L</c> / <c>LI</c> / <c>Lbl</c> / <c>LBody</c>), tables (<c>Table</c> / <c>THead</c> / <c>TBody</c> / <c>TR</c> / <c>TH</c> /
/// <c>TD</c> with <c>RowSpan</c>, <c>ColSpan</c>, <c>Scope</c> and <c>Headers</c>), figures with <c>Alt</c>, links (<c>Link</c> with the
/// annotation's object reference) and inline spans that carry a language. A paragraph with the type <c>Caption</c> next to a figure or a table
/// becomes that element's caption. A block or part that continues on the next page stays one element.
/// </para>
/// <para>
/// A structure type that is not standard in the chosen namespace is written in the default namespace and role-mapped (§14.8.6.2) to the type
/// given with <see cref="WithRoleMapping"/>, or else to the type the element would have had (a paragraph to <c>P</c>, an image to
/// <c>Figure</c>, a list to <c>L</c>, a table to <c>Table</c>, a run to <c>Span</c> or <c>Link</c>). Instances are immutable.
/// </para>
/// </remarks>
public sealed class FlowTagging
{
    private readonly FrozenDictionary<string, string> _roles;

    private FlowTagging(string? language, string? title, FlowStructureNamespace ns, FrozenDictionary<string, string> roles)
    {
        Language = language;
        Title = title;
        Namespace = ns;
        _roles = roles;
    }

    /// <summary>Gets the default: no document language or title, the PDF 2.0 namespace, no role mappings.</summary>
    public static FlowTagging Default { get; } = new(null, null, FlowStructureNamespace.Pdf20, FrozenDictionary<string, string>.Empty);

    /// <summary>Gets the document's natural language written to the catalog's <c>Lang</c> (a BCP 47 tag), or <see langword="null"/> to
    /// leave the catalog as it is.</summary>
    /// <remarks>ISO 32000-2 §14.9.2.</remarks>
    public string? Language { get; }

    /// <summary>Gets the document title written to the metadata (<c>dc:title</c>), with <c>DisplayDocTitle</c> set in the viewer
    /// preferences, or <see langword="null"/> to leave both as they are.</summary>
    /// <remarks>ISO 32000-2 §14.3.3 and §12.2, Table 147.</remarks>
    public string? Title { get; }

    /// <summary>Gets the namespace the standard structure types are written in.</summary>
    /// <remarks>ISO 32000-2 §14.8.6.</remarks>
    public FlowStructureNamespace Namespace { get; }

    /// <summary>Gets the role mappings from non-standard structure types to standard ones.</summary>
    /// <remarks>ISO 32000-2 §14.8.6.2.</remarks>
    public IReadOnlyDictionary<string, string> RoleMap => _roles;

    /// <summary>Returns a copy with the document language set.</summary>
    /// <param name="language">A BCP 47 language tag, or <see langword="null"/>.</param>
    /// <returns>The new options.</returns>
    public FlowTagging WithLanguage(string? language) => new(language, Title, Namespace, _roles);

    /// <summary>Returns a copy with the document title set.</summary>
    /// <param name="title">The title, or <see langword="null"/>.</param>
    /// <returns>The new options.</returns>
    public FlowTagging WithTitle(string? title) => new(Language, title, Namespace, _roles);

    /// <summary>Returns a copy writing the standard structure types in <paramref name="ns"/>.</summary>
    /// <param name="ns">The namespace.</param>
    /// <returns>The new options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ns"/> is not a defined value.</exception>
    public FlowTagging WithNamespace(FlowStructureNamespace ns)
    {
        if (!Enum.IsDefined(ns))
        {
            throw new ArgumentOutOfRangeException(nameof(ns), ns, "Unknown namespace.");
        }

        return new(Language, Title, ns, _roles);
    }

    /// <summary>Returns a copy that role-maps the custom structure type <paramref name="type"/> to <paramref name="standardType"/>.</summary>
    /// <param name="type">A structure type that is not standard.</param>
    /// <param name="standardType">A type standard in both the PDF 1.7 and the PDF 2.0 namespaces (such as <c>P</c>, <c>H1</c>, <c>Div</c>,
    /// <c>Span</c>, <c>Figure</c>).</param>
    /// <returns>The new options.</returns>
    /// <exception cref="ArgumentException"><paramref name="type"/> is empty or standard, or <paramref name="standardType"/> is not standard in
    /// both namespaces.</exception>
    /// <remarks>ISO 32000-2 §14.8.6.2.</remarks>
    public FlowTagging WithRoleMapping(string type, string standardType)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentException.ThrowIfNullOrEmpty(standardType);
        if (PdfStructureNamespace.Pdf20.IsStandardType(type) || PdfStructureNamespace.Pdf17.IsStandardType(type))
        {
            throw new ArgumentException($"'{type}' is a standard structure type; only custom types are role-mapped.", nameof(type));
        }

        if (!PdfStructureNamespace.Pdf20.IsStandardType(standardType) || !PdfStructureNamespace.Pdf17.IsStandardType(standardType))
        {
            throw new ArgumentException($"'{standardType}' is not a structure type standard in both the PDF 1.7 and PDF 2.0 namespaces.", nameof(standardType));
        }

        var roles = new Dictionary<string, string>(RoleMap, StringComparer.Ordinal) { [type] = standardType };
        return new(Language, Title, Namespace, roles.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
