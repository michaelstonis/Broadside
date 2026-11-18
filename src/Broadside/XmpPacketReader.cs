using System.Text;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside;

/// <summary>Reads the bytes of an XMP packet into an <see cref="XmpPacket"/> (ISO 16684-1 §7); never throws.</summary>
/// <remarks>
/// <para>
/// The XML is read by <see cref="XmpXmlReader"/>, which reads only the subset XMP uses: document type declarations refused, no
/// entities beyond the predefined ones, no resolver, a character limit, fragment conformance (the packet wrapper's processing
/// instructions surround the elements). Only the first <c>rdf:RDF</c> element is loaded; reading stops at its end tag. It accepts
/// and refuses exactly what the BCL's <c>XmlReader</c> did before (issue #545), without keeping <c>System.Xml</c> in the app.
/// </para>
/// <para>ISO 32000-2 §14.3.2.</para>
/// </remarks>
internal static class XmpPacketReader
{
    /// <summary>The most characters a packet may have; a larger one is not read.</summary>
    internal const int MaxCharacters = 16 * 1024 * 1024;

    /// <summary>How deeply property values may nest (structures in arrays in structures ...); deeper values read as empty.</summary>
    private const int MaxDepth = 64;

    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Reads a packet.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="report">Receives the deviations (code, severity, message); <see langword="null"/> to ignore them.</param>
    /// <returns>The packet, or <see langword="null"/> when the bytes hold no readable <c>rdf:RDF</c> element.</returns>
    public static XmpPacket? Read(ReadOnlySpan<byte> data, Action<DiagnosticCode, DiagnosticSeverity, string>? report)
    {
        string text = Decode(data);
        int start = text.IndexOf('<', StringComparison.Ordinal);
        if (start < 0)
        {
            report?.Invoke(DocumentDiagnostics.XmpMalformed, DiagnosticSeverity.Error, "The metadata stream holds no XML markup; it is not an XMP packet.");
            return null;
        }

        if (text.AsSpan(0, start).ContainsAnyExcept(" \t\r\n﻿"))
        {
            report?.Invoke(
                DocumentDiagnostics.XmpLeadingJunk,
                DiagnosticSeverity.Warning,
                "The XMP packet has bytes other than white-space before its first markup; they are skipped.");
        }

        XmpXmlElement? rdf = XmpXmlReader.ReadRdf(text.AsSpan(start), MaxCharacters, out string? error);
        if (error is not null)
        {
            bool doctype = text.Contains("<!DOCTYPE", StringComparison.Ordinal);
            report?.Invoke(
                doctype ? DocumentDiagnostics.XmpDtdProhibited : DocumentDiagnostics.XmpMalformed,
                DiagnosticSeverity.Error,
                doctype
                    ? "The XMP packet has a document type declaration, which is not read (it could expand entities or fetch files); the packet is ignored."
                    : $"The XMP packet is not well-formed XML ({error}); the packet is ignored.");
            return null;
        }

        if (rdf is null)
        {
            report?.Invoke(DocumentDiagnostics.XmpMalformed, DiagnosticSeverity.Error, "The XMP packet has no rdf:RDF element; the packet is ignored.");
            return null;
        }

        return Build(rdf, report);
    }

    /// <summary>Decodes the bytes by their byte order mark (ISO 16684-1 §7.1), else by the first character, else as UTF-8.</summary>
    private static string Decode(ReadOnlySpan<byte> data)
    {
        Encoding encoding;
        int skip = 0;
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            (encoding, skip) = (Encoding.UTF8, 3);
        }
        else if (data.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        {
            (encoding, skip) = (new UTF32Encoding(bigEndian: true, byteOrderMark: false), 4);
        }
        else if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
        {
            (encoding, skip) = (new UTF32Encoding(bigEndian: false, byteOrderMark: false), 4);
        }
        else if (data.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            (encoding, skip) = (Encoding.BigEndianUnicode, 2);
        }
        else if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            (encoding, skip) = (Encoding.Unicode, 2);
        }
        else if (data.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x00, (byte)'<']))
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: false);
        }
        else if (data.StartsWith((ReadOnlySpan<byte>)[(byte)'<', 0x00, 0x00, 0x00]))
        {
            encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: false);
        }
        else if (data.StartsWith((ReadOnlySpan<byte>)[0x00, (byte)'<']))
        {
            encoding = Encoding.BigEndianUnicode;
        }
        else if (data.StartsWith((ReadOnlySpan<byte>)[(byte)'<', 0x00]))
        {
            encoding = Encoding.Unicode;
        }
        else
        {
            encoding = Encoding.UTF8;
        }

        data = data[skip..];

        // A packet beyond the limit is not decoded whole: the XML reader stops at the limit anyway.
        int maxBytes = encoding.GetMaxByteCount(MaxCharacters + 1);
        return encoding.GetString(data.Length > maxBytes ? data[..maxBytes] : data);
    }

    /// <summary>Merges the properties of every description in <c>rdf:RDF</c> (ISO 16684-1 §7.4); the first of two equal names wins.</summary>
    private static XmpPacket Build(XmpXmlElement rdf, Action<DiagnosticCode, DiagnosticSeverity, string>? report)
    {
        var properties = new List<XmpProperty>();
        var names = new HashSet<(string, string)>();
        string? about = null;
        foreach (XmpXmlElement description in rdf.Children)
        {
            about ??= description.Attribute(XmpNamespaces.Rdf, "about")?.Value;
            foreach (XmpProperty property in ReadNode(description, depth: 0))
            {
                if (names.Add((property.NamespaceName, property.Name)))
                {
                    properties.Add(property);
                }
                else
                {
                    report?.Invoke(
                        DocumentDiagnostics.XmpPropertyDuplicate,
                        DiagnosticSeverity.Warning,
                        $"The XMP property {property.Name} ({property.NamespaceName}) is given more than once; the first value is used.");
                }
            }
        }

        return new XmpPacket(properties, about);
    }

    /// <summary>Reads the properties of a node element: its property attributes, then its property elements (§7.4, §7.9.2.2).</summary>
    private static List<XmpProperty> ReadNode(XmpXmlElement node, int depth)
    {
        var properties = new List<XmpProperty>();
        foreach (XmpXmlAttribute attribute in PropertyAttributes(node))
        {
            properties.Add(Simple(attribute.NamespaceName, attribute.LocalName, attribute.Value, language: null));
        }

        foreach (XmpXmlElement element in node.Children)
        {
            properties.Add(ReadProperty(element, depth + 1));
        }

        return properties;
    }

    /// <summary>Reads one property element in any of the forms of ISO 16684-1 §7.5-7.9.</summary>
    private static XmpProperty ReadProperty(XmpXmlElement element, int depth)
    {
        string? language = element.Attribute(XmlNamespace, "lang")?.Value;
        if (depth > MaxDepth)
        {
            return Simple(element.NamespaceName, element.LocalName, string.Empty, language);
        }

        if (element.Attribute(XmpNamespaces.Rdf, "resource") is { } resource)
        {
            return new XmpProperty(element.NamespaceName, element.LocalName, XmpPropertyKind.Simple, resource.Value, language, [], [], [], isUri: true);
        }

        string? parseType = element.Attribute(XmpNamespaces.Rdf, "parseType")?.Value;
        if (parseType == "Resource")
        {
            // §7.9.2.4: the element is itself the structure's (or the qualified value's) description.
            return FromNode(element, element, language, depth);
        }

        XmpXmlElement? child = element.Children.Count > 0 ? element.Children[0] : null;
        if (child is null)
        {
            List<XmpXmlAttribute> fields = [.. PropertyAttributes(element)];
            string text = element.Text;
            if (fields.Count > 0 && string.IsNullOrWhiteSpace(text))
            {
                // §7.9.2.5: a structure whose fields are all attributes of the property element.
                return new XmpProperty(
                    element.NamespaceName,
                    element.LocalName,
                    XmpPropertyKind.Structure,
                    null,
                    language,
                    [],
                    [.. fields.Select(field => Simple(field.NamespaceName, field.LocalName, field.Value, null))],
                    []);
            }

            return Simple(element.NamespaceName, element.LocalName, text, language);
        }

        if (child.NamespaceName == XmpNamespaces.Rdf)
        {
            XmpPropertyKind? kind = child.LocalName switch
            {
                "Bag" => XmpPropertyKind.UnorderedArray,
                "Seq" => XmpPropertyKind.OrderedArray,
                "Alt" => XmpPropertyKind.Alternative,
                _ => null,
            };
            if (kind is { } arrayKind)
            {
                XmpProperty[] items = [.. child.Children.Where(item => item.LocalName == "li" && item.NamespaceName == XmpNamespaces.Rdf).Select(item => ReadProperty(item, depth + 1))];
                return new XmpProperty(element.NamespaceName, element.LocalName, arrayKind, null, language, items, [], []);
            }
        }

        // A nested node element: a structure, or a qualified value when it has rdf:value (§7.6, §7.8).
        return FromNode(element, child, language, depth);
    }

    /// <summary>Reads a node element as a structure, or as a qualified value when it has <c>rdf:value</c>.</summary>
    private static XmpProperty FromNode(XmpXmlElement name, XmpXmlElement node, string? language, int depth)
    {
        if (node.Element(XmpNamespaces.Rdf, "value") is { } valueElement)
        {
            XmpProperty value = ReadProperty(valueElement, depth + 1);
            List<XmpProperty> qualifiers = [];
            foreach (XmpXmlAttribute attribute in PropertyAttributes(node))
            {
                qualifiers.Add(Simple(attribute.NamespaceName, attribute.LocalName, attribute.Value, null));
            }

            foreach (XmpXmlElement qualifier in node.Children)
            {
                if (!ReferenceEquals(qualifier, valueElement))
                {
                    qualifiers.Add(ReadProperty(qualifier, depth + 1));
                }
            }

            return new XmpProperty(name.NamespaceName, name.LocalName, value.Kind, value.Value, value.Language ?? language, value.Items, value.Fields, qualifiers, value.IsUri);
        }

        return new XmpProperty(name.NamespaceName, name.LocalName, XmpPropertyKind.Structure, null, language, [], ReadNode(node, depth), []);
    }

    /// <summary>The attributes of an element that are properties: not namespace declarations, not RDF or XML syntax.</summary>
    private static IEnumerable<XmpXmlAttribute> PropertyAttributes(XmpXmlElement element) =>
        element.Attributes.Where(attribute => attribute.NamespaceName is not (XmpNamespaces.Rdf or XmlNamespace or ""));

    private static XmpProperty Simple(string namespaceName, string localName, string value, string? language) =>
        new(namespaceName, localName, XmpPropertyKind.Simple, value, language, [], [], []);
}
