using System.Globalization;
using System.Text;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms.FormData;

/// <summary>
/// Reads an XFDF file with the <c>System.Xml</c>-free reader the XMP packets use: field values from <c>&lt;fields&gt;</c> and
/// markup annotations from <c>&lt;annots&gt;</c>, turned into the annotation dictionaries of ISO 32000-2 §12.5.6.
/// </summary>
/// <remarks>
/// <para>
/// XFDF is specified by ISO 19444-1:2019, which is not among the specifications Broadside cites from; the working reference is Adobe's
/// public XFDF 3.0 specification. The root is <c>&lt;xfdf xmlns="http://ns.adobe.com/xfdf/"&gt;</c>; fields nest
/// <c>&lt;field name&gt;</c> elements (a name may also hold periods, a fully qualified name split differently), each with one
/// <c>&lt;value&gt;</c> per selected value or a <c>&lt;value-richtext&gt;</c> rich text value (§12.7.5.3, <c>RV</c>).
/// </para>
/// <para>
/// Each annotation element (<c>&lt;text&gt;</c>, <c>&lt;highlight&gt;</c>, <c>&lt;ink&gt;</c> …) becomes an annotation dictionary
/// of the matching subtype (§12.5.6, Table 171) with its common attributes (<c>page</c>, <c>rect</c>, <c>name</c>, <c>title</c>,
/// <c>subject</c>, <c>date</c>, <c>creationdate</c>, <c>color</c>, <c>flags</c>, <c>opacity</c>, <c>width</c>) and those of its
/// subtype; a <c>&lt;popup&gt;</c> child becomes its pop-up annotation, and <c>inreplyto</c> names the parent by its <c>NM</c>,
/// resolved after every annotation is in. A document type declaration is an error (the reader never expands entities), so nothing
/// outside the file is ever fetched.
/// </para>
/// </remarks>
internal static class XfdfReader
{
    /// <summary>The XFDF namespace (Adobe XFDF 3.0).</summary>
    public const string Namespace = "http://ns.adobe.com/xfdf/";

    private const int MaxCharacters = 256 * 1024 * 1024;
    private const int MaxFieldDepth = 64;

    private static readonly Dictionary<string, string> Subtypes = new(StringComparer.Ordinal)
    {
        ["text"] = "Text",
        ["freetext"] = "FreeText",
        ["line"] = "Line",
        ["square"] = "Square",
        ["circle"] = "Circle",
        ["polygon"] = "Polygon",
        ["polyline"] = "PolyLine",
        ["highlight"] = "Highlight",
        ["underline"] = "Underline",
        ["squiggly"] = "Squiggly",
        ["strikeout"] = "StrikeOut",
        ["caret"] = "Caret",
        ["stamp"] = "Stamp",
        ["ink"] = "Ink",
    };

    /// <summary>The annotation flag names of XFDF's <c>flags</c> attribute, in bit order (§12.5.3, Table 167).</summary>
    internal static readonly string[] FlagNames = ["invisible", "hidden", "print", "nozoom", "norotate", "noview", "readonly", "locked", "togglenoview", "lockedcontents"];

    /// <summary>Reads the file.</summary>
    /// <param name="bytes">The XFDF file.</param>
    /// <param name="diagnostics">Where malformed entries are recorded.</param>
    /// <returns>The fields and annotations the file holds.</returns>
    /// <exception cref="DiagnosticException">The file is not well-formed XML with an <c>xfdf</c> root, or (strict mode) it deviates from XFDF.</exception>
    public static FormDataSet Read(ReadOnlySpan<byte> bytes, DiagnosticSink diagnostics)
    {
        string text = DecodeText(bytes);
        XmpXmlElement? root = XmpXmlReader.ReadElement(text, MaxCharacters, namespaceName: null, "xfdf", out string? error);
        if (root is null)
        {
            diagnostics.Fail(
                FormDiagnostics.FormDataInvalid,
                error is null ? "The file has no xfdf element: it is not an XFDF file." : $"The XFDF file is not well-formed XML: {error}.");
        }

        if (root.NamespaceName != Namespace)
        {
            Invalid(diagnostics, $"The xfdf element's namespace is '{root.NamespaceName}', not {Namespace} (Adobe XFDF 3.0); it is read anyway.");
        }

        var graph = new Dictionary<CosReference, CosObject>();
        var set = new FormDataSet(value => value is CosReference reference ? graph.GetValueOrDefault(reference) ?? CosNull.Instance : value ?? CosNull.Instance);
        foreach (XmpXmlElement child in root.Children)
        {
            switch (child.LocalName)
            {
                case "fields":
                    ReadFields(child, set.Fields, diagnostics);
                    break;
                case "annots":
                    var annotations = new AnnotationReader(graph, set.Annotations, diagnostics);
                    foreach (XmpXmlElement annotation in child.Children)
                    {
                        annotations.Read(annotation);
                    }

                    break;
            }
        }

        return set;
    }

    /// <summary>Decodes the file's bytes: UTF-16 after its byte order mark, ISO-8859-1 when the declaration says so, UTF-8 otherwise.</summary>
    private static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        ReadOnlySpan<byte> declaration = bytes[..Math.Min(bytes.Length, 100)];
        int end = declaration.IndexOf("?>"u8);
        if (declaration.StartsWith("<?xml"u8) && end > 0)
        {
            string text = Encoding.ASCII.GetString(declaration[..end]);
            if (text.Contains("ISO-8859-1", StringComparison.OrdinalIgnoreCase) || text.Contains("latin1", StringComparison.OrdinalIgnoreCase))
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static void ReadFields(XmpXmlElement fields, List<FormDataField> entries, DiagnosticSink diagnostics)
    {
        var work = new Stack<(XmpXmlElement Element, string? Parent, int Depth)>();
        for (int index = fields.Children.Count - 1; index >= 0; index--)
        {
            work.Push((fields.Children[index], null, 0));
        }

        while (work.TryPop(out (XmpXmlElement Element, string? Parent, int Depth) item))
        {
            XmpXmlElement element = item.Element;
            if (element.LocalName != "field")
            {
                continue;
            }

            if (item.Depth >= MaxFieldDepth)
            {
                Invalid(diagnostics, $"The XFDF fields are nested deeper than {MaxFieldDepth} levels; the fields below are not read.");
                continue;
            }

            string? partial = Attribute(element, "name");
            if (string.IsNullOrEmpty(partial))
            {
                Invalid(diagnostics, "An XFDF field element has no name attribute; it and its children are skipped.");
                continue;
            }

            string name = item.Parent is null ? partial : $"{item.Parent}.{partial}";
            var field = new FormDataField(name);
            var values = new List<CosObject>();
            XmpXmlElement? richText = null;
            foreach (XmpXmlElement child in element.Children)
            {
                switch (child.LocalName)
                {
                    case "value":
                        values.Add(new CosString(TextStringEncoder.Encode(child.Text)));
                        break;
                    case "value-richtext":
                        richText = child;
                        break;
                }
            }

            if (richText is not null)
            {
                // The plain text value is the value element when the file has one, else the rich text's own text (§12.7.4.3.4).
                field.RichValue = richText.RawContent.Trim();
                if (values.Count == 0)
                {
                    values.Add(new CosString(TextStringEncoder.Encode(PlainText(richText))));
                }
            }

            if (values.Count == 1)
            {
                field.Value = values[0];
            }
            else if (values.Count > 1)
            {
                field.Value = new CosArray(values);
            }

            if (field.HasChanges)
            {
                entries.Add(field);
            }

            for (int index = element.Children.Count - 1; index >= 0; index--)
            {
                work.Push((element.Children[index], name, item.Depth + 1));
            }
        }
    }

    /// <summary>The text of a rich text value, its paragraphs on separate lines (§12.7.4.3.2: the plain text that <c>V</c> holds beside <c>RV</c>).</summary>
    private static string PlainText(XmpXmlElement element)
    {
        var builder = new StringBuilder();
        var work = new Stack<XmpXmlElement>();
        work.Push(element);
        while (work.TryPop(out XmpXmlElement? current))
        {
            if (current.LocalName == "p" && builder.Length > 0)
            {
                builder.Append('\r');
            }

            builder.Append(current.Text);
            for (int index = current.Children.Count - 1; index >= 0; index--)
            {
                work.Push(current.Children[index]);
            }
        }

        return builder.ToString();
    }

    internal static string? Attribute(XmpXmlElement element, string name)
    {
        foreach (XmpXmlAttribute attribute in element.Attributes)
        {
            if (attribute.LocalName == name && attribute.NamespaceName.Length == 0)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static void Invalid(DiagnosticSink diagnostics, string message) =>
        diagnostics.Report(FormDiagnostics.FormDataInvalid, DiagnosticSeverity.Warning, message);

    /// <summary>Turns annotation elements into annotation dictionaries in a graph of their own, numbered from 1.</summary>
    private sealed class AnnotationReader(Dictionary<CosReference, CosObject> graph, List<FormDataAnnotation> annotations, DiagnosticSink diagnostics)
    {
        public void Read(XmpXmlElement element)
        {
            if (!Subtypes.TryGetValue(element.LocalName, out string? subtype))
            {
                Skip($"The XFDF annotation element {element.LocalName} is not one Broadside imports; it is skipped.");
                return;
            }

            if (!int.TryParse(Attribute(element, "page"), NumberStyles.None, CultureInfo.InvariantCulture, out int page))
            {
                Skip($"An XFDF {element.LocalName} element has no page attribute with a page number; it is skipped.");
                return;
            }

            if (Numbers(Attribute(element, "rect"), ',') is not [var x1, var y1, var x2, var y2])
            {
                Skip($"An XFDF {element.LocalName} element has no rect attribute of four numbers; it is skipped.");
                return;
            }

            var dictionary = new CosDictionary
            {
                [KnownNames.Type] = AnnotationNames.Annot,
                [AnnotationNames.Subtype] = new CosName(subtype),
                [AnnotationNames.Rect] = NumberArray([Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2)]),
            };
            Common(element, dictionary);
            if (!Specific(element, subtype, dictionary))
            {
                return;
            }

            CosReference reference = Add(dictionary);
            var annotation = new FormDataAnnotation(page, dictionary, reference) { InReplyToName = NonEmpty(Attribute(element, "inreplyto")) };
            annotations.Add(annotation);
            if (Child(element, "popup") is { } popup)
            {
                ReadPopup(popup, page, dictionary, reference);
            }
        }

        private void ReadPopup(XmpXmlElement element, int parentPage, CosDictionary parent, CosReference parentReference)
        {
            int page = int.TryParse(Attribute(element, "page"), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : parentPage;
            if (Numbers(Attribute(element, "rect"), ',') is not [var x1, var y1, var x2, var y2])
            {
                Invalid(diagnostics, "An XFDF popup element has no rect attribute of four numbers; the annotation is imported without its pop-up.");
                return;
            }

            var popup = new CosDictionary
            {
                [KnownNames.Type] = AnnotationNames.Annot,
                [AnnotationNames.Subtype] = new CosName("Popup"),
                [AnnotationNames.Rect] = NumberArray([Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2)]),
                [AnnotationNames.Parent] = parentReference,
            };
            if (Attribute(element, "open") is { } open)
            {
                popup[AnnotationNames.Open] = (open is "yes" or "true" ? CosBoolean.True : CosBoolean.False);
            }

            if (Flags(Attribute(element, "flags")) is { } flags)
            {
                popup[AnnotationNames.F] = new CosInteger(flags);
            }

            CosReference reference = Add(popup);
            parent[AnnotationNames.Popup] = reference;
            annotations.Add(new FormDataAnnotation(page, popup, reference));
        }

        private void Common(XmpXmlElement element, CosDictionary dictionary)
        {
            SetText(dictionary, AnnotationNames.NM, Attribute(element, "name"));
            SetText(dictionary, AnnotationNames.T, Attribute(element, "title"));
            SetText(dictionary, AnnotationNames.Subj, Attribute(element, "subject"));
            SetText(dictionary, AnnotationNames.M, Attribute(element, "date"));
            SetText(dictionary, AnnotationNames.CreationDate, Attribute(element, "creationdate"));
            if (Color(Attribute(element, "color")) is { } color)
            {
                dictionary[AnnotationNames.C] = color;
            }

            if (Color(Attribute(element, "interior-color")) is { } interior)
            {
                dictionary[AnnotationNames.IC] = interior;
            }

            if (Flags(Attribute(element, "flags")) is { } flags)
            {
                dictionary[AnnotationNames.F] = new CosInteger(flags);
            }

            if (Number(Attribute(element, "opacity")) is { } opacity)
            {
                dictionary[AnnotationNames.StrokingOpacity] = AnnotationEditing.Number(Math.Clamp(opacity, 0, 1));
            }

            if (Number(Attribute(element, "width")) is { } width && width >= 0)
            {
                dictionary[AnnotationNames.BS] = new CosDictionary { [AnnotationNames.W] = AnnotationEditing.Number(width), [AnnotationNames.S] = new CosName("S") };
            }

            if (Attribute(element, "replyType") is "group")
            {
                dictionary[AnnotationNames.RT] = new CosName("Group");
            }

            if (NonEmpty(Attribute(element, "intent")) is { } intent)
            {
                dictionary[AnnotationNames.IT] = new CosName(intent);
            }

            if (Child(element, "contents") is { } contents)
            {
                SetText(dictionary, AnnotationNames.Contents, contents.Text);
            }

            if (Child(element, "contents-richtext") is { } rich)
            {
                SetText(dictionary, AnnotationNames.RC, rich.RawContent.Trim());
                if (!dictionary.ContainsKey(AnnotationNames.Contents))
                {
                    SetText(dictionary, AnnotationNames.Contents, PlainText(rich));
                }
            }
        }

        /// <summary>The subtype's own entries; <see langword="false"/> (after a diagnostic) when a required one is missing.</summary>
        private bool Specific(XmpXmlElement element, string subtype, CosDictionary dictionary)
        {
            switch (subtype)
            {
                case "Text":
                    dictionary[AnnotationNames.Name] = new CosName(NonEmpty(Attribute(element, "icon")) ?? "Note");
                    SetText(dictionary, AnnotationNames.State, Attribute(element, "state"));
                    SetText(dictionary, AnnotationNames.StateModel, Attribute(element, "statemodel"));
                    return true;
                case "Stamp":
                    dictionary[AnnotationNames.Name] = new CosName(NonEmpty(Attribute(element, "icon")) ?? "Draft");
                    return true;
                case "Highlight" or "Underline" or "Squiggly" or "StrikeOut":
                    if (Numbers(Attribute(element, "coords"), ',') is not { Length: > 0 } coords || coords.Length % 8 != 0)
                    {
                        return Skip($"An XFDF {element.LocalName} element has no coords attribute of eight numbers per quadrilateral (§12.5.6.10, QuadPoints); it is skipped.");
                    }

                    dictionary[AnnotationNames.QuadPoints] = NumberArray(coords);
                    return true;
                case "Ink":
                    var strokes = new CosArray();
                    foreach (XmpXmlElement gesture in Child(element, "inklist")?.Children ?? [])
                    {
                        if (gesture.LocalName == "gesture" && Points(gesture.Text) is { Length: >= 2 } points)
                        {
                            strokes.Add(NumberArray(points));
                        }
                    }

                    if (strokes.Count == 0)
                    {
                        return Skip("An XFDF ink element has no inklist with a gesture of points (§12.5.6.13, InkList); it is skipped.");
                    }

                    dictionary[AnnotationNames.InkList] = strokes;
                    return true;
                case "Line":
                    if (Numbers(Attribute(element, "start"), ',') is not [var x1, var y1] || Numbers(Attribute(element, "end"), ',') is not [var x2, var y2])
                    {
                        return Skip("An XFDF line element has no start and end attributes of two numbers each (§12.5.6.7, L); it is skipped.");
                    }

                    dictionary[AnnotationNames.L] = NumberArray([x1, y1, x2, y2]);
                    string? head = NonEmpty(Attribute(element, "head"));
                    string? tail = NonEmpty(Attribute(element, "tail"));
                    if (head is not null || tail is not null)
                    {
                        dictionary[AnnotationNames.LE] = new CosArray { new CosName(head ?? "None"), new CosName(tail ?? "None") };
                    }

                    return true;
                case "Polygon" or "PolyLine":
                    if (Points(Child(element, "vertices")?.Text) is not { Length: >= 4 } vertices)
                    {
                        return Skip($"An XFDF {element.LocalName} element has no vertices of at least two points (§12.5.6.9, Vertices); it is skipped.");
                    }

                    dictionary[AnnotationNames.Vertices] = NumberArray(vertices);
                    return true;
                case "FreeText":
                    SetText(dictionary, AnnotationNames.DA, Child(element, "defaultappearance")?.Text ?? "/Helv 12 Tf 0 g");
                    SetText(dictionary, AnnotationNames.DS, Child(element, "defaultstyle")?.Text);
                    if (Attribute(element, "justification") is { } justification)
                    {
                        dictionary[AnnotationNames.Q] = new CosInteger(justification switch { "centered" => 1, "right" => 2, _ => 0 });
                    }

                    return true;
                default:
                    return true;
            }
        }

        private CosReference Add(CosDictionary dictionary)
        {
            var reference = new CosReference(graph.Count + 1, 0);
            graph.Add(reference, dictionary);
            return reference;
        }

        private bool Skip(string message)
        {
            diagnostics.Report(FormDiagnostics.FormDataAnnotationSkipped, DiagnosticSeverity.Warning, message);
            return false;
        }

        private CosArray? Color(string? value)
        {
            if (value is null)
            {
                return null;
            }

            if (value is ['#', _, _, _, _, _, _] && int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return NumberArray([((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0]);
            }

            Invalid(diagnostics, $"An XFDF color '{value}' is not of the form #RRGGBB; it is ignored.");
            return null;
        }

        private uint? Flags(string? value)
        {
            if (value is null)
            {
                return null;
            }

            uint flags = 0;
            foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int bit = Array.IndexOf(FlagNames, part.ToLowerInvariant());
                if (bit < 0)
                {
                    Invalid(diagnostics, $"The XFDF annotation flag '{part}' is not one of Adobe XFDF 3.0's; it is ignored.");
                    continue;
                }

                flags |= 1u << bit;
            }

            return flags;
        }

        private double? Number(string? value)
        {
            if (value is null)
            {
                return null;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
            {
                return number;
            }

            Invalid(diagnostics, $"The XFDF attribute value '{value}' is not a number; it is ignored.");
            return null;
        }
    }

    private static XmpXmlElement? Child(XmpXmlElement element, string localName)
    {
        foreach (XmpXmlElement child in element.Children)
        {
            if (child.LocalName == localName)
            {
                return child;
            }
        }

        return null;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void SetText(CosDictionary dictionary, CosName key, string? value)
    {
        if (value is not null)
        {
            dictionary[key] = new CosString(TextStringEncoder.Encode(value));
        }
    }

    /// <summary>Numbers separated by <paramref name="separator"/> (and white space); <see langword="null"/> when any is not a finite number.</summary>
    internal static double[]? Numbers(string? value, char separator)
    {
        if (value is null)
        {
            return null;
        }

        string[] parts = value.Split([separator, ' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var numbers = new double[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index]) || !double.IsFinite(numbers[index]))
            {
                return null;
            }
        }

        return numbers;
    }

    /// <summary>Points written <c>x,y;x,y</c> (Adobe XFDF 3.0 gestures and vertices); a flat list of coordinates, or <see langword="null"/>.</summary>
    private static double[]? Points(string? value) => value is null ? null : Numbers(value.Replace(';', ','), ',') is { } numbers && numbers.Length % 2 == 0 ? numbers : null;

    private static CosArray NumberArray(IEnumerable<double> numbers)
    {
        var array = new CosArray();
        foreach (double number in numbers)
        {
            array.Add(AnnotationEditing.Number(number));
        }

        return array;
    }
}
