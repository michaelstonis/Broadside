using System.Globalization;
using System.Text;

namespace Broadside;

/// <summary>An element of the XML an XMP packet is written in: its expanded name, attributes, child elements and text.</summary>
/// <remarks>ISO 16684-1 §7 (XMP is serialized as RDF/XML in XML 1.0); ISO 32000-2 §14.3.2.</remarks>
internal sealed class XmpXmlElement(string namespaceName, string localName, List<XmpXmlAttribute> attributes)
{
    private StringBuilder? _text;

    /// <summary>Gets the namespace name; the empty string for none.</summary>
    public string NamespaceName { get; } = namespaceName;

    /// <summary>Gets the local name.</summary>
    public string LocalName { get; } = localName;

    /// <summary>Gets the attributes in the order written, namespace declarations left out.</summary>
    public List<XmpXmlAttribute> Attributes { get; } = attributes;

    /// <summary>Gets the child elements in the order written.</summary>
    public List<XmpXmlElement> Children { get; } = [];

    /// <summary>Gets the element's own character data (text and CDATA sections, references resolved), in order; what XMP reads as a simple value.</summary>
    public string Text => _text?.ToString() ?? string.Empty;

    /// <summary>Returns the first attribute with the given expanded name.</summary>
    public XmpXmlAttribute? Attribute(string namespaceName, string localName)
    {
        foreach (XmpXmlAttribute attribute in Attributes)
        {
            if (attribute.LocalName == localName && attribute.NamespaceName == namespaceName)
            {
                return attribute;
            }
        }

        return null;
    }

    /// <summary>Returns the first child element with the given expanded name.</summary>
    public XmpXmlElement? Element(string namespaceName, string localName)
    {
        foreach (XmpXmlElement child in Children)
        {
            if (child.LocalName == localName && child.NamespaceName == namespaceName)
            {
                return child;
            }
        }

        return null;
    }

    internal void AppendText(ReadOnlySpan<char> text)
    {
        if (!text.IsEmpty)
        {
            _ = (_text ??= new StringBuilder()).Append(text);
        }
    }
}

/// <summary>An attribute: expanded name and normalized value (XML 1.0 §3.3.3).</summary>
internal sealed record XmpXmlAttribute(string NamespaceName, string LocalName, string Value);

/// <summary>
/// Reads the subset of XML 1.0 with namespaces that XMP packets use (ISO 16684-1 §7.1, §7.3), without <c>System.Xml</c>: elements,
/// attributes, character data, CDATA sections, comments, processing instructions, the XML declaration, character references and
/// the five predefined entities. A document type declaration is an error, so no entity is ever expanded and nothing outside the packet
/// is ever fetched.
/// </summary>
/// <remarks>
/// <para>
/// The input is read as a fragment, the way the packet wrapper's processing instructions surround the <c>x:xmpmeta</c> element: text,
/// comments, processing instructions and elements may follow one another at the top level. Reading stops at the end tag of the first
/// <c>rdf:RDF</c> element, so whatever follows it (padding, the trailer, anything else) is never read. Everything before that end tag
/// must be well-formed: names (XML 1.0 fourth edition character classes), quoting, unique attributes, declared prefixes, the reserved
/// <c>xml</c> and <c>xmlns</c> prefixes and namespaces, <c>xml:space</c> values, matching end tags, legal characters (XML 1.0 §2.2),
/// legal references, no <c>]]&gt;</c> in character data, no <c>--</c> in comments, an XML declaration only at the very start.
/// These are the rules of the BCL reader it replaces, configured as it was (fragment, document type declarations prohibited, no
/// resolver, comments and processing instructions skipped, white-space kept), so a packet reads to the same result.
/// </para>
/// <para>
/// Line ends are normalized before anything else (§2.11) and attribute values as CDATA attributes (§3.3.3): each literal tab or line
/// feed becomes a space, character references are kept as written.
/// </para>
/// <para>ISO 32000-2 §14.3.2.</para>
/// </remarks>
internal sealed class XmpXmlReader
{
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    private readonly string _text;
    private readonly bool _truncated;
    private readonly List<(string Prefix, string NamespaceName)> _bindings = [];
    private readonly List<(string QualifiedName, int Bindings)> _open = [];
    private readonly StringBuilder _value = new();
    private int _position;

    private XmpXmlReader(string text, bool truncated)
    {
        _text = text;
        _truncated = truncated;
    }

    /// <summary>Reads the first <c>rdf:RDF</c> element of <paramref name="text"/>.</summary>
    /// <param name="text">The markup, starting at its first <c>&lt;</c>.</param>
    /// <param name="maxCharacters">How many characters may be read; reading past them is an error.</param>
    /// <param name="error">Why the markup is not well-formed, or <see langword="null"/>.</param>
    /// <returns>The <c>rdf:RDF</c> element; <see langword="null"/> when there is none or the markup before its end is malformed.</returns>
    public static XmpXmlElement? ReadRdf(ReadOnlySpan<char> text, int maxCharacters, out string? error)
    {
        bool truncated = text.Length > maxCharacters;
        if (truncated)
        {
            text = text[..maxCharacters];
        }

        var reader = new XmpXmlReader(NormalizeLineEnds(text), truncated);
        try
        {
            error = null;
            return reader.ReadRdf();
        }
        catch (MalformedException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    /// <summary>Replaces each carriage return and line feed pair, and each other carriage return, by a line feed (XML 1.0 §2.11).</summary>
    private static string NormalizeLineEnds(ReadOnlySpan<char> text)
    {
        if (!text.Contains('\r'))
        {
            return text.ToString();
        }

        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                _ = builder.Append('\n');
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private XmpXmlElement? ReadRdf()
    {
        _bindings.Add(("xml", XmlNamespace));
        _bindings.Add(("xmlns", XmlnsNamespace));
        _bindings.Add((string.Empty, string.Empty));

        // The top level, and the elements around rdf:RDF: checked, not kept.
        while (true)
        {
            if (AtEnd)
            {
                if (_open.Count > 0)
                {
                    throw Fail($"the input ends inside the element {_open[^1].QualifiedName}");
                }

                return _truncated ? throw Fail("the packet is longer than the reader allows") : null;
            }

            if (Current != '<')
            {
                ReadCharacterData(target: null);
                continue;
            }

            switch (Peek(1))
            {
                case '/':
                    ReadEndTag();
                    break;
                case '?':
                    ReadProcessingInstruction();
                    break;
                case '!':
                    ReadMarkupDeclaration(target: null);
                    break;
                default:
                    (XmpXmlElement element, bool empty) = ReadStartTag();
                    if (element.LocalName == "RDF" && element.NamespaceName == XmpNamespaces.Rdf)
                    {
                        if (!empty)
                        {
                            ReadContent(element);
                        }

                        return element;
                    }

                    if (empty)
                    {
                        CloseElement();
                    }

                    break;
            }
        }
    }

    /// <summary>Reads the content of <paramref name="root"/>, whose start tag was just read, through its end tag, keeping every element.</summary>
    private void ReadContent(XmpXmlElement root)
    {
        int depth = _open.Count;
        var elements = new Stack<XmpXmlElement>();
        elements.Push(root);
        while (true)
        {
            if (AtEnd)
            {
                throw Fail($"the input ends inside the element {_open[^1].QualifiedName}");
            }

            XmpXmlElement current = elements.Peek();
            if (Current != '<')
            {
                ReadCharacterData(current);
                continue;
            }

            switch (Peek(1))
            {
                case '/':
                    ReadEndTag();
                    _ = elements.Pop();
                    if (_open.Count < depth)
                    {
                        return;
                    }

                    break;
                case '?':
                    ReadProcessingInstruction();
                    break;
                case '!':
                    ReadMarkupDeclaration(current);
                    break;
                default:
                    (XmpXmlElement element, bool empty) = ReadStartTag();
                    current.Children.Add(element);
                    if (empty)
                    {
                        CloseElement();
                    }
                    else
                    {
                        elements.Push(element);
                    }

                    break;
            }
        }
    }

    private bool AtEnd => _position >= _text.Length;

    private char Current => _text[_position];

    private char Peek(int offset) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private bool StartsWith(string value) => _text.AsSpan(_position).StartsWith(value, StringComparison.Ordinal);

    /// <summary>Reads character data up to the next <c>&lt;</c> or the end, resolving references (XML 1.0 §2.4, §4.1).</summary>
    private void ReadCharacterData(XmpXmlElement? target)
    {
        int run = _position;
        while (!AtEnd)
        {
            char c = Current;
            if (c is '<' or '&' || (c == ']' && StartsWith("]]>")))
            {
                target?.AppendText(_text.AsSpan(run, _position - run));
                if (c == '<')
                {
                    return;
                }

                if (c == ']')
                {
                    throw Fail("']]>' is not allowed in character data");
                }

                ReadReference(target is null ? null : _value.Clear());
                target?.AppendText(_value.ToString());
                run = _position;
                continue;
            }

            _position += CheckCharacter();
        }

        target?.AppendText(_text.AsSpan(run, _position - run));
    }

    /// <summary>Checks the character at the position (a surrogate pair counts as one) and returns its width in chars.</summary>
    private int CheckCharacter()
    {
        char c = Current;
        if (c is '\t' or '\n' or '\r' or (>= ' ' and < '\uD800') or (>= '\uE000' and <= '\uFFFD'))
        {
            return 1;
        }

        if (char.IsHighSurrogate(c) && char.IsLowSurrogate(Peek(1)))
        {
            return 2;
        }

        throw Fail($"the character U+{(int)c:X4} is not allowed in XML");
    }

    /// <summary>Reads <c>&amp;name;</c>, <c>&amp;#n;</c> or <c>&amp;#xh;</c> at the position and appends what it stands for (XML 1.0 §4.1, §4.6).</summary>
    private void ReadReference(StringBuilder? output)
    {
        _position++;
        if (!AtEnd && Current == '#')
        {
            _position++;
            bool hex = Peek(0) == 'x';
            if (hex)
            {
                _position++;
            }

            int start = _position;
            while (!AtEnd && (hex ? char.IsAsciiHexDigit(Current) : char.IsAsciiDigit(Current)))
            {
                _position++;
            }

            if (_position == start || Peek(0) != ';')
            {
                throw Fail(hex ? "invalid hexadecimal character reference" : "invalid decimal character reference");
            }

            ReadOnlySpan<char> digits = _text.AsSpan(start, _position - start);
            _position++;
            if (!int.TryParse(digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                || value > 0x10FFFF)
            {
                throw Fail("a character reference beyond U+10FFFF");
            }

            if (value >= 0x10000)
            {
                _ = output?.Append(char.ConvertFromUtf32(value));
                return;
            }

            char c = (char)value;
            if (!(c is '\t' or '\n' or '\r' or (>= ' ' and < '\uD800') or (>= '\uE000' and <= '\uFFFD')))
            {
                throw Fail($"a reference to the character U+{value:X4}, which is not allowed in XML");
            }

            _ = output?.Append(c);
            return;
        }

        int nameStart = _position;
        if (AtEnd || !XmpXmlNames.IsStartChar(Current))
        {
            throw Fail("an '&' that does not start a reference");
        }

        while (!AtEnd && XmpXmlNames.IsNameChar(Current))
        {
            _position++;
        }

        if (AtEnd || Current != ';')
        {
            throw Fail("an entity reference without ';'");
        }

        string name = _text[nameStart.._position];
        _position++;
        char replacement = name switch
        {
            "amp" => '&',
            "lt" => '<',
            "gt" => '>',
            "quot" => '"',
            "apos" => '\'',
            _ => throw Fail($"a reference to the undeclared entity '{name}'"),
        };
        _ = output?.Append(replacement);
    }

    /// <summary>Reads a comment, a CDATA section, or fails on a document type declaration or any other <c>&lt;!</c> markup.</summary>
    private void ReadMarkupDeclaration(XmpXmlElement? target)
    {
        if (StartsWith("<!--"))
        {
            _position += 4;
            while (true)
            {
                if (AtEnd)
                {
                    throw Fail("the input ends inside a comment");
                }

                if (Current == '-' && Peek(1) == '-')
                {
                    if (Peek(2) != '>')
                    {
                        throw Fail("a comment holds '--'");
                    }

                    _position += 3;
                    return;
                }

                _position += CheckCharacter();
            }
        }

        if (StartsWith("<![CDATA["))
        {
            _position += 9;
            while (true)
            {
                if (AtEnd)
                {
                    throw Fail("the input ends inside a CDATA section");
                }

                if (Current == ']' && StartsWith("]]>"))
                {
                    _position += 3;
                    return;
                }

                int width = CheckCharacter();
                target?.AppendText(_text.AsSpan(_position, width));
                _position += width;
            }
        }

        throw Fail(StartsWith("<!DOCTYPE") ? "a document type declaration, which is not read" : "markup that starts with '<!' and is neither a comment nor a CDATA section");
    }

    /// <summary>Reads a processing instruction (XML 1.0 §2.6), or the XML declaration (§2.8) when it is the first thing in the input.</summary>
    private void ReadProcessingInstruction()
    {
        bool first = _position == 0;
        _position += 2;
        string target = ReadNcName("a processing instruction target");
        if (!AtEnd && Current == ':')
        {
            throw Fail("a processing instruction target holds ':'");
        }

        if (target.Equals("xml", StringComparison.OrdinalIgnoreCase))
        {
            if (!first || target != "xml")
            {
                throw Fail(first ? $"'{target}' is not allowed as a processing instruction target" : "an XML declaration that is not at the start of the input");
            }

            ReadXmlDeclaration();
            return;
        }

        if (StartsWith("?>"))
        {
            _position += 2;
            return;
        }

        if (AtEnd || !IsWhiteSpace(Current))
        {
            throw Fail("a processing instruction target not followed by white-space");
        }

        while (true)
        {
            if (AtEnd)
            {
                throw Fail("the input ends inside a processing instruction");
            }

            if (Current == '?' && Peek(1) == '>')
            {
                _position += 2;
                return;
            }

            _position += CheckCharacter();
        }
    }

    /// <summary>Reads the rest of <c>&lt;?xml version="1.0" encoding="..." standalone="..."?&gt;</c> (XML 1.0 §2.8, §4.3.3, §2.9).</summary>
    private void ReadXmlDeclaration()
    {
        string[] names = ["version", "encoding", "standalone"];
        int next = 0;
        bool any = false;
        while (true)
        {
            bool space = SkipWhiteSpace();
            if (StartsWith("?>"))
            {
                if (!any)
                {
                    break;
                }

                _position += 2;
                return;
            }

            if (!space)
            {
                break;
            }

            int nameStart = _position;
            while (!AtEnd && XmpXmlNames.IsNameChar(Current))
            {
                _position++;
            }

            string name = _text[nameStart.._position];
            int index = Array.IndexOf(names, name, next);
            if (index < 0 || (!any && index != 0))
            {
                break;
            }

            _ = SkipWhiteSpace();
            if (AtEnd || Current != '=')
            {
                break;
            }

            _position++;
            _ = SkipWhiteSpace();
            if (AtEnd || Current is not ('"' or '\''))
            {
                break;
            }

            char quote = Current;
            int valueStart = ++_position;
            // As the BCL reader: a literal of single characters of the Basic Multilingual Plane, no white-space other than the space,
            // and none of the markup characters, whichever quote encloses it.
            while (!AtEnd && Current is (>= ' ' and < '\uD800') or (>= '\uE000' and <= '\uFFFD') && Current is not ('<' or '>' or '&' or '"' or '\''))
            {
                _position++;
            }

            if (AtEnd || Current != quote)
            {
                break;
            }

            ReadOnlySpan<char> value = _text.AsSpan(valueStart, _position - valueStart);
            _position++;
            bool valid = index switch
            {
                0 => value.StartsWith("1.0", StringComparison.Ordinal),
                2 => value is "yes" or "no",
                _ => true,
            };
            if (!valid)
            {
                break;
            }

            any = true;
            next = index + 1;
            if (!AtEnd && !IsWhiteSpace(Current) && !StartsWith("?>"))
            {
                break;
            }
        }

        throw Fail("the XML declaration is not valid");
    }

    /// <summary>Reads a start tag or empty-element tag, resolving names against the namespaces in scope (Namespaces in XML 1.0 §5, §6).</summary>
    private (XmpXmlElement Element, bool Empty) ReadStartTag()
    {
        _position++;
        (string? prefix, string localName) = ReadQualifiedName("an element name");
        string qualifiedName = prefix is null ? localName : $"{prefix}:{localName}";
        var raw = new List<(string? Prefix, string LocalName, string Value)>();
        bool empty;
        while (true)
        {
            bool space = SkipWhiteSpace();
            if (AtEnd)
            {
                throw Fail($"the input ends inside the start tag of {qualifiedName}");
            }

            if (Current == '>')
            {
                _position++;
                empty = false;
                break;
            }

            if (Current == '/')
            {
                if (Peek(1) != '>')
                {
                    throw Fail("'/' not followed by '>'");
                }

                _position += 2;
                empty = true;
                break;
            }

            if (!space)
            {
                throw Fail($"no white-space before an attribute of {qualifiedName}");
            }

            (string? attributePrefix, string attributeName) = ReadQualifiedName("an attribute name");
            foreach ((string? otherPrefix, string otherName, _) in raw)
            {
                if (otherName == attributeName && otherPrefix == attributePrefix)
                {
                    throw Fail($"a duplicate attribute on {qualifiedName}");
                }
            }

            _ = SkipWhiteSpace();
            if (AtEnd || Current != '=')
            {
                throw Fail("an attribute without '='");
            }

            _position++;
            _ = SkipWhiteSpace();
            raw.Add((attributePrefix, attributeName, ReadAttributeValue()));
        }

        // Namespace declarations first: they apply to the element's own name and attributes.
        int bindings = _bindings.Count;
        foreach ((string? attributePrefix, string attributeName, string value) in raw)
        {
            if (attributePrefix == "xmlns")
            {
                Declare(attributeName, value);
            }
            else if (attributePrefix is null && attributeName == "xmlns")
            {
                if (value is XmlNamespace or XmlnsNamespace)
                {
                    throw Fail("the default namespace bound to a reserved namespace");
                }

                _bindings.Add((string.Empty, value));
            }
        }

        var attributes = new List<XmpXmlAttribute>(raw.Count);
        foreach ((string? attributePrefix, string attributeName, string value) in raw)
        {
            if (attributePrefix == "xmlns" || (attributePrefix is null && attributeName == "xmlns"))
            {
                continue;
            }

            string namespaceName = attributePrefix is null ? string.Empty : Resolve(attributePrefix);
            foreach (XmpXmlAttribute other in attributes)
            {
                if (other.LocalName == attributeName && other.NamespaceName == namespaceName)
                {
                    throw Fail($"a duplicate attribute on {qualifiedName}");
                }
            }

            if (namespaceName == XmlNamespace && attributeName == "space" && value.AsSpan().Trim(" \t\n\r") is not ("default" or "preserve"))
            {
                throw Fail("an xml:space value other than 'default' or 'preserve'");
            }

            attributes.Add(new XmpXmlAttribute(namespaceName, attributeName, value));
        }

        _open.Add((qualifiedName, bindings));
        return (new XmpXmlElement(Resolve(prefix ?? string.Empty), localName, attributes), empty);
    }

    private void Declare(string prefix, string namespaceName)
    {
        if (prefix == "xmlns")
        {
            throw Fail("the prefix 'xmlns' declared");
        }

        if (prefix == "xml" ? namespaceName != XmlNamespace : namespaceName is "" or XmlNamespace or XmlnsNamespace)
        {
            throw Fail($"an invalid declaration of the prefix '{prefix}'");
        }

        _bindings.Add((prefix, namespaceName));
    }

    private string Resolve(string prefix)
    {
        for (int i = _bindings.Count - 1; i >= 0; i--)
        {
            if (_bindings[i].Prefix == prefix)
            {
                return _bindings[i].NamespaceName;
            }
        }

        throw Fail($"the undeclared prefix '{prefix}'");
    }

    /// <summary>Reads an end tag, which must close the innermost open element.</summary>
    private void ReadEndTag()
    {
        _position += 2;
        (string? prefix, string localName) = ReadQualifiedName("an end tag name");
        string qualifiedName = prefix is null ? localName : $"{prefix}:{localName}";
        _ = SkipWhiteSpace();
        if (AtEnd || Current != '>')
        {
            throw Fail($"the end tag {qualifiedName} not closed by '>'");
        }

        _position++;
        if (_open.Count == 0)
        {
            throw Fail($"the end tag {qualifiedName} closes no element");
        }

        if (_open[^1].QualifiedName != qualifiedName)
        {
            throw Fail($"the end tag {qualifiedName} does not match the start tag {_open[^1].QualifiedName}");
        }

        CloseElement();
    }

    private void CloseElement()
    {
        _bindings.RemoveRange(_open[^1].Bindings, _bindings.Count - _open[^1].Bindings);
        _open.RemoveAt(_open.Count - 1);
    }

    /// <summary>Reads a quoted attribute value, resolving references and normalizing white-space (XML 1.0 §3.3.3).</summary>
    private string ReadAttributeValue()
    {
        if (AtEnd || Current is not ('"' or '\''))
        {
            throw Fail("an attribute value not in quotes");
        }

        char quote = Current;
        _position++;
        StringBuilder value = _value.Clear();
        while (true)
        {
            if (AtEnd)
            {
                throw Fail("the input ends inside an attribute value");
            }

            char c = Current;
            if (c == quote)
            {
                _position++;
                return value.ToString();
            }

            switch (c)
            {
                case '<':
                    throw Fail("'<' in an attribute value");
                case '&':
                    ReadReference(value);
                    break;
                case '\t' or '\n':
                    _ = value.Append(' ');
                    _position++;
                    break;
                default:
                    int width = CheckCharacter();
                    _ = value.Append(_text.AsSpan(_position, width));
                    _position += width;
                    break;
            }
        }
    }

    /// <summary>Reads <c>NCName</c> or <c>NCName:NCName</c> (Namespaces in XML 1.0 §4).</summary>
    private (string? Prefix, string LocalName) ReadQualifiedName(string what)
    {
        string first = ReadNcName(what);
        if (AtEnd || Current != ':')
        {
            return (null, first);
        }

        _position++;
        string second = ReadNcName(what);
        if (!AtEnd && Current == ':')
        {
            throw Fail($"{what} with more than one ':'");
        }

        return (first, second);
    }

    private string ReadNcName(string what)
    {
        int start = _position;
        if (AtEnd || !XmpXmlNames.IsStartChar(Current))
        {
            throw Fail($"{what} that does not start with a name character");
        }

        _position++;
        while (!AtEnd && XmpXmlNames.IsNameChar(Current))
        {
            _position++;
        }

        return _text[start.._position];
    }

    private bool SkipWhiteSpace()
    {
        int start = _position;
        while (!AtEnd && IsWhiteSpace(Current))
        {
            _position++;
        }

        return _position > start;
    }

    private static bool IsWhiteSpace(char c) => c is ' ' or '\t' or '\n' or '\r';

    private MalformedException Fail(string reason)
    {
        if (_truncated && _position >= _text.Length)
        {
            reason = "the packet is longer than the reader allows";
        }

        int position = Math.Min(_position, _text.Length);
        ReadOnlySpan<char> before = _text.AsSpan(0, position);
        int line = before.Count('\n') + 1;
        int column = position - (before.LastIndexOf('\n') + 1) + 1;
        return new MalformedException($"{reason}, line {line}, position {column}");
    }

    /// <summary>Why the markup is not well-formed; caught by <see cref="ReadRdf(ReadOnlySpan{char}, int, out string?)"/>, never escapes.</summary>
#pragma warning disable CA1064 // Internal control flow inside the reader; never public.
    private sealed class MalformedException : Exception
#pragma warning restore CA1064
    {
        public MalformedException()
        {
        }

        public MalformedException(string message)
            : base(message)
        {
        }

        public MalformedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
