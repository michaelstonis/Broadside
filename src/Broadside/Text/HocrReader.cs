using System.Globalization;
using System.Text;

namespace Broadside.Text;

/// <summary>
/// Reads hOCR 1.2 (http://kba.github.io/hocr-spec/1.2/) into <see cref="OcrPage"/>s with a tolerant tag reader rather than an XML
/// parser, so both XHTML and HTML that is not well-formed XML are read.
/// </summary>
/// <remarks>
/// See <see cref="OcrPage.FromHocr(Stream)"/>. Tags are matched loosely as browsers do: an end tag closes the nearest open element of
/// its name and everything opened inside it, an end tag with no open element of its name is ignored, void elements and
/// self-closing tags open nothing, comments, declarations and the raw text of <c>script</c> and <c>style</c> are skipped, and
/// whatever is still open at the end is closed.
/// </remarks>
internal static class HocrReader
{
    private const string Format = "hOCR";

    /// <summary>The most elements kept open; more nested ones that are not pages, lines or words are not tracked.</summary>
    private const int MaxDepth = 256;

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> LineClasses = new(StringComparer.Ordinal)
    {
        "ocr_line", "ocrx_line", "ocr_caption", "ocr_header", "ocr_textfloat",
    };

    /// <summary>The ISO 639-2 codes Tesseract writes as <c>lang</c>, with their BCP 47 tags; any other value is kept as written.</summary>
    private static readonly Dictionary<string, string> TesseractLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ara"] = "ar",
        ["ces"] = "cs",
        ["chi_sim"] = "zh-Hans",
        ["chi_tra"] = "zh-Hant",
        ["dan"] = "da",
        ["deu"] = "de",
        ["ell"] = "el",
        ["eng"] = "en",
        ["fin"] = "fi",
        ["fra"] = "fr",
        ["heb"] = "he",
        ["hin"] = "hi",
        ["hun"] = "hu",
        ["ita"] = "it",
        ["jpn"] = "ja",
        ["kor"] = "ko",
        ["nld"] = "nl",
        ["nor"] = "no",
        ["pol"] = "pl",
        ["por"] = "pt",
        ["ron"] = "ro",
        ["rus"] = "ru",
        ["spa"] = "es",
        ["swe"] = "sv",
        ["tha"] = "th",
        ["tur"] = "tr",
        ["ukr"] = "uk",
        ["vie"] = "vi",
    };

    private enum Kind
    {
        Other,
        Page,
        Line,
        Word,
    }

    public static IReadOnlyList<OcrPage> Read(byte[] data)
    {
        string text = Decode(data);
        var state = new State();
        bool markup = false;
        int position = 0;
        while (position < text.Length)
        {
            int open = text.IndexOf('<', position);
            if (open < 0)
            {
                state.Text(text.AsSpan(position));
                break;
            }

            state.Text(text.AsSpan(position, open - position));
            position = open;
            ReadOnlySpan<char> rest = text.AsSpan(open);
            if (rest.StartsWith("<!--", StringComparison.Ordinal))
            {
                int end = text.IndexOf("-->", open + 4, StringComparison.Ordinal);
                position = end < 0 ? text.Length : end + 3;
                markup = true;
            }
            else if (rest.Length > 1 && rest[1] is '!' or '?')
            {
                int end = text.IndexOf('>', open + 2);
                position = end < 0 ? text.Length : end + 1;
                markup = true;
            }
            else if (rest.Length > 2 && rest[1] == '/' && char.IsAsciiLetter(rest[2]))
            {
                int end = text.IndexOf('>', open + 2);
                int stop = end < 0 ? text.Length : end;
                state.End(TagName(text.AsSpan(open + 2, stop - open - 2), out _).ToString());
                position = end < 0 ? text.Length : end + 1;
                markup = true;
            }
            else if (rest.Length > 1 && char.IsAsciiLetter(rest[1]))
            {
                position = ReadStartTag(text, open + 1, state);
                markup = true;
            }
            else
            {
                state.Text("<");
                position = open + 1;
            }
        }

        if (!markup)
        {
            throw new FormatException("The hOCR input holds no markup: no element was found.");
        }

        return state.Finish();
    }

    /// <summary>UTF-16 when the input starts with its byte order mark, else UTF-8; a leading byte order mark is dropped.</summary>
    private static string Decode(byte[] data)
    {
        string text = data is [0xFF, 0xFE, ..] ? Encoding.Unicode.GetString(data, 2, data.Length - 2)
            : data is [0xFE, 0xFF, ..] ? Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2)
            : Encoding.UTF8.GetString(data);
        return text.StartsWith('﻿') ? text[1..] : text;
    }

    /// <summary>The element name at the start of <paramref name="tag"/>, and how many characters it takes.</summary>
    private static ReadOnlySpan<char> TagName(ReadOnlySpan<char> tag, out int length)
    {
        length = 0;
        while (length < tag.Length && !char.IsWhiteSpace(tag[length]) && tag[length] is not ('/' or '>'))
        {
            length++;
        }

        return tag[..length];
    }

    /// <summary>Reads a start tag from just after its <c>&lt;</c>; returns the position after its <c>&gt;</c> (and, for <c>script</c> or <c>style</c>, after its raw text).</summary>
    private static int ReadStartTag(string text, int start, State state)
    {
        string name = TagName(text.AsSpan(start), out int length).ToString();
        int position = start + length;
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool selfClosing = false;
        while (position < text.Length)
        {
            char current = text[position];
            if (current == '>')
            {
                position++;
                break;
            }

            if (char.IsWhiteSpace(current))
            {
                position++;
                continue;
            }

            if (current == '/')
            {
                selfClosing = position + 1 < text.Length && text[position + 1] == '>';
                position++;
                continue;
            }

            int nameStart = position;
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('=' or '>' or '/'))
            {
                position++;
            }

            string attribute = text[nameStart..position];
            if (attribute.Length == 0)
            {
                position++;
                continue;
            }

            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            string value = string.Empty;
            if (position < text.Length && text[position] == '=')
            {
                position++;
                while (position < text.Length && char.IsWhiteSpace(text[position]))
                {
                    position++;
                }

                if (position < text.Length && text[position] is '"' or '\'')
                {
                    char quote = text[position];
                    int end = text.IndexOf(quote, position + 1);
                    end = end < 0 ? text.Length : end;
                    value = Unescape(text.AsSpan(position + 1, end - position - 1));
                    position = Math.Min(end + 1, text.Length);
                }
                else
                {
                    int valueStart = position;
                    while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] != '>')
                    {
                        position++;
                    }

                    value = Unescape(text.AsSpan(valueStart, position - valueStart));
                }
            }

            attributes.TryAdd(attribute, value);
        }

        if (name.Equals("script", StringComparison.OrdinalIgnoreCase) || name.Equals("style", StringComparison.OrdinalIgnoreCase))
        {
            if (!selfClosing)
            {
                int end = text.IndexOf("</" + name, position, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    return text.Length;
                }

                int close = text.IndexOf('>', end);
                return close < 0 ? text.Length : close + 1;
            }

            return position;
        }

        state.Start(name, attributes, selfClosing || VoidElements.Contains(name));
        return position;
    }

    /// <summary>Text with its character references resolved: the five XML entities, <c>&amp;nbsp;</c>, and decimal and hexadecimal references.</summary>
    private static string Unescape(ReadOnlySpan<char> text)
    {
        if (text.IndexOf('&') < 0)
        {
            return text.ToString();
        }

        var result = new StringBuilder(text.Length);
        int position = 0;
        while (position < text.Length)
        {
            int amp = text[position..].IndexOf('&');
            if (amp < 0)
            {
                result.Append(text[position..]);
                break;
            }

            result.Append(text.Slice(position, amp));
            position += amp;
            int semicolon = text[position..].IndexOf(';');
            if (semicolon is > 1 and <= 12 && Entity(text.Slice(position + 1, semicolon - 1)) is { } resolved)
            {
                result.Append(resolved);
                position += semicolon + 1;
            }
            else
            {
                result.Append('&');
                position++;
            }
        }

        return result.ToString();
    }

    private static string? Entity(ReadOnlySpan<char> name)
    {
        if (name.Length > 1 && name[0] == '#')
        {
            bool hexadecimal = name[1] is 'x' or 'X';
            ReadOnlySpan<char> digits = hexadecimal ? name[2..] : name[1..];
            return int.TryParse(digits, hexadecimal ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                && Rune.TryCreate(value, out Rune rune) ? rune.ToString() : null;
        }

        return name switch
        {
            "amp" => "&",
            "lt" => "<",
            "gt" => ">",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => " ",
            _ => null,
        };
    }

    /// <summary>The <c>title</c> property <paramref name="name"/> (hOCR 1.2: properties separated by <c>;</c>, a name then its values), or <see langword="null"/>.</summary>
    private static string? Property(string? title, string name)
    {
        if (title is null)
        {
            return null;
        }

        foreach (string part in title.Split(';'))
        {
            ReadOnlySpan<char> trimmed = part.AsSpan().Trim();
            if (trimmed.StartsWith(name, StringComparison.Ordinal) && (trimmed.Length == name.Length || char.IsWhiteSpace(trimmed[name.Length])))
            {
                return trimmed[name.Length..].Trim().ToString();
            }
        }

        return null;
    }

    /// <summary>A <c>bbox</c> value: four numbers, the right and bottom not before the left and top.</summary>
    private static bool TryBox(string? value, out double x0, out double y0, out double x1, out double y1)
    {
        x0 = y0 = x1 = y1 = 0;
        if (value is null)
        {
            return false;
        }

        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4
            && OcrPageBuilder.TryNumber(parts[0], out x0) && OcrPageBuilder.TryNumber(parts[1], out y0)
            && OcrPageBuilder.TryNumber(parts[2], out x1) && OcrPageBuilder.TryNumber(parts[3], out y1)
            && x1 >= x0 && y1 >= y0;
    }

    private static bool HasClass(string? classes, string name)
    {
        if (classes is null)
        {
            return false;
        }

        foreach (string token in classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == name)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLineClass(string? classes)
    {
        if (classes is null)
        {
            return false;
        }

        foreach (string token in classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (LineClasses.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Language(Dictionary<string, string> attributes) =>
        (attributes.TryGetValue("lang", out string? lang) || attributes.TryGetValue("xml:lang", out lang)) && !string.IsNullOrWhiteSpace(lang)
            ? TesseractLanguages.GetValueOrDefault(lang.Trim(), lang.Trim())
            : null;

    /// <summary>An open element: its name, what it is to hOCR, and the language it gives what it holds.</summary>
    private sealed record Frame(string Name, Kind Kind, string? Language);

    /// <summary>The reader's state: the open elements, the page, line and word being read, and the pages read.</summary>
    private sealed class State
    {
        private readonly List<Frame> _open = [];
        private readonly List<OcrPage> _pages = [];
        private readonly StringBuilder _word = new();
        private OcrPageBuilder? _page;
        private double _pageX;
        private double _pageY;
        private List<OcrWord>? _line;
        private double _lineAngle;
        private string? _lineLanguage;
        private bool _inWord;
        private string? _wordTitle;

        public void Start(string name, Dictionary<string, string> attributes, bool closed)
        {
            attributes.TryGetValue("class", out string? classes);
            attributes.TryGetValue("title", out string? title);
            string? language = Language(attributes) ?? (_open.Count > 0 ? _open[^1].Language : null);
            Kind kind = Kind.Other;
            if (HasClass(classes, "ocr_page"))
            {
                ClosePage();
                kind = Kind.Page;
                var diagnostics = new List<Diagnostics.Diagnostic>();
                double? width = null;
                double? height = null;
                _pageX = _pageY = 0;
                if (TryBox(Property(title, "bbox"), out double x0, out double y0, out double x1, out double y1) && x1 > x0 && y1 > y0 && x1 - x0 < 1e9 && y1 - y0 < 1e9)
                {
                    (_pageX, _pageY, width, height) = (x0, y0, x1 - x0, y1 - y0);
                }

                _page = new OcrPageBuilder(Format, width, height, diagnostics);
            }
            else if (_page is not null && HasClass(classes, "ocrx_word"))
            {
                CloseWord();
                kind = Kind.Word;
                _inWord = true;
                _wordTitle = title;
                _word.Clear();
            }
            else if (_page is not null && HasLineClass(classes))
            {
                CloseWord();
                CloseLine();
                kind = Kind.Line;
                _line = [];
                _lineLanguage = language;
                _lineAngle = OcrPageBuilder.TryNumber(Property(title, "textangle"), out double angle) ? angle : 0;
            }

            if (closed)
            {
                // A void or self-closing element holds nothing: a word or line it opened ends at once.
                if (kind == Kind.Word)
                {
                    CloseWord();
                }
                else if (kind == Kind.Line)
                {
                    CloseLine();
                }
                else if (kind == Kind.Page)
                {
                    ClosePage();
                }

                return;
            }

            if (kind == Kind.Other && _open.Count >= MaxDepth)
            {
                // Deeper markup than any hOCR has: left unopened, so a hostile input cannot make end tags search a huge stack.
                return;
            }

            _open.Add(new Frame(name, kind, language));
        }

        public void End(string name)
        {
            int index = _open.FindLastIndex(frame => frame.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            for (int top = _open.Count - 1; top >= index; top--)
            {
                Close(_open[top].Kind);
                _open.RemoveAt(top);
            }
        }

        public void Text(ReadOnlySpan<char> text)
        {
            if (_inWord && !text.IsEmpty)
            {
                _word.Append(Unescape(text));
            }
        }

        public List<OcrPage> Finish()
        {
            for (int top = _open.Count - 1; top >= 0; top--)
            {
                Close(_open[top].Kind);
            }

            _open.Clear();
            ClosePage();
            return _pages;
        }

        private void Close(Kind kind)
        {
            switch (kind)
            {
                case Kind.Word:
                    CloseWord();
                    break;
                case Kind.Line:
                    CloseLine();
                    break;
                case Kind.Page:
                    ClosePage();
                    break;
            }
        }

        private void CloseWord()
        {
            if (!_inWord || _page is null)
            {
                _inWord = false;
                return;
            }

            _inWord = false;
            string text = string.Join(' ', _word.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            _word.Clear();
            string? bbox = Property(_wordTitle, "bbox");
            if (!TryBox(bbox, out double x0, out double y0, out double x1, out double y1))
            {
                _page.BoundsInvalid($"'{OcrPageBuilder.Shorten(text)}' has the bbox '{OcrPageBuilder.Shorten(bbox ?? string.Empty)}'");
                return;
            }

            double confidence = 1;
            if (Property(_wordTitle, "x_wconf") is { } wconf)
            {
                if (OcrPageBuilder.TryNumber(wconf, out double percent) && percent is >= 0 and <= 100)
                {
                    confidence = percent / 100;
                }
                else
                {
                    _page.ConfidenceInvalid(wconf);
                }
            }

            var word = new OcrWord(text, new PdfRectangle(x0 - _pageX, y0 - _pageY, x1 - _pageX, y1 - _pageY), confidence);
            if (_line is not null)
            {
                _line.Add(word);
            }
            else
            {
                // A word outside any line is a line of its own.
                _page.AddLine([word], 0, _open.Count > 0 ? _open[^1].Language : null);
            }
        }

        private void CloseLine()
        {
            CloseWord();
            if (_line is not null)
            {
                _page?.AddLine(_line, _lineAngle, _lineLanguage);
                _line = null;
            }
        }

        private void ClosePage()
        {
            CloseLine();
            if (_page is not null)
            {
                _pages.Add(_page.Build());
                _page = null;
            }
        }
    }
}
