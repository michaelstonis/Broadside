using System.Text;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>xmp-xml</c> target (issue #545): the XML reader XMP packets are read with, which replaced <c>System.Xml</c>, over the input
/// decoded as UTF-8 from its first <c>&lt;</c>. Invariants: no exception; a result exactly when no error is reported; reading is
/// deterministic; a kept element has a local name that is an XML name, no attribute that is a namespace declaration, no two attributes
/// with the same expanded name, and character data of legal XML characters only; the reader never reads past the character limit
/// it is given (a smaller limit can only turn a result into an error, never change it).
/// </summary>
/// <remarks>ISO 16684-1 §7.1, §7.3; XML 1.0 §2.2, §2.3; Namespaces in XML 1.0 §3, §6.3; ISO 32000-2 §14.3.2.</remarks>
internal static class XmpXmlTarget
{
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    public static void Target(ReadOnlySpan<byte> data)
    {
        string text = Encoding.UTF8.GetString(data);
        int start = text.IndexOf('<', StringComparison.Ordinal);
        if (start < 0)
        {
            return;
        }

        ReadOnlySpan<char> markup = text.AsSpan(start);
        XmpXmlElement? rdf = XmpXmlReader.ReadRdf(markup, XmpPacketReader.MaxCharacters, out string? error);
        if (rdf is not null && error is not null)
        {
            throw new InvalidOperationException("The reader returned an element and an error.");
        }

        XmpXmlElement? again = XmpXmlReader.ReadRdf(markup, XmpPacketReader.MaxCharacters, out string? errorAgain);
        if (error != errorAgain || Describe(rdf) != Describe(again))
        {
            throw new InvalidOperationException("Reading the same markup twice gave different results.");
        }

        // A limit the markup fits in changes nothing; one it does not fit in may only add an error.
        XmpXmlElement? limited = XmpXmlReader.ReadRdf(markup, markup.Length / 2, out string? limitedError);
        if (limited is not null && (limitedError is not null || Describe(limited) != Describe(rdf)))
        {
            throw new InvalidOperationException("A packet read under a smaller limit must read the same or not at all.");
        }

        if (rdf is not null)
        {
            Check(rdf);
        }
    }

    private static void Check(XmpXmlElement root)
    {
        var pending = new Stack<XmpXmlElement>();
        pending.Push(root);
        while (pending.TryPop(out XmpXmlElement? element))
        {
            if (element.LocalName.Length == 0 || !XmpXmlNames.IsStartChar(element.LocalName[0]) || !element.LocalName.All(XmpXmlNames.IsNameChar))
            {
                throw new InvalidOperationException($"The element name '{element.LocalName}' is not an XML name.");
            }

            var names = new HashSet<(string, string)>();
            foreach (XmpXmlAttribute attribute in element.Attributes)
            {
                if (attribute.NamespaceName == XmlnsNamespace || (attribute.NamespaceName.Length == 0 && attribute.LocalName == "xmlns"))
                {
                    throw new InvalidOperationException("A namespace declaration was kept as an attribute.");
                }

                if (!names.Add((attribute.NamespaceName, attribute.LocalName)))
                {
                    throw new InvalidOperationException($"The attribute {attribute.LocalName} appears twice.");
                }
            }

            string characters = element.Text;
            for (int i = 0; i < characters.Length; i++)
            {
                char c = characters[i];
                bool legal = c is '\t' or '\n' or '\r' or (>= ' ' and < '\uD800') or (>= '' and <= '�')
                    || (char.IsHighSurrogate(c) && i + 1 < characters.Length && char.IsLowSurrogate(characters[++i]));
                if (!legal)
                {
                    throw new InvalidOperationException($"The character data holds U+{(int)c:X4}, which XML does not allow.");
                }
            }

            foreach (XmpXmlElement child in element.Children)
            {
                pending.Push(child);
            }
        }
    }

    private static string Describe(XmpXmlElement? root)
    {
        if (root is null)
        {
            return "null";
        }

        var builder = new StringBuilder();
        var pending = new Stack<XmpXmlElement>();
        pending.Push(root);
        while (pending.TryPop(out XmpXmlElement? element))
        {
            _ = builder.Append('{').Append(element.NamespaceName).Append('}').Append(element.LocalName).Append('|').Append(element.Text).Append('|');
            foreach (XmpXmlAttribute attribute in element.Attributes)
            {
                _ = builder.Append(attribute.NamespaceName).Append(' ').Append(attribute.LocalName).Append('=').Append(attribute.Value).Append(';');
            }

            _ = builder.Append(element.Children.Count).Append('\n');
            for (int i = element.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(element.Children[i]);
            }
        }

        return builder.ToString();
    }
}
