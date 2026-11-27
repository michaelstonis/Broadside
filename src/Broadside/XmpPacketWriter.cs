using System.Text;

namespace Broadside;

/// <summary>Serializes XMP properties as a complete packet (ISO 16684-1 §7), the inverse of <see cref="XmpPacketReader"/>.</summary>
/// <remarks>
/// <para>
/// The packet is UTF-8 without a byte order mark: an <c>&lt;?xpacket begin="&#xFEFF;"?&gt;</c> header whose <c>begin</c> attribute
/// is the byte order mark character (§7.3.2), an <c>x:xmpmeta</c> element, one <c>rdf:RDF</c> element holding one
/// <c>rdf:Description</c>, 2 KB of padding and a writable trailer, <c>&lt;?xpacket end="w"?&gt;</c> (§7.3.3).
/// </para>
/// <para>
/// Every property is written in the canonical element form (§7.5-7.9): a simple value as text or, for a URI, <c>rdf:resource</c>;
/// a structure as <c>rdf:parseType="Resource"</c>; arrays as <c>rdf:Bag</c>, <c>rdf:Seq</c> or <c>rdf:Alt</c>; a value with
/// qualifiers other than <c>xml:lang</c> as <c>rdf:parseType="Resource"</c> holding <c>rdf:value</c>. Prefixes are not part of the
/// data model (§6.2): the usual one is used for each namespace this library names, <c>ns1</c>, <c>ns2</c>... for the others, all
/// declared on the description. Properties named in the RDF or XML namespace are syntax, not properties, and are left out. Characters XML 1.0 cannot carry (most C0 controls, unpaired surrogates, U+FFFE, U+FFFF) are left
/// out; a carriage return is written as a character reference so it survives end-of-line normalization.
/// </para>
/// <para>
/// The markup is written directly, without <c>System.Xml</c> (issue #545), byte for byte as the BCL's <c>XmlWriter</c> wrote it
/// before (one space of indentation per level, line feeds, <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c> escaped in text and
/// attributes, <c>"</c>, tab and line feed escaped in attributes).
/// </para>
/// <para>ISO 19005-2 §6.6.</para>
/// <para>ISO 32000-2 §14.3.2.</para>
/// </remarks>
internal static class XmpPacketWriter
{
    /// <summary>The packet identifier §7.3.2 fixes.</summary>
    private const string PacketId = "W5M0MpCehiHzreSzNTczkc9d";

    private const int PaddingLines = 20;

    /// <summary>The namespace of namespace declarations, which no prefix may be declared for.</summary>
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    private static readonly Dictionary<string, string> KnownPrefixes = new(StringComparer.Ordinal)
    {
        [XmpNamespaces.DublinCore] = "dc",
        [XmpNamespaces.Xmp] = "xmp",
        [XmpNamespaces.XmpMediaManagement] = "xmpMM",
        [XmpNamespaces.Pdf] = "pdf",
        [XmpNamespaces.PdfAIdentification] = "pdfaid",
        [XmpNamespaces.PdfUAIdentification] = "pdfuaid",
        ["http://ns.adobe.com/xap/1.0/rights/"] = "xmpRights",
        ["http://ns.adobe.com/xap/1.0/sType/ResourceRef#"] = "stRef",
        ["http://ns.adobe.com/xap/1.0/sType/ResourceEvent#"] = "stEvt",
        ["http://ns.adobe.com/pdfx/1.3/"] = "pdfx",
        ["http://www.aiim.org/pdfa/ns/extension/"] = "pdfaExtension",
        ["http://www.aiim.org/pdfa/ns/schema#"] = "pdfaSchema",
        ["http://www.aiim.org/pdfa/ns/property#"] = "pdfaProperty",
        ["urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#"] = "fx",
    };

    /// <summary>Writes the packet.</summary>
    /// <param name="properties">The top-level properties, in order.</param>
    /// <param name="about">The <c>rdf:about</c> of the description; the empty string for a PDF document's own metadata.</param>
    /// <returns>The packet's bytes.</returns>
    public static byte[] Write(IReadOnlyList<XmpProperty> properties, string about)
    {
        Dictionary<string, string> prefixes = AssignPrefixes(properties);
        var writer = new MarkupWriter();
        writer.Raw($"<?xpacket begin=\"\uFEFF\" id=\"{PacketId}\"?>");
        writer.StartElement("x:xmpmeta");
        writer.Attribute("xmlns:x", XmpNamespaces.XmpMeta);
        writer.StartElement("rdf:RDF");
        writer.Attribute("xmlns:rdf", XmpNamespaces.Rdf);
        writer.StartElement("rdf:Description");
        writer.Attribute("rdf:about", Clean(about));
        foreach ((string namespaceName, string prefix) in prefixes)
        {
            writer.Attribute($"xmlns:{prefix}", namespaceName);
        }

        foreach (XmpProperty property in properties.Where(IsProperty))
        {
            WriteProperty(writer, prefixes, property.NamespaceName, property.Name, property);
        }

        writer.EndElement(full: false);
        writer.EndElement(full: false);
        writer.EndElement(full: false);

        // §7.3.3: padding lets the packet be edited in place; whitespace after the root element, then the trailer.
        writer.Raw("\n");
        for (int line = 0; line < PaddingLines; line++)
        {
            writer.Raw(new string(' ', 99));
            writer.Raw("\n");
        }

        writer.Raw("<?xpacket end=\"w\"?>");
        return writer.ToBytes();
    }

    /// <summary>Removes the characters XML 1.0 cannot carry (§2.2 of XML 1.0): they have no representation, not even a reference.</summary>
    internal static string Clean(string text)
    {
        int index = 0;
        while (index < text.Length && IsXmlCharacter(text, index, out int width))
        {
            index += width;
        }

        if (index == text.Length)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, index);
        while (index < text.Length)
        {
            if (IsXmlCharacter(text, index, out int width))
            {
                builder.Append(text, index, width);
            }

            index += width;
        }

        return builder.ToString();
    }

    private static bool IsXmlCharacter(string text, int index, out int width)
    {
        char c = text[index];
        width = 1;
        if (char.IsHighSurrogate(c))
        {
            if (index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                width = 2;
                return true;
            }

            return false;
        }

        return c is '\t' or '\n' or '\r' or (>= ' ' and < '\uD800') or (>= '\uE000' and <= '\uFFFD');
    }

    private static Dictionary<string, string> AssignPrefixes(IReadOnlyList<XmpProperty> properties)
    {
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal) { "x", "rdf", "xml", "xmlns" };
        int next = 1;
        Visit(properties);
        return prefixes;

        void Visit(IReadOnlyList<XmpProperty> list)
        {
            foreach (XmpProperty property in list)
            {
                if (property.NamespaceName.Length > 0
                    && property.NamespaceName != XmpNamespaces.Rdf
                    && property.NamespaceName != "http://www.w3.org/XML/1998/namespace"
                    && property.NamespaceName != XmlnsNamespace
                    && !prefixes.ContainsKey(property.NamespaceName))
                {
                    if (!KnownPrefixes.TryGetValue(property.NamespaceName, out string? prefix) || used.Contains(prefix))
                    {
                        do
                        {
                            prefix = $"ns{next++}";
                        }
                        while (used.Contains(prefix));
                    }

                    used.Add(prefix);
                    prefixes[property.NamespaceName] = prefix;
                }

                Visit(property.Items);
                Visit(property.Fields);
                Visit(property.Qualifiers);
            }
        }
    }

    /// <summary>Writes one property element (or an array item, named <c>rdf:li</c>).</summary>
    private static void WriteProperty(MarkupWriter writer, Dictionary<string, string> prefixes, string namespaceName, string name, XmpProperty property)
    {
        string qualifiedName = namespaceName.Length == 0 ? name : $"{Prefix(prefixes, namespaceName)}:{name}";
        writer.StartElement(qualifiedName);
        if (property.Language is { } language)
        {
            writer.Attribute("xml:lang", Clean(language));
        }

        if (property.Qualifiers.Count > 0)
        {
            // §7.8: a qualified value is a description whose rdf:value is the value.
            writer.Attribute("rdf:parseType", "Resource");
            writer.StartElement("rdf:value");
            WriteValue(writer, prefixes, property);
            writer.EndElement(full: false);
            foreach (XmpProperty qualifier in property.Qualifiers.Where(IsProperty))
            {
                WriteProperty(writer, prefixes, qualifier.NamespaceName, qualifier.Name, qualifier);
            }
        }
        else
        {
            WriteValue(writer, prefixes, property);
        }

        writer.EndElement(full: true);
    }

    /// <summary>Writes the value part of a property onto the open element: text, a resource, fields or an array.</summary>
    private static void WriteValue(MarkupWriter writer, Dictionary<string, string> prefixes, XmpProperty property)
    {
        switch (property.Kind)
        {
            case XmpPropertyKind.Simple when property.IsUri:
                writer.Attribute("rdf:resource", Clean(property.Value ?? string.Empty));
                break;
            case XmpPropertyKind.Simple:
                writer.Text(Clean(property.Value ?? string.Empty));
                break;
            case XmpPropertyKind.Structure:
                writer.Attribute("rdf:parseType", "Resource");
                foreach (XmpProperty field in property.Fields.Where(IsProperty))
                {
                    WriteProperty(writer, prefixes, field.NamespaceName, field.Name, field);
                }

                break;
            default:
                writer.StartElement(property.Kind switch
                {
                    XmpPropertyKind.UnorderedArray => "rdf:Bag",
                    XmpPropertyKind.OrderedArray => "rdf:Seq",
                    _ => "rdf:Alt",
                });
                foreach (XmpProperty item in property.Items)
                {
                    WriteProperty(writer, prefixes, XmpNamespaces.Rdf, "li", item);
                }

                writer.EndElement(full: true);
                break;
        }
    }

    /// <summary>
    /// Whether a property, field or qualifier can be written: one named in the RDF or XML namespace is RDF syntax (an element such
    /// as <c>rdf:value</c> or <c>rdf:Description</c> where a property belongs), not a property (ISO 16684-1 §7.4), and is left out.
    /// So is one named in the namespace of namespace declarations (an element such as <c>xmlns:a</c>, which XML reads but no prefix
    /// may be declared for, Namespaces in XML 1.0 §3).
    /// </summary>
    internal static bool IsProperty(XmpProperty property) =>
        property.NamespaceName is not (XmpNamespaces.Rdf or "http://www.w3.org/XML/1998/namespace" or XmlnsNamespace);

    private static string Prefix(Dictionary<string, string> prefixes, string namespaceName) =>
        namespaceName == XmpNamespaces.Rdf ? "rdf" : namespaceName == "http://www.w3.org/XML/1998/namespace" ? "xml" : prefixes[namespaceName];

    /// <summary>
    /// Writes elements, attributes and text as the BCL's indenting <c>XmlWriter</c> did with the settings this packet used: each start
    /// tag on a new line indented one space per level, unless the parent already holds text; an end tag on a new line only after
    /// child elements; an element with nothing in it closed as <c>&lt;a&gt;&lt;/a&gt;</c> when <c>full</c>, else <c>&lt;a /&gt;</c>.
    /// </summary>
    private sealed class MarkupWriter
    {
        private readonly StringBuilder _output = new();
        private readonly Stack<(string Name, bool Mixed, bool HasElements)> _open = new();
        private bool _startTagOpen;

        public void Raw(string text) => _output.Append(text);

        public void StartElement(string qualifiedName)
        {
            bool mixed = _open.TryPeek(out (string Name, bool Mixed, bool HasElements) parent) && parent.Mixed;
            CloseStartTag();
            if (_open.Count > 0)
            {
                (string name, bool parentMixed, _) = _open.Pop();
                _open.Push((name, parentMixed, true));
            }

            if (!mixed && _output.Length > 0)
            {
                _ = _output.Append('\n').Append(' ', _open.Count);
            }

            _ = _output.Append('<').Append(qualifiedName);
            _open.Push((qualifiedName, mixed, false));
            _startTagOpen = true;
        }

        public void Attribute(string qualifiedName, string value)
        {
            _ = _output.Append(' ').Append(qualifiedName).Append("=\"");
            foreach (char c in value)
            {
                _ = c switch
                {
                    '&' => _output.Append("&amp;"),
                    '<' => _output.Append("&lt;"),
                    '>' => _output.Append("&gt;"),
                    '"' => _output.Append("&quot;"),
                    '\t' => _output.Append("&#x9;"),
                    '\n' => _output.Append("&#xA;"),
                    '\r' => _output.Append("&#xD;"),
                    _ => _output.Append(c),
                };
            }

            _ = _output.Append('"');
        }

        public void Text(string text)
        {
            CloseStartTag();
            (string name, _, bool hasElements) = _open.Pop();
            _open.Push((name, true, hasElements));
            foreach (char c in text)
            {
                _ = c switch
                {
                    '&' => _output.Append("&amp;"),
                    '<' => _output.Append("&lt;"),
                    '>' => _output.Append("&gt;"),
                    '\r' => _output.Append("&#xD;"),
                    _ => _output.Append(c),
                };
            }
        }

        public void EndElement(bool full)
        {
            (string name, bool mixed, bool hasElements) = _open.Pop();
            if (_startTagOpen)
            {
                _startTagOpen = false;
                _ = full ? _output.Append("></").Append(name).Append('>') : _output.Append(" />");
                return;
            }

            if (!mixed && hasElements)
            {
                _ = _output.Append('\n').Append(' ', _open.Count);
            }

            _ = _output.Append("</").Append(name).Append('>');
        }

        public byte[] ToBytes() => Encoding.UTF8.GetBytes(_output.ToString());

        private void CloseStartTag()
        {
            if (_startTagOpen)
            {
                _ = _output.Append('>');
                _startTagOpen = false;
            }
        }
    }
}
