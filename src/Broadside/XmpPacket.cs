using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Broadside;

/// <summary>An XMP packet: the properties of one <c>rdf:RDF</c> element, with typed getters for the properties PDF files commonly use.</summary>
/// <remarks>
/// <para>
/// ISO 16684-1 §7. A packet is one <c>rdf:RDF</c> element, optionally inside an <c>x:xmpmeta</c> element and an
/// <c>&lt;?xpacket?&gt;</c> wrapper (§7.3), neither required. Its <c>rdf:Description</c> elements all describe the same resource
/// and their properties are merged; a property written twice keeps its first value. Properties are identified by namespace name and
/// local name (see <see cref="XmpNamespaces"/>), never by prefix. Every form §7.5-7.9 allows is read: simple values as elements
/// or as attributes, structures as nested descriptions, <c>rdf:parseType="Resource"</c> or attributes, arrays as <c>rdf:Bag</c>,
/// <c>rdf:Seq</c> and <c>rdf:Alt</c>, and qualifiers written with <c>rdf:value</c>.
/// </para>
/// <para>
/// Reading (<see cref="TryParse"/>): a leading byte order mark selects UTF-8, UTF-16 or UTF-32 (§7.1; without one, UTF-16 is
/// recognized by the first character and anything else is read as UTF-8); bytes before the first <c>&lt;</c> are skipped; reading
/// stops at the end of the <c>rdf:RDF</c> element, so the packet padding and whatever follows it are never read. The XML is read
/// with document type declarations refused (no entity is ever expanded and nothing outside the packet is fetched) and at most
/// 16 million characters. Malformed XML before the end of <c>rdf:RDF</c> makes the whole packet unreadable.
/// </para>
/// <para>Immutable: a snapshot of the bytes it was read from. Writing XMP is not supported yet.</para>
/// </remarks>
public sealed class XmpPacket
{
    private readonly Dictionary<(string NamespaceName, string Name), XmpProperty> _byName;

    internal XmpPacket(IReadOnlyList<XmpProperty> properties, string? about)
    {
        Properties = properties;
        About = about;
        _byName = new Dictionary<(string, string), XmpProperty>(properties.Count);
        foreach (XmpProperty property in properties)
        {
            _ = _byName.TryAdd((property.NamespaceName, property.Name), property);
        }
    }

    /// <summary>Gets the top-level properties, in the order written.</summary>
    public IReadOnlyList<XmpProperty> Properties { get; }

    /// <summary>Gets the <c>rdf:about</c> attribute of the descriptions (the empty string for a PDF document's own metadata), or <see langword="null"/> when none has one.</summary>
    /// <remarks>ISO 16684-1 §7.4.</remarks>
    public string? About { get; }

    /// <summary>Gets the title: the default alternative of <c>dc:title</c>.</summary>
    /// <remarks>ISO 16684-1 §8.3; ISO 32000-2 §14.3.3 Table 349 maps the Info dictionary's <c>Title</c> to it.</remarks>
    public string? Title => GetLanguageAlternative(XmpNamespaces.DublinCore, "title");

    /// <summary>Gets the description: the default alternative of <c>dc:description</c> (the Info dictionary's <c>Subject</c>).</summary>
    /// <remarks>ISO 16684-1 §8.3; ISO 32000-2 §14.3.3 Table 349.</remarks>
    public string? Description => GetLanguageAlternative(XmpNamespaces.DublinCore, "description");

    /// <summary>Gets the creators, <c>dc:creator</c>, in order (the Info dictionary's <c>Author</c>); empty when absent.</summary>
    /// <remarks>ISO 16684-1 §8.3; ISO 32000-2 §14.3.3 Table 349.</remarks>
    public IReadOnlyList<string> Creators => GetArrayValues(XmpNamespaces.DublinCore, "creator");

    /// <summary>Gets the subjects, <c>dc:subject</c>; empty when absent.</summary>
    /// <remarks>ISO 16684-1 §8.3.</remarks>
    public IReadOnlyList<string> Subjects => GetArrayValues(XmpNamespaces.DublinCore, "subject");

    /// <summary>Gets the media type, <c>dc:format</c>, such as <c>application/pdf</c>.</summary>
    /// <remarks>ISO 16684-1 §8.3.</remarks>
    public string? Format => GetText(XmpNamespaces.DublinCore, "format");

    /// <summary>Gets <c>xmp:CreateDate</c> (the Info dictionary's <c>CreationDate</c>), or <see langword="null"/> when absent or not a date.</summary>
    /// <remarks>ISO 16684-1 §8.4 and §8.2.1.2.</remarks>
    public XmpDate? CreateDate => GetDate(XmpNamespaces.Xmp, "CreateDate");

    /// <summary>Gets <c>xmp:ModifyDate</c> (the Info dictionary's <c>ModDate</c>), or <see langword="null"/> when absent or not a date.</summary>
    /// <remarks>ISO 16684-1 §8.4 and §8.2.1.2.</remarks>
    public XmpDate? ModifyDate => GetDate(XmpNamespaces.Xmp, "ModifyDate");

    /// <summary>Gets <c>xmp:MetadataDate</c>, or <see langword="null"/> when absent or not a date.</summary>
    /// <remarks>ISO 16684-1 §8.4 and §8.2.1.2.</remarks>
    public XmpDate? MetadataDate => GetDate(XmpNamespaces.Xmp, "MetadataDate");

    /// <summary>Gets <c>xmp:CreatorTool</c>, the tool that created the document (the Info dictionary's <c>Creator</c>).</summary>
    /// <remarks>ISO 16684-1 §8.4; ISO 32000-2 §14.3.3 Table 349.</remarks>
    public string? CreatorTool => GetText(XmpNamespaces.Xmp, "CreatorTool");

    /// <summary>Gets <c>pdf:Producer</c> (the Info dictionary's <c>Producer</c>).</summary>
    /// <remarks>ISO 32000-2 §14.3.3 Table 349.</remarks>
    public string? Producer => GetText(XmpNamespaces.Pdf, "Producer");

    /// <summary>Gets <c>pdf:Keywords</c> (the Info dictionary's <c>Keywords</c>).</summary>
    /// <remarks>ISO 32000-2 §14.3.3 Table 349.</remarks>
    public string? Keywords => GetText(XmpNamespaces.Pdf, "Keywords");

    /// <summary>Gets <c>pdf:PDFVersion</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.2.</remarks>
    public string? PdfVersion => GetText(XmpNamespaces.Pdf, "PDFVersion");

    /// <summary>Gets <c>pdf:Trapped</c> (the Info dictionary's <c>Trapped</c>): <c>True</c>, <c>False</c> or <c>Unknown</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3 Table 349.</remarks>
    public string? Trapped => GetText(XmpNamespaces.Pdf, "Trapped");

    /// <summary>Gets the PDF/A part the document claims, <c>pdfaid:part</c>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 19005.</remarks>
    public int? PdfAPart => GetInteger(XmpNamespaces.PdfAIdentification, "part");

    /// <summary>Gets the PDF/A conformance level the document claims, <c>pdfaid:conformance</c>, such as <c>B</c>.</summary>
    /// <remarks>ISO 19005.</remarks>
    public string? PdfAConformance => GetText(XmpNamespaces.PdfAIdentification, "conformance");

    /// <summary>Gets the PDF/UA part the document claims, <c>pdfuaid:part</c>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 14289-1 §5; ISO 14289-2 §6.</remarks>
    public int? PdfUAPart => GetInteger(XmpNamespaces.PdfUAIdentification, "part");

    /// <summary>Gets <c>xmpMM:DocumentID</c>, the identifier of the document across its versions.</summary>
    /// <remarks>ISO 16684-1 §8.6.</remarks>
    public string? DocumentId => GetText(XmpNamespaces.XmpMediaManagement, "DocumentID");

    /// <summary>Gets <c>xmpMM:InstanceID</c>, the identifier of this version of the document.</summary>
    /// <remarks>ISO 16684-1 §8.6.</remarks>
    public string? InstanceId => GetText(XmpNamespaces.XmpMediaManagement, "InstanceID");

    /// <summary>Reads an XMP packet; never throws.</summary>
    /// <param name="data">The packet's bytes, such as the decoded data of a metadata stream.</param>
    /// <param name="packet">The packet, or <see langword="null"/> when the bytes are not one.</param>
    /// <returns><see langword="true"/> when the bytes hold a readable <c>rdf:RDF</c> element.</returns>
    /// <remarks>ISO 16684-1 §7. See the type's remarks for what is accepted.</remarks>
    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out XmpPacket? packet)
    {
        packet = XmpPacketReader.Read(data, report: null);
        return packet is not null;
    }

    /// <summary>Returns the top-level property with the given name.</summary>
    /// <param name="namespaceName">The namespace name, such as <see cref="XmpNamespaces.DublinCore"/>.</param>
    /// <param name="name">The local name, such as <c>title</c>.</param>
    /// <returns>The property, or <see langword="null"/> when the packet does not have it.</returns>
    public XmpProperty? GetProperty(string namespaceName, string name)
    {
        ArgumentNullException.ThrowIfNull(namespaceName);
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault((namespaceName, name));
    }

    /// <summary>Returns the text of a simple property, exactly as written.</summary>
    /// <param name="namespaceName">The namespace name.</param>
    /// <param name="name">The local name.</param>
    /// <returns>The text, or <see langword="null"/> when the property is absent or not simple.</returns>
    public string? GetText(string namespaceName, string name) => GetProperty(namespaceName, name) is { Kind: XmpPropertyKind.Simple } property ? property.Value : null;

    /// <summary>Returns one alternative of a language alternative such as <c>dc:title</c>.</summary>
    /// <param name="namespaceName">The namespace name.</param>
    /// <param name="name">The local name.</param>
    /// <param name="language">The language wanted, such as <c>de</c>; <see langword="null"/> for the default.</param>
    /// <returns>
    /// The item whose <c>xml:lang</c> is <paramref name="language"/> (compared without case), else the <c>x-default</c> item, else the
    /// first item; the value itself when the property is simple; <see langword="null"/> when absent.
    /// </returns>
    /// <remarks>ISO 16684-1 §8.2.2.4.</remarks>
    public string? GetLanguageAlternative(string namespaceName, string name, string? language = null)
    {
        XmpProperty? property = GetProperty(namespaceName, name);
        if (property is null || property.Kind == XmpPropertyKind.Structure)
        {
            return null;
        }

        if (property.Kind == XmpPropertyKind.Simple)
        {
            return property.Value;
        }

        XmpProperty? match = (language is null ? null : FindLanguage(property.Items, language)) ?? FindLanguage(property.Items, "x-default");
        return (match ?? (property.Items.Count > 0 ? property.Items[0] : null))?.Value;
    }

    private static XmpProperty? FindLanguage(IReadOnlyList<XmpProperty> items, string language)
    {
        foreach (XmpProperty item in items)
        {
            if (string.Equals(item.Language, language, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private string[] GetArrayValues(string namespaceName, string name) => GetProperty(namespaceName, name) switch
    {
        null => [],
        { Kind: XmpPropertyKind.Simple, Value: { } value } => [value],
        { } array => [.. array.Items.Where(item => item.Value is not null).Select(item => item.Value!)],
    };

    private XmpDate? GetDate(string namespaceName, string name) => XmpDate.TryParse(GetText(namespaceName, name), out XmpDate date) ? date : null;

    private int? GetInteger(string namespaceName, string name) =>
        int.TryParse(GetText(namespaceName, name)?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : null;
}
