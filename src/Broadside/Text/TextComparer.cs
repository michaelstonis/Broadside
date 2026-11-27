using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Text;

/// <summary>
/// Compares two documents' words: each document's pages extracted in reading order and flattened into one sequence of words, the
/// words compared by identity (their text folded as the options ask), the shortest edit script found by <see cref="MyersDiff"/>, and
/// the script's runs turned into <see cref="TextChange"/>s, one per page of each side.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.2.6.2 (word breaks) and §14.8.2.2 (artifacts).</remarks>
internal static class TextComparer
{
    public static TextComparison Compare(PdfDocument oldDocument, PdfDocument newDocument, TextComparisonOptions options, CancellationToken cancellationToken)
    {
        var identities = new Dictionary<string, int>(StringComparer.Ordinal);
        TextExtractionOptions extraction = new TextExtractionOptions()
            .WithArtifacts(options.IncludeArtifacts)
            .WithContentOptions(new ContentOptions().WithCancellation(cancellationToken));
        WordSequence old = WordSequence.Read(oldDocument, extraction, options, identities, cancellationToken);
        WordSequence @new = WordSequence.Read(newDocument, extraction, options, identities, cancellationToken);

        var changes = new List<TextChange>();
        var deleted = new bool[old.Count];
        var inserted = new bool[@new.Count];
        if (MyersDiff.TryDiff(old.Identities, @new.Identities, deleted, inserted, options.MaxEditDistance, cancellationToken))
        {
            Collect(old, 0, old.Count, deleted, @new, 0, @new.Count, inserted, changes);
            return new TextComparison(changes.AsReadOnly(), comparedByPage: false);
        }

        // Past the bound: each page with the page of the same index in the other document, without a bound (a page is short).
        Array.Clear(deleted);
        Array.Clear(inserted);
        int pages = Math.Max(old.PageCount, @new.PageCount);
        for (int page = 0; page < pages; page++)
        {
            (int oldStart, int oldEnd) = old.PageRange(page);
            (int newStart, int newEnd) = @new.PageRange(page);
            MyersDiff.TryDiff(
                old.Identities[oldStart..oldEnd],
                @new.Identities[newStart..newEnd],
                deleted.AsSpan(oldStart, oldEnd - oldStart),
                inserted.AsSpan(newStart, newEnd - newStart),
                int.MaxValue,
                cancellationToken);
            Collect(old, oldStart, oldEnd, deleted, @new, newStart, newEnd, inserted, changes);
        }

        return new TextComparison(changes.AsReadOnly(), comparedByPage: true);
    }

    /// <summary>Walks an edit script over two ranges of words and adds its runs as changes.</summary>
    private static void Collect(WordSequence old, int i, int oldEnd, bool[] deleted, WordSequence @new, int j, int newEnd, bool[] inserted, List<TextChange> changes)
    {
        while (i < oldEnd || j < newEnd)
        {
            int oldStart = i, newStart = j;

            // A run of deletions and insertions with no common word between them (the script may interleave the two) is one change.
            while ((i < oldEnd && deleted[i]) || (j < newEnd && inserted[j]))
            {
                while (i < oldEnd && deleted[i])
                {
                    i++;
                }

                while (j < newEnd && inserted[j])
                {
                    j++;
                }
            }

            if (i > oldStart || j > newStart)
            {
                AddSplitByPage(old, oldStart, i, @new, newStart, j, changes);
                continue;
            }

            // A word both sides keep.
            i++;
            j++;
        }
    }

    /// <summary>
    /// Adds one run as changes, one per page: the k-th page of the old words pairs with the k-th page of the new words, and a page
    /// one side has more of becomes a deletion or an insertion.
    /// </summary>
    private static void AddSplitByPage(WordSequence old, int oldStart, int oldEnd, WordSequence @new, int newStart, int newEnd, List<TextChange> changes)
    {
        while (oldStart < oldEnd || newStart < newEnd)
        {
            int oldStop = old.EndOfPage(oldStart, oldEnd);
            int newStop = @new.EndOfPage(newStart, newEnd);
            bool hasOld = oldStop > oldStart, hasNew = newStop > newStart;
            TextChangeKind kind = hasOld && hasNew ? TextChangeKind.Changed : hasOld ? TextChangeKind.Deleted : TextChangeKind.Inserted;
            changes.Add(new TextChange(
                kind,
                hasOld ? old.Page(oldStart) : -1,
                old.Text(oldStart, oldStop),
                old.Quads(oldStart, oldStop),
                hasNew ? @new.Page(newStart) : -1,
                @new.Text(newStart, newStop),
                @new.Quads(newStart, newStop)));
            oldStart = oldStop;
            newStart = newStop;
        }
    }

    /// <summary>One document's compared words in reading order, page after page.</summary>
    private sealed class WordSequence
    {
        private readonly List<Word> _words = [];
        private readonly List<int> _identities = [];
        private readonly List<int> _pageStarts = [];

        public int Count => _words.Count;

        public int PageCount => _pageStarts.Count;

        public ReadOnlySpan<int> Identities => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_identities);

        public static WordSequence Read(PdfDocument document, TextExtractionOptions extraction, TextComparisonOptions options, Dictionary<string, int> identities, CancellationToken cancellationToken)
        {
            var sequence = new WordSequence();
            var key = new StringBuilder();
            int lineId = 0;
            for (int index = 0; index < document.Pages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sequence._pageStarts.Add(sequence._words.Count);
                TextPage page = document.Pages[index].ExtractText(extraction, null);
                IReadOnlyList<TextWord> words = page.Words;
                IReadOnlyList<TextLine> lines = page.Lines;
                for (int line = 0; line < lines.Count; line++, lineId++)
                {
                    TextLine textLine = lines[line];
                    for (int w = textLine.WordStart; w < textLine.WordStart + textLine.WordCount; w++)
                    {
                        TextWord word = words[w];
                        ReadOnlySpan<char> text = page.GetWordText(word);
                        string? identity = Identity(text, options, key);
                        if (identity is null)
                        {
                            continue;
                        }

                        if (!identities.TryGetValue(identity, out int id))
                        {
                            id = identities.Count;
                            identities.Add(identity, id);
                        }

                        sequence._words.Add(new Word(index, lineId, word.Quad, text.ToString()));
                        sequence._identities.Add(id);
                    }
                }
            }

            return sequence;
        }

        /// <summary>The range of words on a page; empty past the document's last page.</summary>
        public (int Start, int End) PageRange(int page) =>
            page >= _pageStarts.Count ? (Count, Count) : (_pageStarts[page], page + 1 < _pageStarts.Count ? _pageStarts[page + 1] : Count);

        public int Page(int word) => _words[word].Page;

        /// <summary>Where the words from <paramref name="start"/> leave its page, no further than <paramref name="end"/>.</summary>
        public int EndOfPage(int start, int end)
        {
            if (start >= end)
            {
                return start;
            }

            int page = _words[start].Page;
            int stop = start + 1;
            while (stop < end && _words[stop].Page == page)
            {
                stop++;
            }

            return stop;
        }

        public string Text(int start, int end)
        {
            if (start >= end)
            {
                return string.Empty;
            }

            var text = new StringBuilder();
            for (int i = start; i < end; i++)
            {
                if (i > start)
                {
                    text.Append(' ');
                }

                text.Append(_words[i].Text);
            }

            return text.ToString();
        }

        /// <summary>One quad per line the words cover, from the line's first word's left edge to its last word's right edge.</summary>
        public ReadOnlyCollection<Quad> Quads(int start, int end)
        {
            if (start >= end)
            {
                return ReadOnlyCollection<Quad>.Empty;
            }

            var quads = new List<Quad>();
            int first = start;
            for (int i = start + 1; i <= end; i++)
            {
                if (i == end || _words[i].Line != _words[first].Line)
                {
                    Quad a = _words[first].Quad, b = _words[i - 1].Quad;
                    quads.Add(first == i - 1 ? a : new Quad(a.BottomLeft, b.BottomRight, b.TopRight, a.TopLeft));
                    first = i;
                }
            }

            return quads.AsReadOnly();
        }

        /// <summary>The text a word compares by, or <see langword="null"/> when it is not compared (only punctuation, which is ignored).</summary>
        private static string? Identity(ReadOnlySpan<char> text, TextComparisonOptions options, StringBuilder key)
        {
            if (options.MatchCase && !options.IgnorePunctuation)
            {
                return text.ToString();
            }

            key.Clear();
            foreach (char c in text)
            {
                if (options.IgnorePunctuation && IsPunctuation(c))
                {
                    continue;
                }

                key.Append(c);
            }

            if (key.Length == 0)
            {
                return null;
            }

            string kept = key.ToString();
            return options.MatchCase ? kept : SearchableText.FoldWord(kept);
        }

        private static bool IsPunctuation(char c) => CharUnicodeInfo.GetUnicodeCategory(c) is
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;

        private readonly record struct Word(int Page, int Line, Quad Quad, string Text);
    }
}
