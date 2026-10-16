using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Synthetic content streams for the content interpreter's allocation tests and benchmarks: deterministic, well-formed, and heavy
/// in the operators a path renderer sees most (ISO 32000-2 §8.4.4, §8.5).
/// </summary>
public static class ContentSamples
{
    /// <summary>
    /// A path-heavy content stream of at least <paramref name="minimumLength"/> bytes: blocks of state changes, paths built with
    /// every construction operator, every painting operator, and clips, each block balanced in <c>q</c>/<c>Q</c>.
    /// </summary>
    public static byte[] PathHeavy(int minimumLength)
    {
        var text = new StringBuilder(minimumLength + 512);
        int block = 0;
        while (text.Length < minimumLength)
        {
            double x = block % 500 * 1.25;
            double y = block % 700 * 0.75;
            text.Append(CultureInfo.InvariantCulture, $"q 1 0 0 1 {x} {y} cm {block % 7 + 0.5} w {block % 3} J {block % 3} j 4 M [3 {block % 4 + 1}] 0.5 d /Perceptual ri 1 i\n");
            text.Append(CultureInfo.InvariantCulture, $"0 0 m 100.5 0 l 100 100.25 l 50 150 25 175 0 100 c h S\n");
            text.Append(CultureInfo.InvariantCulture, $"10 10 m 20 30 40 50 v 60 70 80 90 y s\n");
            text.Append(CultureInfo.InvariantCulture, $"{x} {y} 50 -25 re f 0 0 10 10 re f* 5 5 m 6 6 l B 7 7 m 8 8 l B* 1 1 m 2 2 l b 3 3 m 4 4 l b*\n");
            text.Append("0 0 200 200 re W n 0 0 300 300 re W* n BX 1 2 3 newop EX Q\n");
            block++;
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>
    /// A stroke-heavy content stream of at least <paramref name="minimumLength"/> bytes for a Letter page: blocks of wide polylines and
    /// curves cycling through every cap, join and miter limit, solid and dashed (with phases and zero-length dashes), closed and open,
    /// zero-width and stroke-adjusted lines and a non-uniform transform (ISO 32000-2 §8.4.3, §8.5.3.2, §10.7.5). Stroke adjustment
    /// comes from the ExtGState resource <c>/SA</c>; pass <see cref="StrokeHeavyResources"/> to <see cref="OnePage"/>.
    /// </summary>
    public static byte[] StrokeHeavy(int minimumLength)
    {
        var text = new StringBuilder(minimumLength + 512);
        int block = 0;
        while (text.Length < minimumLength)
        {
            double x = block % 11 * 50;
            double y = block % 15 * 50;
            int style = block % 3;
            text.Append(CultureInfo.InvariantCulture, $"q {style} J {(block + 1) % 3} j {1 + (block % 5)} M {(block % 4) * 3 + 0.5} w 0 0 {block % 2} RG\n");
            text.Append(CultureInfo.InvariantCulture, $"{x} {y} m {x + 40} {y + 5} l {x + 10} {y + 30} l {x + 45} {y + 45} l S\n");
            text.Append(CultureInfo.InvariantCulture, $"{x} {y} m {x + 10} {y + 50} {x + 40} {y - 10} {x + 50} {y + 40} c h S\n");
            text.Append(CultureInfo.InvariantCulture, $"[{1 + (block % 6)} 2 0 3] {block % 7} d {x + 5} {y + 5} 40 30 re S {x} {y + 25} m {x + 25} {y + 50} {x + 50} {y + 25} v S\n");
            text.Append(CultureInfo.InvariantCulture, $"[] 0 d 0 w {x} {y + 0.5} m {x + 50} {y + 0.5} l S /SA gs 1 w {x + 0.5} {y} m {x + 0.5} {y + 50} l S\n");
            text.Append(CultureInfo.InvariantCulture, $"3 0 0 1 {x} {y} cm 2 w 1 J 0 0 m 10 10 l 15 0 l S 5 5 m 5 5 l S Q\n");
            block++;
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>Gets the resource entries <see cref="StrokeHeavy"/> needs: an ExtGState <c>/SA</c> that turns stroke adjustment on.</summary>
    public static string StrokeHeavyResources => "/ExtGState << /SA << /SA true >> >>";

    /// <summary>
    /// A text-heavy content stream showing at least <paramref name="glyphs"/> glyphs in the font resource <c>/F1</c>: text objects with
    /// every text state operator, <c>TJ</c> arrays with adjustments, and the positioning and next-line operators (ISO 32000-2 §9.3,
    /// §9.4).
    /// </summary>
    public static byte[] TextHeavy(int glyphs)
    {
        var text = new StringBuilder(glyphs * 2);
        int shown = 0;
        int line = 0;
        while (shown < glyphs)
        {
            text.Append(CultureInfo.InvariantCulture, $"BT /F1 {10 + (line % 3)} Tf {line % 2} Tc 0.5 Tw 95 Tz 12 TL {line % 3} Tr 0 Ts 72 {700 - (line % 50 * 12)} Td\n");
            text.Append("[(Hello, ) -250 (Broadside) 120 (text) -33.5 (show)] TJ T* (word spacing 32) Tj 1 0.5 (quoted) \" (next) '\n");
            text.Append("ET\n");
            shown += 47;
            line++;
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>
    /// A one-page file whose content stream (object 4) is <paramref name="content"/>, whose page resources are
    /// <paramref name="resources"/> (the inside of the dictionary) and whose further objects, numbered from 5, are
    /// <paramref name="objects"/> (ISO 32000-2 §7.5, §7.7.3.3).
    /// </summary>
    public static byte[] OnePage(byte[] content, string resources = "", params string[] objects) => File(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << {resources} >> /Contents 4 0 R >>",
        Stream(Encoding.Latin1.GetString(content)),
        .. objects,
    ]);

    /// <summary>
    /// A tagged page (ISO 32000-2 §14.7, §14.8) of <paramref name="paragraphs"/> lines in Helvetica, each a <c>P</c> element owning one
    /// MCID, painted bottom line first so page content order is the reverse of logical content order (§14.8.2.5.1).
    /// </summary>
    /// <param name="paragraphs">The number of lines and elements.</param>
    /// <returns>The file.</returns>
    public static byte[] TaggedText(int paragraphs)
    {
        var content = new StringBuilder();
        for (int index = paragraphs - 1; index >= 0; index--)
        {
            content.Append(CultureInfo.InvariantCulture, $"/P <</MCID {index}>> BDC BT /F1 10 Tf 72 {780 - (index * 12 % 760)} Td (Paragraph {index} of the tagged sample page) Tj ET EMC\n");
        }

        string elements = string.Join(' ', Enumerable.Range(0, paragraphs).Select(static index => string.Create(CultureInfo.InvariantCulture, $"{index + 8} 0 R")));
        return File(
        [
            "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R /StructParents 0 >>",
            Stream(content.ToString()),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Type /StructTreeRoot /K [7 0 R] /ParentTree << /Nums [0 [{elements}]] >> >>",
            $"<< /Type /StructElem /S /Document /P 6 0 R /K [{elements}] >>",
            .. Enumerable.Range(0, paragraphs).Select(static index => string.Create(CultureInfo.InvariantCulture, $"<< /Type /StructElem /S /P /P 7 0 R /Pg 3 0 R /K {index} >>")),
        ]);
    }

    /// <summary>
    /// Untagged pages of sections (spec story 36, issue #188): on each page three sections of a Helvetica-Bold 14 heading, two lines
    /// of Helvetica 10 body text and a list of four bulleted items, each with a nested list of two lettered items (about 1,200 glyphs
    /// a page).
    /// </summary>
    /// <param name="pages">The number of pages.</param>
    /// <returns>The file.</returns>
    public static byte[] HeadingsAndLists(int pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pages).Select(static page => string.Create(CultureInfo.InvariantCulture, $"{3 + (2 * page)} 0 R")))}] /Count {pages} >>",
        };
        int fonts = 3 + (2 * pages);
        for (int page = 0; page < pages; page++)
        {
            var content = new StringBuilder();
            int y = 740;
            for (int section = 0; section < 3; section++)
            {
                content.Append(CultureInfo.InvariantCulture, $"BT /F2 14 Tf 72 {y} Td (Section {(char)('A' + page)}{(char)('a' + section)}) Tj ET\n");
                y -= 24;
                content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 72 {y} Td (Body text that introduces the list below it, long enough to fill most of) Tj ET\n");
                content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 72 {y - 12} Td (the line it is set on.) Tj ET\n");
                y -= 36;
                for (int item = 0; item < 4; item++)
                {
                    content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 84 {y} Td (\x95) Tj ET BT /F1 10 Tf 96 {y} Td (Item {item + 1} of the list, with a line of text) Tj ET\n");
                    content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 108 {y - 12} Td (a\\)) Tj ET BT /F1 10 Tf 122 {y - 12} Td (a nested item) Tj ET\n");
                    content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 108 {y - 24} Td (b\\)) Tj ET BT /F1 10 Tf 122 {y - 24} Td (another nested item) Tj ET\n");
                    y -= 36;
                }

                y -= 12;
            }

            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fonts} 0 R /F2 {fonts + 1} 0 R >> >> /Contents {4 + (2 * page)} 0 R >>"));
            objects.Add(Stream(content.ToString()));
        }

        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        return File([.. objects]);
    }

    /// <summary>
    /// Untagged pages of tables (spec story 36, issue #189): on each page a line of text, a ruled table of 21 rows and 5 columns (a
    /// bold header row, its first column merged in pairs of rows by left-out lines) and an unruled table of 21 rows and 5 columns
    /// aligned by white space (about 900 glyphs a page).
    /// </summary>
    /// <param name="pages">The number of pages.</param>
    /// <returns>The file.</returns>
    public static byte[] Tables(int pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pages).Select(static page => string.Create(CultureInfo.InvariantCulture, $"{3 + (2 * page)} 0 R")))}] /Count {pages} >>",
        };
        int fonts = 3 + (2 * pages);
        for (int page = 0; page < pages; page++)
        {
            var content = new StringBuilder();
            content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 72 760 Td (Page {page + 1} holds a ruled table and an unruled one.) Tj ET\n0.5 w\n");
            for (int row = 0; row <= 21; row++)
            {
                // The lines between rows 2k - 1 and 2k leave out the first column, which merges those cells.
                int left = row > 1 && row < 21 && row % 2 == 0 ? 152 : 72;
                content.Append(CultureInfo.InvariantCulture, $"{left} {740 - (row * 14)} m 472 {740 - (row * 14)} l S\n");
            }

            foreach (int x in (int[])[72, 152, 232, 312, 392, 472])
            {
                content.Append(CultureInfo.InvariantCulture, $"{x} 740 m {x} 446 l S\n");
            }

            for (int row = 0; row < 21; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    if (column == 0 && row > 1 && row % 2 == 0)
                    {
                        continue;
                    }

                    string cell = row == 0 ? $"Head {column + 1}" : column == 0 ? $"Group {row}" : $"{(row * 37) + (column * 11)}.{row % 10}";
                    content.Append(CultureInfo.InvariantCulture, $"BT /F{(row == 0 ? 2 : 1)} 9 Tf {76 + (column * 80)} {730 - (row * 14)} Td ({cell}) Tj ET\n");
                }
            }

            for (int row = 0; row < 21; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    string cell = row == 0 ? $"Col {column + 1}" : column == 0 ? $"Name {row}" : $"{(row * 13) + column}";
                    content.Append(CultureInfo.InvariantCulture, $"BT /F{(row == 0 ? 2 : 1)} 9 Tf {72 + (column * 90)} {400 - (row * 13)} Td ({cell}) Tj ET\n");
                }
            }

            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fonts} 0 R /F2 {fonts + 1} 0 R >> >> /Contents {4 + (2 * page)} 0 R >>"));
            objects.Add(Stream(content.ToString()));
        }

        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        return File([.. objects]);
    }

    private static readonly string[] Ordinals = ["first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth"];

    /// <summary>
    /// Untagged pages of figures (spec story 36, issue #190): on each page two lines of text, a bar chart of 20 bars on two axes with a
    /// label under each bar and a caption below, a scatter plot of 200 small circles drawn with curves in a frame, captioned below,
    /// an image captioned above, and a closing line (about 400 glyphs and 250 graphics a page).
    /// </summary>
    /// <param name="pages">The number of pages.</param>
    /// <returns>The file.</returns>
    public static byte[] Figures(int pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pages).Select(static page => string.Create(CultureInfo.InvariantCulture, $"{3 + (2 * page)} 0 R")))}] /Count {pages} >>",
        };
        int fonts = 3 + (2 * pages);
        for (int page = 0; page < pages; page++)
        {
            var content = new StringBuilder();
            // Words that differ from page to page, so no line recurs as a running header or footer.
            string ordinal = Ordinals[page % Ordinals.Length];
            content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 72 760 Td (The {ordinal} page holds a bar chart, a scatter plot and an image,) Tj 0 -12 Td (each with a caption found by its label.) Tj ET\n0.5 w\n");
            content.Append("72 560 m 72 720 l S 72 560 m 540 560 l S\n");
            for (int bar = 0; bar < 20; bar++)
            {
                double x = 80 + (bar * 23);
                content.Append(CultureInfo.InvariantCulture, $"0.{(bar % 9) + 1} 0.4 0.7 rg {x} 560 16 {20 + ((bar * 37) % 130)} re f 0 g\n");
                content.Append(CultureInfo.InvariantCulture, $"BT /F1 7 Tf {x + 2} 552 Td (B{bar + 1}) Tj ET\n");
            }

            content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 72 530 Td (Figure {(page * 3) + 1}: Twenty bars over two axes.) Tj ET\n");
            content.Append("72 300 300 200 re S\n");
            for (int point = 0; point < 200; point++)
            {
                double x = 80 + ((point * 53) % 285);
                double y = 310 + ((point * 31) % 180);
                content.Append(CultureInfo.InvariantCulture, $"{x} {y} m {x + 2} {y + 2} {x + 4} {y - 2} {x} {y} c f\n");
            }

            content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 72 280 Td (Figure {(page * 3) + 2}. Two hundred points in a frame.) Tj ET\n");
            content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 400 470 Td (Figure {(page * 3) + 3}: An image.) Tj ET\n");
            content.Append("q 120 0 0 120 400 330 cm BI /W 2 /H 2 /CS /G /BPC 8 /F /AHx ID 00FF7F20> EI Q\n");
            content.Append(CultureInfo.InvariantCulture, $"BT /F1 10 Tf 72 112 Td (Closing lines of running text end the {ordinal} page,) Tj 0 -12 Td (the {ordinal} of its kind.) Tj ET\n");
            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fonts} 0 R /F2 {fonts + 1} 0 R >> >> /Contents {4 + (2 * page)} 0 R >>"));
            objects.Add(Stream(content.ToString()));
        }

        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        return File([.. objects]);
    }

    /// <summary>A file of the objects, numbered from 1 (object 1 the catalog), with a cross-reference table (ISO 32000-2 §7.5).</summary>
    public static byte[] File(string[] objects)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(text.Length);
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>
    /// A one-page file that paints images every way the rasterizer resamples them (ISO 32000-2 §8.9): a 256 x 256 8-bit RGB
    /// gradient (the byte conversion path) enlarged with and without Interpolate, reduced, and under a rotation; a 64 x 64 16-bit
    /// RGB image and a 64 x 64 8-bit gray image with an inverted Decode array (the component-value path); a 4-bit Indexed image; an
    /// inline image. Each image XObject is painted at least twice.
    /// </summary>
    public static byte[] ImageHeavy()
    {
        var rgb = new StringBuilder(256 * 256 * 3);
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                rgb.Append((char)x).Append((char)y).Append((char)((x * y) & 0xFF));
            }
        }

        var deep = new StringBuilder(64 * 64 * 6);
        var gray = new StringBuilder(64 * 64);
        for (int i = 0; i < 64 * 64; i++)
        {
            int value = i * 16;
            deep.Append((char)(value >> 8)).Append((char)(value & 0xFF)).Append((char)(i & 0xFF)).Append((char)(i >> 4)).Append((char)0x80).Append((char)0x01);
            gray.Append((char)((i * 7) & 0xFF));
        }

        var indices = new StringBuilder(32 * 16);
        for (int i = 0; i < 32 * 16; i++)
        {
            indices.Append((char)(((i & 15) << 4) | ((i >> 4) & 15)));
        }

        var palette = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            palette.Append(CultureInfo.InvariantCulture, $"{i * 17:X2}{255 - (i * 17):X2}{(i * 51) % 256:X2}");
        }

        const string content =
            "q 300 0 0 300 20 470 cm /Rgb Do Q q 200 0 0 200 340 570 cm /Smooth Do Q q 100 0 0 100 340 450 cm /Rgb Do Q "
            + "q 106 106 -106 106 480 300 cm /Smooth Do Q q 250 0 0 250 20 200 cm /Deep Do Q q 120 0 0 120 300 300 cm /Deep Do Q "
            + "q 150 0 0 150 300 100 cm /Gray Do Q q 40 0 0 40 500 100 cm /Gray Do Q q 180 0 0 120 20 20 cm /Indexed Do Q "
            + "q 100 0 0 100 450 20 cm BI /W 2 /H 2 /CS /G /BPC 8 /I true ID \u0000ÿÿ\u0000 EI Q";
        const string image = "/Type /XObject /Subtype /Image ";
        return OnePage(
            Encoding.Latin1.GetBytes(content),
            "/XObject << /Rgb 5 0 R /Smooth 6 0 R /Deep 7 0 R /Gray 8 0 R /Indexed 9 0 R >>",
            Stream(rgb.ToString(), image + "/Width 256 /Height 256 /ColorSpace /DeviceRGB /BitsPerComponent 8"),
            Stream(rgb.ToString(), image + "/Width 256 /Height 256 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Interpolate true"),
            Stream(deep.ToString(), image + "/Width 64 /Height 64 /ColorSpace /DeviceRGB /BitsPerComponent 16"),
            Stream(gray.ToString(), image + "/Width 64 /Height 64 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [1 0]"),
            Stream(indices.ToString(), image + $"/Width 32 /Height 32 /BitsPerComponent 4 /ColorSpace [/Indexed /DeviceRGB 15 <{palette}>]"));
    }

    /// <summary>A stream object whose data is <paramref name="data"/> (Latin-1), with <paramref name="entries"/> in its dictionary.</summary>
    public static string Stream(string data, string entries = "") =>
        $"<< {entries} /Length {Encoding.Latin1.GetByteCount(data)} >>\nstream\n{data}\nendstream";
}
