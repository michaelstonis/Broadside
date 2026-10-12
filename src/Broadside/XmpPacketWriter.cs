using System.Text;
using System.Xml;

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
/// </remarks>
internal static class XmpPacketWriter
{
    /// <summary>The packet identifier §7.3.2 fixes.</summary>
    private const string PacketId = "W5M0MpCehiHzreSzNTczkc9d";

    private const int PaddingLines = 20;

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
    };

    /// <summary>Writes the packet.</summary>
    /// <param name="properties">The top-level properties, in order.</param>
    /// <param name="about">The <c>rdf:about</c> of the description; the empty string for a PDF document's own metadata.</param>
    /// <returns>The packet's bytes.</returns>
    public static byte[] Write(IReadOnlyList<XmpProperty> properties, string about)
    {
        Dictionary<string, string> prefixes = AssignPrefixes(properties);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = " ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Entitize,
            ConformanceLevel = ConformanceLevel.Document,
            CheckCharacters = true,
        };
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, settings))
        {
            writer.WriteProcessingInstruction("xpacket", $"begin=\"﻿\" id=\"{PacketId}\"");
            writer.WriteStartElement("x", "xmpmeta", XmpNamespaces.XmpMeta);
            writer.WriteStartElement("rdf", "RDF", XmpNamespaces.Rdf);
            writer.WriteStartElement("rdf", "Description", XmpNamespaces.Rdf);
            writer.WriteAttributeString("rdf", "about", XmpNamespaces.Rdf, Clean(about));
            foreach ((string namespaceName, string prefix) in prefixes)
            {
                writer.WriteAttributeString("xmlns", prefix, null, namespaceName);
            }

            foreach (XmpProperty property in properties.Where(IsProperty))
            {
                WriteProperty(writer, prefixes, property.NamespaceName, property.Name, property);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.Flush();
        }

        // §7.3.3: padding lets the packet be edited in place; whitespace after the root element, then the trailer.
        var padding = new StringBuilder("\n");
        for (int line = 0; line < PaddingLines; line++)
        {
            padding.Append(' ', 99).Append('\n');
        }

        output.Write(Encoding.ASCII.GetBytes(padding.ToString()));
        output.Write("<?xpacket end=\"w\"?>"u8);
        return output.ToArray();
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

        return c is '\t' or '\n' or '\r' or (>= ' ' and < '\uD800') or (>= '' and <= '�');
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
    private static void WriteProperty(XmlWriter writer, Dictionary<string, string> prefixes, string namespaceName, string name, XmpProperty property)
    {
        if (namespaceName.Length == 0)
        {
            writer.WriteStartElement(name);
        }
        else
        {
            writer.WriteStartElement(Prefix(prefixes, namespaceName), name, namespaceName);
        }

        if (property.Language is { } language)
        {
            writer.WriteAttributeString("xml", "lang", null, Clean(language));
        }

        if (property.Qualifiers.Count > 0)
        {
            // §7.8: a qualified value is a description whose rdf:value is the value.
            writer.WriteAttributeString("rdf", "parseType", XmpNamespaces.Rdf, "Resource");
            writer.WriteStartElement("rdf", "value", XmpNamespaces.Rdf);
            WriteValue(writer, prefixes, property);
            writer.WriteEndElement();
            foreach (XmpProperty qualifier in property.Qualifiers.Where(IsProperty))
            {
                WriteProperty(writer, prefixes, qualifier.NamespaceName, qualifier.Name, qualifier);
            }
        }
        else
        {
            WriteValue(writer, prefixes, property);
        }

        writer.WriteFullEndElement();
    }

    /// <summary>Writes the value part of a property onto the open element: text, a resource, fields or an array.</summary>
    private static void WriteValue(XmlWriter writer, Dictionary<string, string> prefixes, XmpProperty property)
    {
        switch (property.Kind)
        {
            case XmpPropertyKind.Simple when property.IsUri:
                writer.WriteAttributeString("rdf", "resource", XmpNamespaces.Rdf, Clean(property.Value ?? string.Empty));
                break;
            case XmpPropertyKind.Simple:
                writer.WriteString(Clean(property.Value ?? string.Empty));
                break;
            case XmpPropertyKind.Structure:
                writer.WriteAttributeString("rdf", "parseType", XmpNamespaces.Rdf, "Resource");
                foreach (XmpProperty field in property.Fields.Where(IsProperty))
                {
                    WriteProperty(writer, prefixes, field.NamespaceName, field.Name, field);
                }

                break;
            default:
                writer.WriteStartElement(
                    "rdf",
                    property.Kind switch
                    {
                        XmpPropertyKind.UnorderedArray => "Bag",
                        XmpPropertyKind.OrderedArray => "Seq",
                        _ => "Alt",
                    },
                    XmpNamespaces.Rdf);
                foreach (XmpProperty item in property.Items)
                {
                    WriteProperty(writer, prefixes, XmpNamespaces.Rdf, "li", item);
                }

                writer.WriteFullEndElement();
                break;
        }
    }

    /// <summary>
    /// Whether a property, field or qualifier can be written: one named in the RDF or XML namespace is RDF syntax (an element such
    /// as <c>rdf:value</c> or <c>rdf:Description</c> where a property belongs), not a property (ISO 16684-1 §7.4), and is left out.
    /// </summary>
    internal static bool IsProperty(XmpProperty property) =>
        property.NamespaceName is not (XmpNamespaces.Rdf or "http://www.w3.org/XML/1998/namespace");

    private static string Prefix(Dictionary<string, string> prefixes, string namespaceName) =>
        namespaceName == XmpNamespaces.Rdf ? "rdf" : namespaceName == "http://www.w3.org/XML/1998/namespace" ? "xml" : prefixes[namespaceName];
}
