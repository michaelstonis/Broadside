using System.Text;
using System.Xml;
using System.Xml.Linq;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside;

/// <summary>Reads the bytes of an XMP packet into an <see cref="XmpPacket"/> (ISO 16684-1 §7); never throws.</summary>
/// <remarks>
/// The XML is read by the BCL's <see cref="XmlReader"/> with explicit settings: document type declarations prohibited, no resolver,
/// no entity expansion, a character limit, fragment conformance (the packet wrapper's processing instructions surround the
/// elements). Only the first <c>rdf:RDF</c> element is loaded; reading stops at its end tag.
/// </remarks>
internal static class XmpPacketReader
{
    /// <summary>The most characters a packet may have; a larger one is not read.</summary>
    internal const int MaxCharacters = 16 * 1024 * 1024;

    /// <summary>How deeply property values may nest (structures in arrays in structures ...); deeper values read as empty.</summary>
    private const int MaxDepth = 64;

    private static readonly XNamespace RdfNamespace = XmpNamespaces.Rdf;
    private static readonly XName About = RdfNamespace + "about";
    private static readonly XName ParseType = RdfNamespace + "parseType";
    private static readonly XName Resource = RdfNamespace + "resource";
    private static readonly XName Value = RdfNamespace + "value";
    private static readonly XName Lang = XNamespace.Xml + "lang";

    /// <summary>Reads a packet.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="report">Receives the deviations (code, severity, message); <see langword="null"/> to ignore them.</param>
    /// <returns>The packet, or <see langword="null"/> when the bytes hold no readable <c>rdf:RDF</c> element.</returns>
    public static XmpPacket? Read(ReadOnlySpan<byte> data, Action<string, DiagnosticSeverity, string>? report)
    {
        string text = Decode(data);
        int start = text.IndexOf('<', StringComparison.Ordinal);
        if (start < 0)
        {
            report?.Invoke(DiagnosticCodes.XmpMalformed, DiagnosticSeverity.Error, "The metadata stream holds no XML markup; it is not an XMP packet.");
            return null;
        }

        if (text.AsSpan(0, start).ContainsAnyExcept(" \t\r\n﻿"))
        {
            report?.Invoke(
                DiagnosticCodes.XmpLeadingJunk,
                DiagnosticSeverity.Warning,
                "The XMP packet has bytes other than white-space before its first markup; they are skipped.");
        }

        XElement? rdf;
        try
        {
            rdf = LoadRdf(text, start);
        }
        catch (XmlException exception)
        {
            bool doctype = text.Contains("<!DOCTYPE", StringComparison.Ordinal);
            report?.Invoke(
                doctype ? DiagnosticCodes.XmpDtdProhibited : DiagnosticCodes.XmpMalformed,
                DiagnosticSeverity.Error,
                doctype
                    ? "The XMP packet has a document type declaration, which is not read (it could expand entities or fetch files); the packet is ignored."
                    : $"The XMP packet is not well-formed XML ({exception.Message}); the packet is ignored.");
            return null;
        }

        if (rdf is null)
        {
            report?.Invoke(DiagnosticCodes.XmpMalformed, DiagnosticSeverity.Error, "The XMP packet has no rdf:RDF element; the packet is ignored.");
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

    /// <summary>Loads the first <c>rdf:RDF</c> element, or returns <see langword="null"/> when there is none.</summary>
    private static XElement? LoadRdf(string text, int start)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = MaxCharacters,
            ConformanceLevel = ConformanceLevel.Fragment,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = false,
            CheckCharacters = true,
            CloseInput = true,
        };
        using var input = new StringReader(start == 0 ? text : text[start..]);
        using var reader = XmlReader.Create(input, settings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "RDF" && reader.NamespaceURI == XmpNamespaces.Rdf)
            {
                using XmlReader subtree = reader.ReadSubtree();
                return XElement.Load(subtree, LoadOptions.PreserveWhitespace);
            }
        }

        return null;
    }

    /// <summary>Merges the properties of every description in <c>rdf:RDF</c> (ISO 16684-1 §7.4); the first of two equal names wins.</summary>
    private static XmpPacket Build(XElement rdf, Action<string, DiagnosticSeverity, string>? report)
    {
        var properties = new List<XmpProperty>();
        var names = new HashSet<(string, string)>();
        string? about = null;
        foreach (XElement description in rdf.Elements())
        {
            about ??= description.Attribute(About)?.Value;
            foreach (XmpProperty property in ReadNode(description, depth: 0))
            {
                if (names.Add((property.NamespaceName, property.Name)))
                {
                    properties.Add(property);
                }
                else
                {
                    report?.Invoke(
                        DiagnosticCodes.XmpPropertyDuplicate,
                        DiagnosticSeverity.Warning,
                        $"The XMP property {property.Name} ({property.NamespaceName}) is given more than once; the first value is used.");
                }
            }
        }

        return new XmpPacket(properties, about);
    }

    /// <summary>Reads the properties of a node element: its property attributes, then its property elements (§7.4, §7.9.2.2).</summary>
    private static List<XmpProperty> ReadNode(XElement node, int depth)
    {
        var properties = new List<XmpProperty>();
        foreach (XAttribute attribute in PropertyAttributes(node))
        {
            properties.Add(Simple(attribute.Name, attribute.Value, language: null));
        }

        foreach (XElement element in node.Elements())
        {
            properties.Add(ReadProperty(element, depth + 1));
        }

        return properties;
    }

    /// <summary>Reads one property element in any of the forms of ISO 16684-1 §7.5-7.9.</summary>
    private static XmpProperty ReadProperty(XElement element, int depth)
    {
        XName name = element.Name;
        string? language = element.Attribute(Lang)?.Value;
        if (depth > MaxDepth)
        {
            return Simple(name, string.Empty, language);
        }

        if (element.Attribute(Resource) is { } resource)
        {
            return Simple(name, resource.Value, language);
        }

        string? parseType = element.Attribute(ParseType)?.Value;
        if (parseType == "Resource")
        {
            // §7.9.2.4: the element is itself the structure's (or the qualified value's) description.
            return FromNode(name, element, language, depth);
        }

        XElement? child = element.Elements().FirstOrDefault();
        if (child is null)
        {
            List<XAttribute> fields = [.. PropertyAttributes(element)];
            if (fields.Count > 0 && string.IsNullOrWhiteSpace(element.Value))
            {
                // §7.9.2.5: a structure whose fields are all attributes of the property element.
                return new XmpProperty(Uri(name), name.LocalName, XmpPropertyKind.Structure, null, language, [], [.. fields.Select(field => Simple(field.Name, field.Value, null))], []);
            }

            return Simple(name, element.Value, language);
        }

        if (child.Name.Namespace == RdfNamespace)
        {
            XmpPropertyKind? kind = child.Name.LocalName switch
            {
                "Bag" => XmpPropertyKind.UnorderedArray,
                "Seq" => XmpPropertyKind.OrderedArray,
                "Alt" => XmpPropertyKind.Alternative,
                _ => null,
            };
            if (kind is { } arrayKind)
            {
                XmpProperty[] items = [.. child.Elements(RdfNamespace + "li").Select(item => ReadProperty(item, depth + 1))];
                return new XmpProperty(Uri(name), name.LocalName, arrayKind, null, language, items, [], []);
            }
        }

        // A nested node element: a structure, or a qualified value when it has rdf:value (§7.6, §7.8).
        return FromNode(name, child, language, depth);
    }

    /// <summary>Reads a node element as a structure, or as a qualified value when it has <c>rdf:value</c>.</summary>
    private static XmpProperty FromNode(XName name, XElement node, string? language, int depth)
    {
        if (node.Element(Value) is { } valueElement)
        {
            XmpProperty value = ReadProperty(valueElement, depth + 1);
            List<XmpProperty> qualifiers = [];
            foreach (XAttribute attribute in PropertyAttributes(node))
            {
                qualifiers.Add(Simple(attribute.Name, attribute.Value, null));
            }

            foreach (XElement qualifier in node.Elements())
            {
                if (qualifier != valueElement)
                {
                    qualifiers.Add(ReadProperty(qualifier, depth + 1));
                }
            }

            return new XmpProperty(Uri(name), name.LocalName, value.Kind, value.Value, value.Language ?? language, value.Items, value.Fields, qualifiers);
        }

        return new XmpProperty(Uri(name), name.LocalName, XmpPropertyKind.Structure, null, language, [], ReadNode(node, depth), []);
    }

    /// <summary>The attributes of an element that are properties: not namespace declarations, not RDF or XML syntax.</summary>
    private static IEnumerable<XAttribute> PropertyAttributes(XElement element) =>
        element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration
            && attribute.Name.Namespace != RdfNamespace
            && attribute.Name.Namespace != XNamespace.Xml
            && attribute.Name.Namespace != XNamespace.None);

    private static XmpProperty Simple(XName name, string value, string? language) =>
        new(Uri(name), name.LocalName, XmpPropertyKind.Simple, value, language, [], [], []);

    private static string Uri(XName name) => name.NamespaceName;
}
