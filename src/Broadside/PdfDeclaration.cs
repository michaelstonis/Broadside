namespace Broadside;

/// <summary>
/// A PDF Declaration: a machine-readable assertion, stored in XMP, that the document (or one object) conforms to an external
/// standard or profile, such as ISO/TS 32005 or Well-Tagged PDF.
/// </summary>
/// <remarks>
/// PDF Declarations §7 and §8 (PDF Association, 2019): the <c>pdfd:declarations</c> unordered array (namespace
/// <c>http://pdfa.org/declarations/</c>) in document-level XMP (the catalog's <c>Metadata</c>, <see cref="PdfDocument.Declarations"/>)
/// or object-level XMP (<see cref="PdfObjectMetadata.Declarations"/>), whose scope is that object. There is no catalog key. The
/// <c>https</c> spelling of the namespace, which published documents also use, is read too; URIs are trimmed of XML white space.
/// Exposed, not validated (a PDF/A file also needs an extension schema, §9).
/// </remarks>
public sealed class PdfDeclaration
{
    private static readonly string[] Namespaces = ["http://pdfa.org/declarations/", "https://pdfa.org/declarations/"];

    internal PdfDeclaration(string conformsTo, IReadOnlyList<PdfDeclarationClaim> claims)
    {
        ConformsTo = conformsTo;
        Claims = claims;
    }

    /// <summary>Gets the URI of the standard or profile the declaration asserts conformance with (<c>pdfd:conformsTo</c>), trimmed.</summary>
    /// <remarks>PDF Declarations §8.2.1, Table 2 (required); it mirrors <c>dc:conformsTo</c>.</remarks>
    public string ConformsTo { get; }

    /// <summary>Gets the claims made about the conformance (<c>pdfd:claimData</c>).</summary>
    /// <remarks>PDF Declarations §8.2.1, Table 2 (optional), and §8.2.2, Table 3.</remarks>
    public IReadOnlyList<PdfDeclarationClaim> Claims { get; }

    /// <inheritdoc/>
    public override string ToString() => ConformsTo;

    /// <summary>Reads the declarations of an XMP packet; empty when it has none. Entries without <c>pdfd:conformsTo</c> are skipped.</summary>
    internal static IReadOnlyList<PdfDeclaration> Read(XmpPacket? packet)
    {
        if (packet is null)
        {
            return [];
        }

        var declarations = new List<PdfDeclaration>();
        foreach (string namespaceName in Namespaces)
        {
            if (packet.GetProperty(namespaceName, "declarations") is not { } property)
            {
                continue;
            }

            foreach (XmpProperty item in property.Items)
            {
                if (Field(item, "conformsTo")?.Value?.Trim() is not { Length: > 0 } conformsTo)
                {
                    continue;
                }

                var claims = new List<PdfDeclarationClaim>();
                foreach (XmpProperty claim in Field(item, "claimData")?.Items ?? [])
                {
                    claims.Add(new PdfDeclarationClaim(
                        Field(claim, "claimBy")?.Value?.Trim(),
                        Field(claim, "claimDate")?.Value?.Trim(),
                        Field(claim, "claimCredentials")?.Value?.Trim(),
                        Field(claim, "claimReport")?.Value?.Trim()));
                }

                declarations.Add(new PdfDeclaration(conformsTo, claims));
            }
        }

        return declarations;
    }

    private static XmpProperty? Field(XmpProperty structure, string name)
    {
        foreach (string namespaceName in Namespaces)
        {
            if (structure.GetField(namespaceName, name) is { } field)
            {
                return field;
            }
        }

        return null;
    }
}

/// <summary>One claim about a PDF Declaration: who made it, when, with what credentials, and where the report is.</summary>
/// <remarks>PDF Declarations §8.2.2, Table 3. Every field is optional.</remarks>
public sealed class PdfDeclarationClaim
{
    internal PdfDeclarationClaim(string? claimedBy, string? dateText, string? credentials, string? report)
    {
        ClaimedBy = claimedBy;
        DateText = dateText;
        Credentials = credentials;
        Report = report;
    }

    /// <summary>Gets the organisation, individual or software making the claim (<c>pdfd:claimBy</c>).</summary>
    public string? ClaimedBy { get; }

    /// <summary>Gets the date the claim was made as written (<c>pdfd:claimDate</c>, an XMP date).</summary>
    public string? DateText { get; }

    /// <summary>Gets <see cref="DateText"/> parsed as an XMP date (ISO 16684-1), or <see langword="null"/>.</summary>
    public XmpDate? Date => XmpDate.TryParse(DateText, out XmpDate date) ? date : null;

    /// <summary>Gets the claimant's credentials (<c>pdfd:claimCredentials</c>).</summary>
    public string? Credentials { get; }

    /// <summary>
    /// Gets the URL of a report on the claim (<c>pdfd:claimReport</c>); a fragment such as <c>#ef=report.html</c> names an embedded
    /// file of the document (ISO 32000-2 Annex O).
    /// </summary>
    public string? Report { get; }
}
