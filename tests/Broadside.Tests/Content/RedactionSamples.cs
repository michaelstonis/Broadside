using System.Text;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>Files and checks shared by the redaction tests (issue #624).</summary>
internal static class RedactionSamples
{
    /// <summary>Gets a Helvetica font dictionary with WinAnsiEncoding.</summary>
    public const string Helvetica = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

    /// <summary>A one-page file whose content is <paramref name="content"/>, with Helvetica as <c>/F1</c> (object 5) and more objects from 6.</summary>
    public static PdfDocument Open(string content, string resources = "", params string[] objects) =>
        PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), "/Font << /F1 5 0 R >> " + resources, [Helvetica, .. objects]));

    /// <summary>A rectangle as an area.</summary>
    public static Quad Area(double left, double bottom, double right, double top) => Quad.FromRectangle(Matrix.Identity, left, bottom, right, top);

    /// <summary>Saves with the result's save options, uncompressed so the bytes can be searched, and returns the file.</summary>
    public static byte[] Save(PdfDocument document, PdfRedactionResult result)
    {
        using var stream = new MemoryStream();
        document.Save(stream, result.CreateSaveOptions().WithStreamCompression(PdfStreamCompression.None));
        return stream.ToArray();
    }

    /// <summary>Returns the page's content, decoded and joined.</summary>
    public static string Content(PdfDocument document, PdfPage page)
    {
        CosObject contents = document.Resolve(page.Dictionary[new CosName("Contents")]);
        IEnumerable<CosObject> parts = contents is CosArray array ? array : [contents];
        return string.Join("\n", parts.Select(part => Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(part)).Span)));
    }

    /// <summary>Returns the decoded data of every stream reachable from the trailer of <paramref name="file"/>.</summary>
    public static List<string> ReachableStreams(byte[] file)
    {
        using PdfDocument document = PdfDocument.Open(file);
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        var streams = new List<string>();
        var pending = new Stack<CosObject>();
        pending.Push(document.Trailer);
        while (pending.TryPop(out CosObject? value))
        {
            CosObject resolved = document.Resolve(value);
            if (!seen.Add(resolved))
            {
                continue;
            }

            switch (resolved)
            {
                case CosStream stream:
                    streams.Add(Encoding.Latin1.GetString(document.DecodeStream(stream).Span));
                    pending.Push(stream.Dictionary);
                    break;
                case CosDictionary dictionary:
                    foreach (KeyValuePair<CosName, CosObject> pair in dictionary)
                    {
                        pending.Push(pair.Value);
                    }

                    break;
                case CosArray array:
                    foreach (CosObject element in array)
                    {
                        pending.Push(element);
                    }

                    break;
            }
        }

        return streams;
    }

    /// <summary>Asserts that neither the file's bytes nor any stream reachable in it holds <paramref name="secret"/>, as text or as hexadecimal codes.</summary>
    public static void AssertGone(byte[] file, string secret)
    {
        string hex = Convert.ToHexString(Encoding.Latin1.GetBytes(secret));
        string raw = Encoding.Latin1.GetString(file);
        Assert.DoesNotContain(secret, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(hex, raw, StringComparison.OrdinalIgnoreCase);
        foreach (string stream in ReachableStreams(file))
        {
            Assert.DoesNotContain(secret, stream, StringComparison.Ordinal);
            Assert.DoesNotContain(hex, stream, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Gets the text a page shows inside <paramref name="area"/>.</summary>
    public static string TextIn(PdfPage page, PdfRectangle area) =>
        string.Concat(page.ExtractText().Glyphs
            .Where(glyph => glyph.Quad.Bounds.Right > area.Left && glyph.Quad.Bounds.Left < area.Right && glyph.Quad.Bounds.Top > area.Bottom && glyph.Quad.Bounds.Bottom < area.Top)
            .Select(glyph => glyph.CharacterCode < 128 ? ((char)glyph.CharacterCode).ToString() : "?"));
}
