using System.IO.Compression;
using System.Text;
using Broadside.Css;

namespace Broadside.Graphics.Svg;

/// <summary>An SVG element: its local name, attributes, child elements in order and, for text content, its character data in order with them.</summary>
/// <remarks>SVG 1.1 §1.6 (terms). Elements of other namespaces are not kept; attributes of other namespaces are not kept, except <c>xlink:href</c> (as <c>href</c>, when no plain <c>href</c> is set) and <c>xml:space</c>.</remarks>
internal sealed class SvgElement : ICssElement
{
    private readonly Dictionary<string, string> _attributes;
    private Dictionary<string, string>? _specified;

    public SvgElement(string name, Dictionary<string, string> attributes, SvgElement? parent)
    {
        Name = name;
        _attributes = attributes;
        Parent = parent;
    }

    /// <summary>Gets the local name.</summary>
    public string Name { get; }

    /// <summary>Gets the parent element, or <see langword="null"/> for the root.</summary>
    public SvgElement? Parent { get; }

    /// <summary>Gets the child elements.</summary>
    public List<SvgElement> Children { get; } = [];

    /// <summary>Gets the character data and child elements in document order: a run is a <see cref="string"/> or an <see cref="SvgElement"/>.</summary>
    public List<object> Content { get; } = [];

    /// <summary>Gets the element before this one among its parent's children, or <see langword="null"/>.</summary>
    public SvgElement? PreviousSibling { get; set; }

    /// <summary>Gets the concatenated character data (for <c>style</c>).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets the id, or <see langword="null"/>.</summary>
    public string? Id => Attribute("id");

    /// <summary>Gets the specified values of the styling properties after the cascade: presentation attributes, then style sheet rules by specificity, then the <c>style</c> attribute.</summary>
    public IReadOnlyDictionary<string, string> Specified => _specified ?? (IReadOnlyDictionary<string, string>)_attributes;

    string ICssElement.LocalName => Name;

    ICssElement? ICssElement.ParentElement => Parent;

    ICssElement? ICssElement.PreviousElementSibling => PreviousSibling;

    /// <summary>Returns the value of an attribute, or <see langword="null"/>.</summary>
    public string? Attribute(string name) => _attributes.TryGetValue(name, out string? value) ? value : null;

    string? ICssElement.GetAttribute(string name) => Attribute(name);

    /// <summary>Sets the specified values the cascade found.</summary>
    internal void SetSpecified(Dictionary<string, string> specified) => _specified = specified;

    /// <inheritdoc/>
    public override string ToString() => Id is { } id ? $"<{Name} id=\"{id}\">" : $"<{Name}>";
}

/// <summary>
/// A parsed SVG document: the element tree from the root <c>svg</c> element, the elements by id, and the cascade of the style sheets
/// of its <c>style</c> elements and the <c>style</c> attributes onto every element.
/// </summary>
/// <remarks>
/// <para>
/// SVG 1.1 §5 (document structure) and §6 (styling): presentation attributes have the lowest priority, then the author style sheet,
/// then the <c>style</c> attribute; an <c>!important</c> sheet declaration wins over a normal <c>style</c> attribute one (CSS 2.1 §6.4).
/// </para>
/// <para>
/// The markup is read by the managed XML reader the XMP packets use, so a document type declaration is refused, no entity is
/// expanded and nothing outside the bytes is ever read. Gzip-compressed input (SVGZ) is decompressed first, up to a size limit.
/// </para>
/// </remarks>
internal sealed class SvgDocument
{
    /// <summary>The namespace of SVG elements.</summary>
    public const string SvgNamespace = "http://www.w3.org/2000/svg";

    private const string XLinkNamespace = "http://www.w3.org/1999/xlink";
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    /// <summary>The largest decompressed SVGZ, and the largest markup in characters: 64 MiB.</summary>
    private const int MaxSize = 64 << 20;

    /// <summary>The deepest element nesting kept; deeper elements are dropped (<see cref="Truncated"/>), so nothing recurses without bound.</summary>
    internal const int MaxDepth = 128;

    private static readonly HashSet<string> StyleProperties =
    [
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
        "stroke-dasharray", "stroke-dashoffset", "stroke-opacity", "opacity", "clip-path", "clip-rule", "mask", "display", "visibility",
        "color", "font-family", "font-size", "font-weight", "font-style", "font", "text-anchor", "mix-blend-mode", "stop-color",
        "stop-opacity", "overflow", "mask-type", "isolation", "filter", "marker", "marker-start", "marker-mid", "marker-end",
        "letter-spacing", "word-spacing", "text-decoration", "paint-order", "vector-effect",
    ];

    private readonly Dictionary<string, SvgElement> _ids = new(StringComparer.Ordinal);

    private SvgDocument(SvgElement root) => Root = root;

    /// <summary>Gets the root <c>svg</c> element.</summary>
    public SvgElement Root { get; }

    /// <summary>Gets a value indicating whether elements nested deeper than <see cref="MaxDepth"/> were dropped.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Returns the element with <paramref name="id"/>, or <see langword="null"/>.</summary>
    public SvgElement? Find(string id) => _ids.TryGetValue(id, out SvgElement? element) ? element : null;

    /// <summary>Parses SVG or SVGZ bytes.</summary>
    /// <param name="bytes">The document: UTF-8 (with or without a byte order mark), UTF-16 with a byte order mark, or gzip-compressed.</param>
    /// <param name="onStyleError">Receives a description of each style sheet part that is skipped.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentException">The bytes are not well-formed XML, have a document type declaration, or have no <c>svg</c> element.</exception>
    public static SvgDocument Parse(ReadOnlySpan<byte> bytes, Action<string>? onStyleError)
    {
        byte[]? inflated = null;
        if (bytes is [0x1F, 0x8B, ..])
        {
            inflated = Inflate(bytes);
            bytes = inflated;
        }

        string text = Decode(bytes);
        XmpXmlElement? root = XmpXmlReader.ReadElement(text, MaxSize, null, "svg", keepTextRuns: true, out string? error);
        if (error is not null)
        {
            throw new ArgumentException($"The SVG is not well-formed XML: {error}.", nameof(bytes));
        }

        if (root is null || root.NamespaceName is not (SvgNamespace or ""))
        {
            throw new ArgumentException("The input has no svg root element.", nameof(bytes));
        }

        bool truncated = false;
        var document = new SvgDocument(Convert(root, null, 0, ref truncated)!) { Truncated = truncated };
        document.Index(document.Root);
        document.Cascade(onStyleError);
        return document;
    }

    /// <summary>Returns whether <paramref name="bytes"/> looks like SVG or SVGZ: a gzip signature, or markup starting with <c>&lt;svg</c> or <c>&lt;?xml</c> after a byte order mark and white space.</summary>
    public static bool Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes is [0x1F, 0x8B, ..])
        {
            return true;
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            bytes = bytes[3..];
        }
        else if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
        {
            return true;
        }

        int i = 0;
        while (i < bytes.Length && bytes[i] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r')
        {
            i++;
        }

        ReadOnlySpan<byte> rest = bytes[i..];
        if (rest.StartsWith("<svg"u8) || rest.StartsWith("<?xml"u8))
        {
            return true;
        }

        // An XML comment before the root, as editors write.
        return rest.StartsWith("<!--"u8) && rest[..Math.Min(rest.Length, 4096)].IndexOf("<svg"u8) >= 0;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> bytes)
    {
        using var input = new MemoryStream(bytes.ToArray(), writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        try
        {
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxSize)
                {
                    throw new ArgumentException("The SVGZ decompresses to more than 64 MiB.", nameof(bytes));
                }

                output.Write(buffer, 0, read);
            }
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException("The SVGZ is not valid gzip data.", nameof(bytes), exception);
        }

        return output.ToArray();
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes is [0xFF, 0xFE, ..])
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes is [0xFE, 0xFF, ..])
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            bytes = bytes[3..];
        }

        // An encoding declaration other than UTF-8 names a single-byte encoding: Latin-1 is the one exported SVG uses.
        ReadOnlySpan<byte> head = bytes[..Math.Min(bytes.Length, 200)];
        if (head.StartsWith("<?xml"u8))
        {
            int end = head.IndexOf("?>"u8);
            string declaration = Encoding.ASCII.GetString(end < 0 ? head : head[..end]).ToLowerInvariant();
            if (declaration.Contains("iso-8859-1", StringComparison.Ordinal) || declaration.Contains("latin1", StringComparison.Ordinal)
                || declaration.Contains("windows-1252", StringComparison.Ordinal))
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static SvgElement? Convert(XmpXmlElement source, SvgElement? parent, int depth, ref bool truncated)
    {
        if (source.NamespaceName is not (SvgNamespace or ""))
        {
            return null;
        }

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        string? xlinkHref = null;
        foreach (XmpXmlAttribute attribute in source.Attributes)
        {
            if (attribute.NamespaceName.Length == 0)
            {
                attributes[attribute.LocalName] = attribute.Value;
            }
            else if (attribute.NamespaceName == XLinkNamespace && attribute.LocalName == "href")
            {
                xlinkHref = attribute.Value;
            }
            else if (attribute.NamespaceName == XmlNamespace && attribute.LocalName == "space")
            {
                attributes["xml:space"] = attribute.Value.Trim();
            }
        }

        if (xlinkHref is not null)
        {
            attributes.TryAdd("href", xlinkHref);
        }

        var element = new SvgElement(source.LocalName, attributes, parent) { Text = source.Text };
        IReadOnlyList<(int Before, string Text)> runs = source.TextRuns;
        int run = 0;
        SvgElement? previous = null;
        for (int index = 0; index <= source.Children.Count; index++)
        {
            while (run < runs.Count && runs[run].Before == index)
            {
                element.Content.Add(runs[run].Text);
                run++;
            }

            if (index < source.Children.Count && depth >= MaxDepth)
            {
                truncated = true;
                continue;
            }

            if (index < source.Children.Count && Convert(source.Children[index], element, depth + 1, ref truncated) is { } child)
            {
                child.PreviousSibling = previous;
                previous = child;
                element.Children.Add(child);
                element.Content.Add(child);
            }
        }

        return element;
    }

    private void Index(SvgElement element)
    {
        if (element.Id is { Length: > 0 } id)
        {
            _ids.TryAdd(id, element);
        }

        foreach (SvgElement child in element.Children)
        {
            Index(child);
        }
    }

    private void Cascade(Action<string>? onError)
    {
        var sheet = new CssStyleSheet();
        CollectStyles(Root, sheet, onError);
        Apply(Root, sheet, onError);
    }

    private static void CollectStyles(SvgElement element, CssStyleSheet sheet, Action<string>? onError)
    {
        if (element.Name == "style")
        {
            string? type = element.Attribute("type");
            if (type is null || type.Length == 0 || type.Equals("text/css", StringComparison.OrdinalIgnoreCase))
            {
                sheet.Add(element.Text, onError);
            }

            return;
        }

        foreach (SvgElement child in element.Children)
        {
            CollectStyles(child, sheet, onError);
        }
    }

    private static void Apply(SvgElement element, CssStyleSheet sheet, Action<string>? onError)
    {
        string? inline = element.Attribute("style");
        List<CssRule>? matched = null;
        foreach (CssRule rule in sheet.Rules)
        {
            if (rule.Selector.Matches(element))
            {
                (matched ??= []).Add(rule);
            }
        }

        if (inline is not null || matched is not null)
        {
            var specified = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in StyleProperties)
            {
                if (element.Attribute(name) is { } value)
                {
                    specified[name] = value;
                }
            }

            var important = new HashSet<string>(StringComparer.Ordinal);
            if (matched is not null)
            {
                matched.Sort(static (a, b) => a.Selector.Specificity != b.Selector.Specificity
                    ? a.Selector.Specificity.CompareTo(b.Selector.Specificity)
                    : a.Order.CompareTo(b.Order));
                foreach (CssRule rule in matched)
                {
                    foreach (CssDeclaration declaration in rule.Declarations)
                    {
                        Set(specified, important, declaration, fromSheet: true);
                    }
                }
            }

            if (inline is not null)
            {
                var declarations = new List<CssDeclaration>();
                CssStyleSheet.ParseDeclarations(inline, declarations, onError);
                foreach (CssDeclaration declaration in declarations)
                {
                    Set(specified, important, declaration, fromSheet: false);
                }
            }

            element.SetSpecified(specified);
        }

        foreach (SvgElement child in element.Children)
        {
            Apply(child, sheet, onError);
        }
    }

    private static void Set(Dictionary<string, string> specified, HashSet<string> important, CssDeclaration declaration, bool fromSheet)
    {
        if (declaration.Property == "font")
        {
            ExpandFont(declaration.Value, (name, value) => Set(specified, important, new CssDeclaration(name, value, declaration.Important), fromSheet));
            return;
        }

        if (!StyleProperties.Contains(declaration.Property))
        {
            return;
        }

        if (important.Contains(declaration.Property) && !declaration.Important)
        {
            return;
        }

        specified[declaration.Property] = declaration.Value;
        if (declaration.Important)
        {
            _ = important.Add(declaration.Property);
        }
    }

    /// <summary>Expands the <c>font</c> shorthand into style, weight, size and family (CSS 2.1 §15.8); line height and variants are dropped.</summary>
    private static void ExpandFont(string value, Action<string, string> set)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part is "italic" or "oblique")
            {
                set("font-style", part);
            }
            else if (part is "bold" or "bolder" or "lighter" || (part.Length == 3 && part.EndsWith("00", StringComparison.Ordinal) && char.IsAsciiDigit(part[0])))
            {
                set("font-weight", part);
            }
            else if (part is "normal" or "small-caps")
            {
            }
            else if (char.IsAsciiDigit(part[0]) || part[0] == '.')
            {
                int slash = part.IndexOf('/');
                set("font-size", slash < 0 ? part : part[..slash]);
                if (i + 1 < parts.Length)
                {
                    set("font-family", string.Join(' ', parts, i + 1, parts.Length - i - 1));
                }

                return;
            }
        }
    }
}
