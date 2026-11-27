using System.Globalization;
using System.Text;
using Broadside.Annotations;
using Broadside.Objects;

namespace Broadside.Forms.FormData;

/// <summary>Writes XFDF markup directly, as the XMP packet writer does, without <c>System.Xml</c>: elements, attributes, escaped text and checked raw content.</summary>
/// <remarks>Adobe XFDF 3.0 (ISO 19444-1); XML 1.0 §2.4 and §3.3.3 for escaping. Characters XML cannot carry are left out.</remarks>
internal sealed class XfdfWriter
{
    private readonly StringBuilder _output = new();
    private readonly Stack<(string Name, bool HasContent)> _open = new();
    private bool _startTagOpen;

    public void Raw(string text) => _output.Append(text);

    public void Start(string name)
    {
        CloseStartTag(markContent: true);
        _output.Append('<').Append(name);
        _open.Push((name, false));
        _startTagOpen = true;
    }

    public void Attribute(string name, string value)
    {
        _output.Append(' ').Append(name).Append("=\"");
        foreach (char c in XmpPacketWriter.Clean(value))
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

        _output.Append('"');
    }

    public void Text(string text)
    {
        CloseStartTag(markContent: true);
        foreach (char c in XmpPacketWriter.Clean(text))
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

    /// <summary>Appends markup checked by <see cref="WellFormedContent"/>.</summary>
    public void RawContent(string content)
    {
        CloseStartTag(markContent: true);
        _output.Append(content);
    }

    public void End()
    {
        (string name, _) = _open.Pop();
        if (_startTagOpen)
        {
            _startTagOpen = false;
            _output.Append("/>");
            return;
        }

        _output.Append("</").Append(name).Append('>');
    }

    public override string ToString() => _output.ToString();

    /// <summary>
    /// The markup of a rich text string (§12.7.3.4) as element content: without its XML declaration, and only when it is well-formed
    /// XML on its own; <see langword="null"/> otherwise, so the file stays well-formed.
    /// </summary>
    public static string? WellFormedContent(string markup)
    {
        string content = markup.Trim();
        if (content.StartsWith("<?xml", StringComparison.Ordinal) && content.IndexOf("?>", StringComparison.Ordinal) is int end and > 0)
        {
            content = content[(end + 2)..].Trim();
        }

        return content.Length > 0 && XmpXmlReader.ReadElement($"<r>{content}</r>", int.MaxValue, namespaceName: null, "r", out string? error) is not null && error is null
            ? content
            : null;
    }

    private void CloseStartTag(bool markContent)
    {
        if (_startTagOpen)
        {
            _output.Append('>');
            _startTagOpen = false;
        }

        if (markContent && _open.Count > 0)
        {
            (string name, _) = _open.Pop();
            _open.Push((name, true));
        }
    }
}

/// <summary>Writes one markup annotation as its XFDF element.</summary>
/// <remarks>Adobe XFDF 3.0 (ISO 19444-1) for the elements and attributes; ISO 32000-2 §12.5.2 and §12.5.6 for the entries they carry.</remarks>
internal static class XfdfAnnotationWriter
{
    private static readonly Dictionary<string, string> Elements = new(StringComparer.Ordinal)
    {
        ["Text"] = "text",
        ["FreeText"] = "freetext",
        ["Line"] = "line",
        ["Square"] = "square",
        ["Circle"] = "circle",
        ["Polygon"] = "polygon",
        ["PolyLine"] = "polyline",
        ["Highlight"] = "highlight",
        ["Underline"] = "underline",
        ["Squiggly"] = "squiggly",
        ["StrikeOut"] = "strikeout",
        ["Caret"] = "caret",
        ["Stamp"] = "stamp",
        ["Ink"] = "ink",
    };

    public static void Write(XfdfWriter xml, PdfDocument document, int page, PdfMarkupAnnotation annotation)
    {
        CosDictionary dictionary = annotation.Dictionary;
        if (annotation.Subtype?.Value is not { } subtype || !Elements.TryGetValue(subtype, out string? element))
        {
            return;
        }

        CosObject Get(CosName key) => document.Resolve(dictionary.TryGetValue(key, out CosObject? value) ? value : null);

        xml.Start(element);
        xml.Attribute("page", page.ToString(CultureInfo.InvariantCulture));
        xml.Attribute("rect", Numbers(Get(AnnotationNames.Rect), ","));
        Text(xml, "name", Get(AnnotationNames.NM));
        Text(xml, "title", Get(AnnotationNames.T));
        Text(xml, "subject", Get(AnnotationNames.Subj));
        Text(xml, "date", Get(AnnotationNames.M));
        Text(xml, "creationdate", Get(AnnotationNames.CreationDate));
        if (Color(Get(AnnotationNames.C)) is { } color)
        {
            xml.Attribute("color", color);
        }

        if (Color(Get(AnnotationNames.IC)) is { } interior)
        {
            xml.Attribute("interior-color", interior);
        }

        if (Flags(Get(AnnotationNames.F)) is { Length: > 0 } flags)
        {
            xml.Attribute("flags", flags);
        }

        if (Get(AnnotationNames.StrokingOpacity) is CosNumber opacity && opacity.ToDouble() < 1)
        {
            xml.Attribute("opacity", Format(opacity.ToDouble()));
        }

        if (Get(AnnotationNames.BS) is CosDictionary border && document.Resolve(border.TryGetValue(AnnotationNames.W, out CosObject? w) ? w : null) is CosNumber width)
        {
            xml.Attribute("width", Format(width.ToDouble()));
        }

        if (annotation.InReplyTo?.Name is { } parent)
        {
            xml.Attribute("inreplyto", parent);
        }

        if (Get(AnnotationNames.RT) is CosName { Value: "Group" })
        {
            xml.Attribute("replyType", "group");
        }

        if (Get(AnnotationNames.IT) is CosName intent)
        {
            xml.Attribute("intent", intent.Value);
        }

        Specific(xml, subtype, Get, out Action? children);
        if (annotation.Contents is { } contents)
        {
            xml.Start("contents");
            xml.Text(contents);
            xml.End();
        }

        if (annotation.RichText is { } rich && XfdfWriter.WellFormedContent(rich) is { } content)
        {
            xml.Start("contents-richtext");
            xml.RawContent(content);
            xml.End();
        }

        children?.Invoke();
        if (annotation.Popup is { } popup && popup.Page is { } popupPage)
        {
            xml.Start("popup");
            xml.Attribute("page", (document.Pages.IndexOf(popupPage) is int index and >= 0 ? index : page).ToString(CultureInfo.InvariantCulture));
            xml.Attribute("rect", Numbers(document.Resolve(popup.Dictionary.TryGetValue(AnnotationNames.Rect, out CosObject? rect) ? rect : null), ","));
            xml.Attribute("open", document.Resolve(popup.Dictionary.TryGetValue(AnnotationNames.Open, out CosObject? open) ? open : null) is CosBoolean { Value: true } ? "yes" : "no");
            if (Flags(document.Resolve(popup.Dictionary.TryGetValue(AnnotationNames.F, out CosObject? f) ? f : null)) is { Length: > 0 } popupFlags)
            {
                xml.Attribute("flags", popupFlags);
            }

            xml.End();
        }

        xml.End();
    }

    private static void Specific(XfdfWriter xml, string subtype, Func<CosName, CosObject> get, out Action? children)
    {
        children = null;
        switch (subtype)
        {
            case "Text":
                if (get(AnnotationNames.Name) is CosName icon)
                {
                    xml.Attribute("icon", icon.Value);
                }

                Text(xml, "state", get(AnnotationNames.State));
                Text(xml, "statemodel", get(AnnotationNames.StateModel));
                break;
            case "Stamp":
                if (get(AnnotationNames.Name) is CosName stamp)
                {
                    xml.Attribute("icon", stamp.Value);
                }

                break;
            case "Highlight" or "Underline" or "Squiggly" or "StrikeOut":
                xml.Attribute("coords", Numbers(get(AnnotationNames.QuadPoints), ","));
                break;
            case "Line":
                if (get(AnnotationNames.L) is CosArray { Count: 4 } line)
                {
                    xml.Attribute("start", $"{Format(line[0])},{Format(line[1])}");
                    xml.Attribute("end", $"{Format(line[2])},{Format(line[3])}");
                }

                if (get(AnnotationNames.LE) is CosArray { Count: 2 } endings && endings[0] is CosName head && endings[1] is CosName tail)
                {
                    xml.Attribute("head", head.Value);
                    xml.Attribute("tail", tail.Value);
                }

                break;
            case "FreeText":
                if (get(AnnotationNames.Q) is CosInteger { Value: 1 or 2 } quadding)
                {
                    xml.Attribute("justification", quadding.Value == 1 ? "centered" : "right");
                }

                children = () =>
                {
                    Element(xml, "defaultappearance", get(AnnotationNames.DA));
                    Element(xml, "defaultstyle", get(AnnotationNames.DS));
                };
                break;
            case "Ink":
                if (get(AnnotationNames.InkList) is CosArray strokes)
                {
                    children = () =>
                    {
                        xml.Start("inklist");
                        foreach (CosObject stroke in strokes)
                        {
                            if (stroke is CosArray points)
                            {
                                xml.Start("gesture");
                                xml.Text(Points(points));
                                xml.End();
                            }
                        }

                        xml.End();
                    };
                }

                break;
            case "Polygon" or "PolyLine":
                if (get(AnnotationNames.Vertices) is CosArray vertices)
                {
                    children = () =>
                    {
                        xml.Start("vertices");
                        xml.Text(Points(vertices));
                        xml.End();
                    };
                }

                break;
        }
    }

    private static void Text(XfdfWriter xml, string attribute, CosObject value)
    {
        if (value is CosString text)
        {
            xml.Attribute(attribute, text.DecodeText());
        }
    }

    private static void Element(XfdfWriter xml, string name, CosObject value)
    {
        if (value is CosString text)
        {
            xml.Start(name);
            xml.Text(text.DecodeText());
            xml.End();
        }
    }

    private static string Numbers(CosObject value, string separator) =>
        value is CosArray array ? string.Join(separator, array.OfType<CosNumber>().Select(static number => Format(number.ToDouble()))) : string.Empty;

    /// <summary>Pairs of numbers as Adobe XFDF 3.0 writes gestures and vertices: <c>x,y;x,y</c>.</summary>
    private static string Points(CosArray array)
    {
        double[] numbers = [.. array.OfType<CosNumber>().Select(static number => number.ToDouble())];
        var builder = new StringBuilder();
        for (int index = 0; index + 1 < numbers.Length; index += 2)
        {
            builder.Append(index == 0 ? string.Empty : ";").Append(Format(numbers[index])).Append(',').Append(Format(numbers[index + 1]));
        }

        return builder.ToString();
    }

    private static string Format(CosObject value) => value is CosNumber number ? Format(number.ToDouble()) : "0";

    private static string Format(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>A device colour as <c>#RRGGBB</c>: gray and CMYK are converted as §10.4.2 and §10.4.4 convert them to RGB.</summary>
    private static string? Color(CosObject value)
    {
        if (value is not CosArray array || array.Any(static component => component is not CosNumber))
        {
            return null;
        }

        double[] c = [.. array.Cast<CosNumber>().Select(static number => Math.Clamp(number.ToDouble(), 0, 1))];
        (double r, double g, double b) = c.Length switch
        {
            1 => (c[0], c[0], c[0]),
            3 => (c[0], c[1], c[2]),
            4 => ((1 - c[0]) * (1 - c[3]), (1 - c[1]) * (1 - c[3]), (1 - c[2]) * (1 - c[3])),
            _ => (-1, -1, -1),
        };
        return r < 0 ? null : string.Create(CultureInfo.InvariantCulture, $"#{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}");
    }

    private static string? Flags(CosObject value)
    {
        if (value is not CosInteger integer)
        {
            return null;
        }

        var names = new List<string>();
        for (int bit = 0; bit < XfdfReader.FlagNames.Length; bit++)
        {
            if ((integer.Value & (1L << bit)) != 0)
            {
                names.Add(XfdfReader.FlagNames[bit]);
            }
        }

        return string.Join(',', names);
    }
}
