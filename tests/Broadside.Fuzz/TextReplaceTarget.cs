using Broadside.Objects;
using Broadside.Text;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>text-replace</c> target (issue #641): the input is a page's decoded content, with Helvetica as <c>/F1</c>. Up to three words
/// of the page's text are replaced, each by a word of letters taken from the input, in the fit mode the first byte chooses, then the
/// last is deleted. Invariant: the edit never throws, the page's content still reads, and under <see cref="PdfTextFit.ShiftLine"/>
/// (no overlap) a replaced word's replacement is in the page's text afterwards.
/// </summary>
/// <remarks>ISO 32000-2 §9.4.3 (Table 107), §9.3.4, §9.10 and §14.9.4.</remarks>
internal static class TextReplaceTarget
{
    private static readonly CosName Contents = new("Contents");
    private static readonly CosName Resources = new("Resources");
    private static readonly CosName Font = new("Font");

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        page.Dictionary[Contents] = document.AddObject(new CosStream(new CosDictionary(), data.ToArray()));
        var helvetica = new CosDictionary
        {
            [new CosName("Type")] = Font,
            [new CosName("Subtype")] = new CosName("Type1"),
            [new CosName("BaseFont")] = new CosName("Helvetica"),
            [new CosName("Encoding")] = new CosName("WinAnsiEncoding"),
        };
        page.Dictionary[Resources] = new CosDictionary { [Font] = new CosDictionary { [new CosName("F1")] = helvetica } };

        PdfTextFit fit = data.IsEmpty ? PdfTextFit.KeepPosition : (PdfTextFit)(data[0] % 3);
        var options = new PdfTextReplaceOptions().WithFit(fit);
        TextPage before = page.ExtractText();
        List<string> words = [.. before.Words.Select(word => before.Text.Substring(word.TextStart, word.TextLength)).Where(static word => word.Any(char.IsLetterOrDigit)).Distinct().Take(3)];
        for (int index = 0; index < words.Count; index++)
        {
            string replacement = Replacement(data, index);
            PdfTextEditResult result = document.ReplaceText(new TextQuery(words[index]).WithMatchCase().WithWholeWord(), replacement, options);
            string text = page.ExtractText().Text;
            if (fit == PdfTextFit.ShiftLine && result.Replaced > 0 && result.Diagnostics.Count == 0 && !text.Contains(replacement, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"After replacing a word with '{replacement}' the page's text does not hold it.");
            }
        }

        if (words.Count > 0)
        {
            _ = document.DeleteText(new TextQuery(words[^1]).WithMatchCase(), options);
            _ = page.ExtractText().Text;
        }
    }

    /// <summary>A word of two to six letters from the input's bytes.</summary>
    private static string Replacement(ReadOnlySpan<byte> data, int index)
    {
        int length = 2 + (data.Length > index + 1 ? data[index + 1] % 5 : 0);
        Span<char> word = stackalloc char[length];
        for (int position = 0; position < length; position++)
        {
            int source = (index * 7) + position;
            word[position] = (char)('a' + ((source < data.Length ? data[source] : source) % 26));
        }

        return new string(word);
    }
}
