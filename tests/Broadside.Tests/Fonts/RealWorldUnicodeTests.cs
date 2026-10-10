using System.Buffers;
using System.Text;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Every glyph every page of the fetched real-world corpora shows maps to Unicode leniently (ISO 32000-2 §9.10): no exception,
/// well-formed UTF-16 of at most 512 units, exactly U+FFFD when nothing maps the code. Prints how many glyphs each method of
/// §9.10.2 mapped. Skips when the corpora are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldUnicodeTests(ITestOutputHelper output)
{
    public static TheoryData<string> CorpusIds => new(Document.RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_glyph_of_a_real_world_corpus_maps_to_well_formed_Unicode(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var counter = new Counter();
        foreach (string file in files)
        {
            if (Document.RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                continue;
            }

            using PdfDocument? document = OpenOrNull(file);
            if (document is null)
            {
                continue;
            }

            counter.File = file;
            foreach (PdfPage page in document.Pages)
            {
                page.ProcessContent(counter);
            }
        }

        Assert.Null(counter.Failure);
        output.WriteLine($"{corpusId}: {string.Join(", ", counter.Sources.Select((count, source) => $"{(UnicodeSource)source} {count}"))}.");
    }

    private static PdfDocument? OpenOrNull(string file)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed class Counter : ContentProcessor
    {
        public long[] Sources { get; } = new long[5];

        public string File { get; set; } = string.Empty;

        public string? Failure { get; private set; }

        public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.HiddenContent;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
        {
            ReadOnlySpan<char> text = glyph.Unicode;
            UnicodeSource source = glyph.UnicodeSource;
            Sources[(int)source]++;
            if (Failure is null && (text.Length > 512 || !IsWellFormed(text) || (source == UnicodeSource.Unmapped && !text.SequenceEqual("�"))))
            {
                Failure = $"{File}: code 0x{glyph.CharacterCode:X} maps to {text.Length} units from {source}.";
            }
        }

        private static bool IsWellFormed(ReadOnlySpan<char> text)
        {
            while (!text.IsEmpty)
            {
                if (Rune.DecodeFromUtf16(text, out _, out int consumed) != OperationStatus.Done)
                {
                    return false;
                }

                text = text[consumed..];
            }

            return true;
        }
    }
}
