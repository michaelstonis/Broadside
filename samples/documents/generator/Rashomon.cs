using System.Globalization;
using System.Text;
using System.Text.Json;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// Akutagawa's Rashōmon (1915, public domain) set as a Japanese paperback (bunko) in vertical writing: columns read top to bottom
/// and right to left, with ruby (furigana) beside the kanji they gloss, the Latin word in the text turned on its side as Japanese
/// typesetters do, and the pages bound on the right (<c>Direction R2L</c>). Broadside lays the text out with a CID-keyed CFF font
/// (Noto Serif CJK JP, shaped with the <c>vert</c> feature); a second pass over the saved file makes it a vertical font, as a
/// vertical producer writes it: <c>Identity-V</c> encoding, and <c>DW2</c> and <c>W2</c> from the font's vertical metrics. The colophon
/// is horizontal and uses a font that is not embedded, Ryumin-Light with the predefined <c>UniJIS-UTF16-H</c> CMap, as older
/// Japanese files do.
/// </summary>
internal static class Rashomon
{
    private const double Width = 105 * 72 / 25.4;
    private const double Height = 148 * 72 / 25.4;
    private const double Size = 9;
    private const double Pitch = Size * 1.8;
    private const double RubySize = Size / 2;
    private const double Top = Height - (16 * 72 / 25.4);
    private const double Bottom = 13 * 72 / 25.4;
    private const double RightEdge = Width - (11 * 72 / 25.4);
    private const double LeftEdge = 11 * 72 / 25.4;
    private const string FontName = "NotoSerifCJKjp-Regular";

    // Characters that may not start a column: they hang below the last full cell instead (burasagari).
    private const string NoStart = "、。，．」』）〕】ゝゞ々ーぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ";

    private sealed record Text(string Title, string Author, string Date, JsonElement[][] Paragraphs, string[] Colophon);

    /// <summary>One unit of the column: plain characters, a ruby group, or a Latin word set sideways.</summary>
    private sealed record Unit(string Base, string? Ruby, bool Sideways, double Length);

    public static void Write(GeneratorContext context)
    {
        Text text = JsonSerializer.Deserialize<Text>(File.ReadAllText(Path.Combine(context.Root, "rashomon", "rashomon.json")),
            JsonOptions)!;
        byte[] laidOut;
        using (PdfDocument document = PdfDocument.Create())
        {
            document.Pages.RemoveAt(0);
            SetProperties(document, "羅生門", "Rashōmon (Akutagawa, 1915) in vertical writing, a public-domain text from Aozora Bunko.");
            document.Properties.Author = text.Author;
            PdfCanvasFont serif = context.Font(document, "notoserifcjk/NotoSerifCJKjp-Regular.otf");
            PdfCanvasFont latin = context.Font(document, "ebgaramond/EBGaramond-Italic.otf");
            PdfCanvasFont folio = context.Font(document, "ebgaramond/EBGaramond-Regular.otf");
            TextShaping vertical = TextShaping.Default.WithFeature("vert", true).WithScript("kana").WithLanguage("JAN");

            TitlePage(document, serif, vertical, text);
            Body(document, serif, latin, folio, vertical, text);
            Colophon(document, text);

            document.Catalog[new CosName("ViewerPreferences")] = new CosDictionary { [new CosName("Direction")] = new CosName("R2L") };
            document.Catalog[new CosName("PageLayout")] = new CosName("TwoPageRight");
            document.Catalog[new CosName("Lang")] = new CosString(Encoding.ASCII.GetBytes("ja"));
            using var stream = new MemoryStream();
            document.Save(stream);
            laidOut = stream.ToArray();
        }

        MakeVertical(context, laidOut, context.OutputPath("rashomon-vertical.pdf"));
    }

    private static void TitlePage(PdfDocument document, PdfCanvasFont serif, TextShaping vertical, Text text)
    {
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Width, Height));
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.SetFillColor(Gray(0.1));
        Column(canvas, serif, vertical, 26, (Width / 2) + 22, Height - 110, Spaced(text.Title));
        Column(canvas, serif, vertical, 11, (Width / 2) - 20, Height - 200, text.Author);
        canvas.SetStrokeColor(Gray(0.35));
        canvas.SetLineWidth(0.4);
        canvas.MoveTo(Width / 2, Height - 120);
        canvas.LineTo(Width / 2, 110);
        canvas.Stroke();
    }

    private static string Spaced(string title) => string.Join("　", title.ToCharArray().Select(static c => c.ToString()));

    private static void Body(PdfDocument document, PdfCanvasFont serif, PdfCanvasFont latin, PdfCanvasFont folio, TextShaping vertical, Text text)
    {
        double capacity = Math.Floor((Top - Bottom) / Size);
        var columns = new List<List<Unit>>();
        foreach (JsonElement[] paragraph in text.Paragraphs)
        {
            var current = new List<Unit>();
            double used = 0;
            foreach (Unit unit in Units(paragraph, latin))
            {
                bool hangs = unit.Ruby is null && !unit.Sideways && unit.Base.Length == 1 && NoStart.Contains(unit.Base[0], StringComparison.Ordinal);
                if (used + unit.Length > capacity + 0.01 && !(hangs && used <= capacity + 0.01))
                {
                    columns.Add(current);
                    current = [];
                    used = 0;
                }

                current.Add(unit);
                used += unit.Length;
            }

            columns.Add(current);
        }

        // The date, set one character up from the foot of its own column (地から１字上げ).
        double dateLength = text.Date.Length;
        columns.Add([new Unit(new string('　', (int)(capacity - dateLength - 1)), null, false, capacity - dateLength - 1), new Unit(text.Date, null, false, dateLength)]);

        int perPage = (int)Math.Floor((RightEdge - LeftEdge) / Pitch) + 1;
        int pageNumber = 1;
        for (int first = 0; first < columns.Count; first += perPage)
        {
            PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Width, Height));
            pageNumber++;
            using PdfCanvas canvas = page.CreateCanvas();
            canvas.SetFillColor(Gray(0.08));
            for (int i = first; i < Math.Min(columns.Count, first + perPage); i++)
            {
                double x = RightEdge - ((i - first) * Pitch) - (Size / 2);
                double y = Top;
                foreach (Unit unit in columns[i])
                {
                    if (unit.Sideways)
                    {
                        canvas.SaveState();
                        canvas.Translate(x - (Size * 0.32), y);
                        canvas.Rotate(-90);
                        canvas.BeginText();
                        canvas.SetFont(latin, Size);
                        canvas.MoveText(0, 0);
                        canvas.ShowText(unit.Base);
                        canvas.EndText();
                        canvas.RestoreState();
                    }
                    else
                    {
                        Column(canvas, serif, vertical, Size, x, y, unit.Base);
                        if (unit.Ruby is not null)
                        {
                            double rubyLength = unit.Ruby.Length * RubySize;
                            Column(canvas, serif, vertical, RubySize, x + (Size / 2) + (RubySize / 2) + 0.3, y - (((unit.Base.Length * Size) - rubyLength) / 2), unit.Ruby);
                        }
                    }

                    y -= unit.Length * Size;
                }
            }

            // The running head (the title, small, top outside corner) and the folio at the foot, as in a bunko.
            bool left = pageNumber % 2 == 0;
            Column(canvas, serif, vertical, 6.5, left ? LeftEdge - 14 : RightEdge + 14, Top, text.Title);
            string number = pageNumber.ToString(CultureInfo.InvariantCulture);
            canvas.BeginText();
            canvas.SetFont(folio, 7.5);
            canvas.MoveText((Width / 2) - (folio.MeasureWidth(number, 7.5) / 2), Bottom - 20);
            canvas.ShowText(number);
            canvas.EndText();
        }
    }

    private static IEnumerable<Unit> Units(JsonElement[] paragraph, PdfCanvasFont latin)
    {
        foreach (JsonElement part in paragraph)
        {
            if (part.ValueKind == JsonValueKind.Array)
            {
                string bas = part[0].GetString()!;
                yield return new Unit(bas, part[1].GetString(), false, bas.Length);
                continue;
            }

            string s = part.GetString()!;
            int i = 0;
            while (i < s.Length)
            {
                if (char.IsAsciiLetter(s[i]))
                {
                    int end = i;
                    while (end < s.Length && (char.IsAsciiLetter(s[end]) || s[end] == ' ') && !(s[end] == ' ' && (end + 1 >= s.Length || !char.IsAsciiLetter(s[end + 1]))))
                    {
                        end++;
                    }

                    string word = s[i..end];
                    yield return new Unit(word, null, true, Math.Ceiling(latin.MeasureWidth(word, Size) / Size * 2) / 2);
                    i = end;
                    continue;
                }

                if (s[i] == ' ')
                {
                    yield return new Unit(" ", null, false, 0.25);
                    i++;
                    continue;
                }

                yield return new Unit(s[i].ToString(), null, false, 1);
                i++;
            }
        }
    }

    // Shows a run of text whose glyphs stack downward from (x, top): x is the column's center line, top the top of the first em.
    // Written with the horizontal Identity-H font Broadside embeds; MakeVertical turns the font vertical, so these positions hold.
    private static void Column(PdfCanvas canvas, PdfCanvasFont font, TextShaping vertical, double size, double x, double top, string text)
    {
        if (string.IsNullOrWhiteSpace(text.Replace('　', ' ')))
        {
            return;
        }

        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.SetTextMatrix(new Matrix(1, 0, 0, 1, x, top));
        canvas.ShowText(font.Shape(text, vertical));
        canvas.EndText();
    }

    private static void Colophon(PdfDocument document, Text text)
    {
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Width, Height));
        var content = new StringBuilder();
        content.Append("q 0.3 G 0.4 w ").Append(Num(LeftEdge)).Append(' ').Append(Num(Height - 60)).Append(" m ").Append(Num(RightEdge)).Append(' ').Append(Num(Height - 60)).Append(" l S Q\n");
        content.Append("BT 0.1 g /F1 11 Tf 1 0 0 1 ").Append(Num(LeftEdge)).Append(' ').Append(Num(Height - 52)).Append(" Tm ").Append(Hex(text.Title + "　" + text.Author)).Append(" Tj ET\n");
        string[] lines =
        [
            .. text.Colophon.Where(static l => !l.StartsWith("このファイルは", StringComparison.Ordinal)).Select(static l => l.Trim()),
            "",
            "この PDF は Broadside のサンプル文書として、",
            "青空文庫のテキストを縦書きで組んだものです。",
        ];
        double y = Height - 78;
        foreach (string line in lines)
        {
            if (line.Length > 0)
            {
                content.Append("BT 0.15 g /F1 6.5 Tf 1 0 0 1 ").Append(Num(LeftEdge)).Append(' ').Append(Num(y)).Append(" Tm ").Append(Hex(line)).Append(" Tj ET\n");
            }

            y -= 10.5;
        }

        var descriptor = new CosDictionary
        {
            [new CosName("Type")] = new CosName("FontDescriptor"),
            [new CosName("FontName")] = new CosName("Ryumin-Light"),
            [new CosName("Flags")] = new CosInteger(6),
            [new CosName("FontBBox")] = new CosArray([new CosInteger(-170), new CosInteger(-331), new CosInteger(1024), new CosInteger(903)]),
            [new CosName("ItalicAngle")] = new CosInteger(0),
            [new CosName("Ascent")] = new CosInteger(723),
            [new CosName("Descent")] = new CosInteger(-241),
            [new CosName("CapHeight")] = new CosInteger(709),
            [new CosName("StemV")] = new CosInteger(69),
        };
        var cidFont = new CosDictionary
        {
            [new CosName("Type")] = new CosName("Font"),
            [new CosName("Subtype")] = new CosName("CIDFontType0"),
            [new CosName("BaseFont")] = new CosName("Ryumin-Light"),
            [new CosName("CIDSystemInfo")] = new CosDictionary
            {
                [new CosName("Registry")] = new CosString(Encoding.ASCII.GetBytes("Adobe")),
                [new CosName("Ordering")] = new CosString(Encoding.ASCII.GetBytes("Japan1")),
                [new CosName("Supplement")] = new CosInteger(6),
            },
            [new CosName("FontDescriptor")] = document.AddObject(descriptor),
            [new CosName("DW")] = new CosInteger(1000),
            [new CosName("W")] = new CosArray([new CosInteger(1), new CosInteger(631), new CosInteger(500)]),
        };
        var type0 = new CosDictionary
        {
            [new CosName("Type")] = new CosName("Font"),
            [new CosName("Subtype")] = new CosName("Type0"),
            [new CosName("BaseFont")] = new CosName("Ryumin-Light-UniJIS-UTF16-H"),
            [new CosName("Encoding")] = new CosName("UniJIS-UTF16-H"),
            [new CosName("DescendantFonts")] = new CosArray([document.AddObject(cidFont)]),
        };
        page.Dictionary[new CosName("Resources")] = new CosDictionary
        {
            [new CosName("Font")] = new CosDictionary { [new CosName("F1")] = document.AddObject(type0) },
        };
        page.Dictionary[new CosName("Contents")] = document.AddObject(new CosStream(new CosDictionary(), Encoding.ASCII.GetBytes(content.ToString())));
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Hex(string text) => "<" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text)) + ">";

    /// <summary>
    /// Makes the embedded CJK font vertical: <c>Identity-V</c> instead of <c>Identity-H</c>, and the vertical metrics (<c>DW2</c>, and
    /// <c>W2</c> for the glyphs that differ from the default) from fonts/notoserifcjk/vertical-metrics.json, keyed by the glyph ids
    /// that Broadside uses as CIDs.
    /// </summary>
    private static void MakeVertical(GeneratorContext context, byte[] laidOut, string target)
    {
        using JsonDocument metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.Root, "fonts", "notoserifcjk", "vertical-metrics.json")));
        var w2 = new CosArray();
        foreach (JsonProperty glyph in metrics.RootElement.GetProperty("glyphs").EnumerateObject().OrderBy(static g => int.Parse(g.Name, CultureInfo.InvariantCulture)))
        {
            int advance = glyph.Value[0].GetInt32();
            int width = glyph.Value[1].GetInt32();
            int origin = glyph.Value[2].GetInt32();
            w2.Add(new CosInteger(int.Parse(glyph.Name, CultureInfo.InvariantCulture)));
            w2.Add(new CosArray([new CosInteger(-advance), new CosReal(width / 2.0), new CosInteger(origin)]));
        }

        using PdfDocument document = PdfDocument.Open(laidOut);
        var done = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        int converted = 0;
        foreach (PdfPage page in document.Pages)
        {
            if (!page.Dictionary.TryGetValue(new CosName("Resources"), out CosObject? resources) ||
                document.Resolve(resources) is not CosDictionary resourceDictionary ||
                !resourceDictionary.TryGetValue(new CosName("Font"), out CosObject? fonts) ||
                document.Resolve(fonts) is not CosDictionary fontDictionary)
            {
                continue;
            }

            foreach (KeyValuePair<CosName, CosObject> entry in fontDictionary)
            {
                if (document.Resolve(entry.Value) is not CosDictionary font || !done.Add(font) ||
                    document.Resolve(font[new CosName("BaseFont")]) is not CosName baseFont || !baseFont.Value.Contains(FontName, StringComparison.Ordinal) ||
                    !font.ContainsKey(new CosName("DescendantFonts")))
                {
                    continue;
                }

                font[new CosName("Encoding")] = new CosName("Identity-V");
                var descendant = (CosDictionary)document.Resolve(((CosArray)document.Resolve(font[new CosName("DescendantFonts")]))[0]);
                descendant[new CosName("DW2")] = new CosArray([new CosInteger(880), new CosInteger(-1000)]);
                descendant[new CosName("W2")] = w2;
                converted++;
            }
        }

        if (converted == 0)
        {
            throw new InvalidOperationException("The vertical font was not found in the laid-out file.");
        }

        document.Save(target, new PdfSaveOptions().WithUpdateModificationDate(false));
    }
}
