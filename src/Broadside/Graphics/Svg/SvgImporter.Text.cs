using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;

namespace Broadside.Graphics.Svg;

/// <summary>Text: <c>text</c> and <c>tspan</c> shown as real text in fonts the document's resolvers find, so it extracts.</summary>
/// <remarks>SVG 1.1 §10.4 (<c>text</c>), §10.5 (<c>tspan</c>), §10.9.1 (<c>text-anchor</c>), §10.15 (white space); ISO 32000-2 §9.4.</remarks>
internal sealed partial class SvgImporter
{
    private readonly Dictionary<(string? Family, int Weight, bool Italic), PdfCanvasFont> _fonts = [];

    /// <summary>A run of text with its style and the positions its element sets: absolute per character (<c>x</c>, <c>y</c>) and relative (<c>dx</c>, <c>dy</c>).</summary>
    private sealed record TextRun(SvgElement Element, SvgStyle Style, string Text, List<double>? X, List<double>? Y, List<double>? Dx, List<double>? Dy);

    /// <summary>A piece of a run placed at one position: the text from there to the next explicitly positioned character.</summary>
    private sealed record TextPiece(TextRun Run, string Text, double X, double Y, PdfCanvasFont Font, double Width);

    private void RenderText(SvgElement element, SvgStyle style)
    {
        List<TextPiece> pieces = LayOutText(element, style);
        if (pieces.Count == 0)
        {
            return;
        }

        WithEffects(
            element,
            style,
            () => TextBounds(pieces),
            alpha =>
            {
                foreach (TextPiece piece in pieces)
                {
                    SvgStyle pieceStyle = piece.Run.Style;
                    if (!pieceStyle.Visible || !pieceStyle.Displayed)
                    {
                        continue;
                    }

                    double size = pieceStyle.FontSize;
                    var bounds = new SvgRect(piece.X, piece.Y - (size * 0.8), piece.Width, size);
                    void Draw(PdfCanvas canvas, TextRenderingMode mode)
                    {
                        canvas.BeginText();
                        canvas.SetFont(piece.Font, size);
                        if (mode != TextRenderingMode.Fill)
                        {
                            canvas.SetTextRenderingMode(mode);
                        }

                        canvas.SetTextMatrix(new Matrix(1, 0, 0, -1, piece.X, piece.Y));
                        canvas.ShowText(piece.Text);
                        canvas.EndText();
                    }

                    if (pieceStyle.Fill.Kind != SvgPaintKind.None)
                    {
                        Paint(piece.Run.Element, pieceStyle, pieceStyle.Fill, pieceStyle.FillOpacity * alpha, bounds, stroke: false, canvas => Draw(canvas, TextRenderingMode.Fill));
                    }

                    if (pieceStyle.Stroke.Kind != SvgPaintKind.None && StrokeWidth(pieceStyle) > 0)
                    {
                        Paint(piece.Run.Element, pieceStyle, pieceStyle.Stroke, pieceStyle.StrokeOpacity * alpha, bounds, stroke: true, canvas =>
                        {
                            SetStrokeState(canvas, piece.Run.Element, pieceStyle);
                            Draw(canvas, TextRenderingMode.Stroke);
                        });
                    }
                }
            },
            foldable: false);
    }

    private SvgRect? TextBounds(SvgElement element, SvgStyle style)
    {
        List<TextPiece> pieces = LayOutText(element, style, quiet: true);
        return pieces.Count == 0 ? null : TextBounds(pieces);
    }

    private static SvgRect TextBounds(List<TextPiece> pieces)
    {
        SvgRect? union = null;
        foreach (TextPiece piece in pieces)
        {
            double size = piece.Run.Style.FontSize;
            var box = new SvgRect(piece.X, piece.Y - (piece.Font.Ascent / 1000 * size), piece.Width, (piece.Font.Ascent - piece.Font.Descent) / 1000 * size);
            union = union is { } current ? current.Union(box) : box;
        }

        return union ?? default;
    }

    /// <summary>Collects the runs of a text element, positions them, and applies <c>text-anchor</c> per chunk (a chunk starts at each absolute position).</summary>
    private List<TextPiece> LayOutText(SvgElement element, SvgStyle style, bool quiet = false)
    {
        var runs = new List<TextRun>();
        bool lastWasSpace = true;
        CollectRuns(element, style, runs, ref lastWasSpace, depth: 0);
        if (runs.Count > 0 && !style.PreserveSpace)
        {
            TextRun last = runs[^1];
            runs[^1] = last with { Text = last.Text.TrimEnd(' ') };
        }

        var pieces = new List<TextPiece>();
        var chunk = new List<int>();
        double x = 0, y = 0;
        void Anchor()
        {
            if (chunk.Count == 0)
            {
                return;
            }

            string anchor = pieces[chunk[0]].Run.Style.TextAnchor;
            if (anchor != "start")
            {
                double width = chunk.Sum(index => pieces[index].Width);
                double shift = anchor == "middle" ? -width / 2 : -width;
                foreach (int index in chunk)
                {
                    pieces[index] = pieces[index] with { X = pieces[index].X + shift };
                }
            }

            chunk.Clear();
        }

        foreach (TextRun run in runs)
        {
            PdfCanvasFont font = FontFor(run.Element, run.Style, quiet);
            string text = Encodable(run.Element, font, run.Text, quiet);
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                bool positioned = i < text.Length && (Has(run.X, i) || Has(run.Y, i) || Has(run.Dx, i) || Has(run.Dy, i));
                if (i > start && (positioned || i == text.Length))
                {
                    string part = text[start..i];
                    double width = font.MeasureWidth(part, run.Style.FontSize);
                    pieces.Add(new TextPiece(run, part, x, y, font, width));
                    chunk.Add(pieces.Count - 1);
                    x += width;
                    start = i;
                }

                if (positioned)
                {
                    if (Has(run.X, i) || Has(run.Y, i))
                    {
                        Anchor();
                    }

                    x = Has(run.X, i) ? run.X![i] : x;
                    y = Has(run.Y, i) ? run.Y![i] : y;
                    x += Has(run.Dx, i) ? run.Dx![i] : 0;
                    y += Has(run.Dy, i) ? run.Dy![i] : 0;
                }
            }
        }

        Anchor();
        return pieces;

        static bool Has(List<double>? values, int index) => values is not null && index < values.Count;
    }

    private void CollectRuns(SvgElement element, SvgStyle style, List<TextRun> runs, ref bool lastWasSpace, int depth)
    {
        List<double>? x = Positions(element, "x", _viewportWidth, style);
        List<double>? y = Positions(element, "y", _viewportHeight, style);
        List<double>? dx = Positions(element, "dx", _viewportWidth, style);
        List<double>? dy = Positions(element, "dy", _viewportHeight, style);
        int consumed = 0;
        foreach (object item in element.Content)
        {
            if (item is string raw)
            {
                string text = style.PreserveSpace ? raw.Replace('\n', ' ').Replace('\t', ' ').Replace('\r', ' ') : Collapse(raw, ref lastWasSpace);
                if (text.Length == 0)
                {
                    continue;
                }

                runs.Add(new TextRun(element, style, text, Slice(x, consumed), Slice(y, consumed), Slice(dx, consumed), Slice(dy, consumed)));
                consumed += text.Length;
                lastWasSpace = text.EndsWith(' ');
            }
            else if (item is SvgElement child && child.Name is "tspan" or "a" && depth < MaxReferenceDepth)
            {
                SvgStyle childStyle = StyleOf(child, style);
                if (childStyle.Displayed)
                {
                    CollectRuns(child, childStyle, runs, ref lastWasSpace, depth + 1);
                }
            }
            else if (item is SvgElement other && other.Name is "textPath" or "tref")
            {
                Report(other, SvgDiagnostics.SvgFeatureUnsupported, $"The {other.Name} element is not supported and is skipped.");
            }
        }

        static List<double>? Slice(List<double>? values, int from) => values is null || from >= values.Count ? null : values.GetRange(from, values.Count - from);
    }

    private List<double>? Positions(SvgElement element, string name, double reference, SvgStyle style)
    {
        if (element.Attribute(name) is not { } value)
        {
            return null;
        }

        var positions = new List<double>();
        foreach (string part in value.Split((char[])[' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!SvgLength.TryParse(part, out SvgLength length))
            {
                Report(element, SvgDiagnostics.SvgValueInvalid, $"The {name} value '{value}' of {element} cannot be read; positions after the error are ignored.");
                break;
            }

            positions.Add(length.Resolve(reference, style.FontSize));
        }

        return positions.Count == 0 ? null : positions;
    }

    /// <summary>Removes line ends, turns tabs into spaces and collapses runs of spaces, dropping a leading space after a space (SVG 1.1 §10.15, <c>xml:space="default"</c>).</summary>
    private static string Collapse(string text, ref bool lastWasSpace)
    {
        var builder = new StringBuilder(text.Length);
        bool space = lastWasSpace;
        foreach (char c in text)
        {
            if (c is '\n' or '\r')
            {
                continue;
            }

            if (c is ' ' or '\t')
            {
                if (!space)
                {
                    _ = builder.Append(' ');
                    space = true;
                }

                continue;
            }

            _ = builder.Append(c);
            space = false;
        }

        return builder.ToString();
    }

    /// <summary>Replaces each character the font cannot show by a question mark, and records it.</summary>
    private string Encodable(SvgElement element, PdfCanvasFont font, string text, bool quiet)
    {
        if (font.CanEncode(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            string value = rune.ToString();
            _ = builder.Append(font.CanEncode(value) ? value : font.CanEncode("?") ? "?" : string.Empty);
        }

        if (!quiet)
        {
            Report(element, SvgDiagnostics.SvgTextNotEncodable, $"Text of {element} has characters the font {font.Name} cannot show; a question mark is shown for each.");
        }

        return builder.ToString();
    }

    /// <summary>Finds the font for a style: the first family of <c>font-family</c> the resolvers find, then the default font, then Helvetica.</summary>
    private PdfCanvasFont FontFor(SvgElement element, SvgStyle style, bool quiet)
    {
        bool bold = style.FontWeight >= 600;
        (string? Family, int Weight, bool Italic) key = (style.FontFamily, bold ? 700 : 400, style.Italic);
        if (_fonts.TryGetValue(key, out PdfCanvasFont? cached))
        {
            return cached;
        }

        PdfCanvasFont? font = null;
        bool named = false;
        foreach (string raw in (style.FontFamily ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string family = raw.Trim('"', '\'', ' ');
            if (family.Length == 0)
            {
                continue;
            }

            if (Generic(family) is { } generic)
            {
                font = Standard(generic, bold, style.Italic);
                break;
            }

            named = true;
            font = _document.EmbedFont(new FontQuery(family)
            {
                FontFamily = family,
                FontWeight = key.Weight,
                ItalicAngle = style.Italic ? -12 : 0,
            });
            if (font is not null)
            {
                break;
            }

            if (Alias(family) is { } alias)
            {
                font = Standard(alias, bold, style.Italic);
                break;
            }
        }

        if (font is null && _options.DefaultFont is { } fallback)
        {
            font = _document.EmbedFont(fallback);
        }

        if (font is null)
        {
            if (named && !quiet)
            {
                Report(element, SvgDiagnostics.SvgFontNotFound, $"No font of the families '{style.FontFamily}' of {element} is found; Helvetica is used.");
            }

            font = Standard(Standard14Family.Helvetica, bold, style.Italic);
        }

        _fonts[key] = font;
        return font;
    }

    private enum Standard14Family
    {
        Helvetica,
        Times,
        Courier,
    }

    /// <summary>Maps a CSS generic family to its Standard 14 stand-in (CSS Fonts Level 3 §3.1.1).</summary>
    private static Standard14Family? Generic(string family) => family.ToLowerInvariant() switch
    {
        "serif" or "ui-serif" => Standard14Family.Times,
        "monospace" or "ui-monospace" => Standard14Family.Courier,
        "sans-serif" or "system-ui" or "cursive" or "fantasy" or "ui-sans-serif" or "ui-rounded" => Standard14Family.Helvetica,
        _ => null,
    };

    /// <summary>Maps a family whose metrics a Standard 14 font matches to it, used when the resolvers do not find the family itself.</summary>
    private static Standard14Family? Alias(string family) => family.ToLowerInvariant() switch
    {
        "times" or "times new roman" or "times-roman" or "liberation serif" => Standard14Family.Times,
        "courier" or "courier new" or "liberation mono" => Standard14Family.Courier,
        "helvetica" or "arial" or "liberation sans" => Standard14Family.Helvetica,
        _ => null,
    };

    /// <summary>Returns a Standard 14 face: embedded through the resolvers (the Standard 14 package) when one supplies it, else the non-embedded font.</summary>
    private PdfCanvasFont Standard(Standard14Family family, bool bold, bool italic)
    {
        Standard14Font face = (family, bold, italic) switch
        {
            (Standard14Family.Times, false, false) => Standard14Font.TimesRoman,
            (Standard14Family.Times, true, false) => Standard14Font.TimesBold,
            (Standard14Family.Times, false, true) => Standard14Font.TimesItalic,
            (Standard14Family.Times, true, true) => Standard14Font.TimesBoldItalic,
            (Standard14Family.Courier, false, false) => Standard14Font.Courier,
            (Standard14Family.Courier, true, false) => Standard14Font.CourierBold,
            (Standard14Family.Courier, false, true) => Standard14Font.CourierOblique,
            (Standard14Family.Courier, true, true) => Standard14Font.CourierBoldOblique,
            (_, false, false) => Standard14Font.Helvetica,
            (_, true, false) => Standard14Font.HelveticaBold,
            (_, false, true) => Standard14Font.HelveticaOblique,
            _ => Standard14Font.HelveticaBoldOblique,
        };

        string name = face switch
        {
            Standard14Font.TimesRoman => "Times-Roman",
            Standard14Font.TimesBold => "Times-Bold",
            Standard14Font.TimesItalic => "Times-Italic",
            Standard14Font.TimesBoldItalic => "Times-BoldItalic",
            Standard14Font.Courier => "Courier",
            Standard14Font.CourierBold => "Courier-Bold",
            Standard14Font.CourierOblique => "Courier-Oblique",
            Standard14Font.CourierBoldOblique => "Courier-BoldOblique",
            Standard14Font.Helvetica => "Helvetica",
            Standard14Font.HelveticaBold => "Helvetica-Bold",
            Standard14Font.HelveticaOblique => "Helvetica-Oblique",
            _ => "Helvetica-BoldOblique",
        };

        return _document.EmbedFont(new FontQuery(name) { Standard14 = face }) ?? _document.GetStandard14Font(face);
    }
}
