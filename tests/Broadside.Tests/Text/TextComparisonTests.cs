#pragma warning disable CA5394 // pseudo-random test input, not security

using Broadside.Graphics;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>Word-level comparison of two documents' text (issue #644).</summary>
public sealed class TextComparisonTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Identical_documents_have_no_changes()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Lines("The quick brown fox", "jumps over the lazy dog"));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Lines("The quick brown fox", "jumps over the lazy dog"));

        TextComparison comparison = old.CompareText(@new, cancellationToken: Token);

        Assert.True(comparison.IsIdentical);
        Assert.Empty(comparison.Changes);
        Assert.False(comparison.ComparedByPage);
        Assert.Empty(old.Diagnostics);
        Assert.Empty(@new.Diagnostics);
    }

    [Fact]
    public void Empty_documents_compare_as_identical()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Pages([[], []]));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Pages([[]]));

        Assert.True(old.CompareText(@new, cancellationToken: Token).IsIdentical);
    }

    [Fact]
    public void An_insertion_a_deletion_and_a_change_are_found_with_their_pages_text_and_quads()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Lines("The quick brown fox jumps", "over the very lazy dog"));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Lines("The quick red fox jumps", "over the lazy dog today"));

        IReadOnlyList<TextChange> changes = old.CompareText(@new, cancellationToken: Token).Changes;

        Assert.Equal(3, changes.Count);
        TextChange changed = changes[0];
        Assert.Equal((TextChangeKind.Changed, 0, "brown", 0, "red"), (changed.Kind, changed.OldPageIndex, changed.OldText, changed.NewPageIndex, changed.NewText));

        // Helvetica advances (AFM): "The quick " is 4,613 units at 12 points, the left edge of the third word on both sides.
        Quad oldQuad = Assert.Single(changed.OldQuads);
        Quad newQuad = Assert.Single(changed.NewQuads);
        Assert.Equal(72 + 55.356, oldQuad.BottomLeft.X, 3);
        Assert.Equal(72 + 55.356, newQuad.BottomLeft.X, 3);
        Assert.True(oldQuad.BottomRight.X > newQuad.BottomRight.X); // "brown" is wider than "red"

        TextChange deleted = changes[1];
        Assert.Equal((TextChangeKind.Deleted, 0, "very", -1, ""), (deleted.Kind, deleted.OldPageIndex, deleted.OldText, deleted.NewPageIndex, deleted.NewText));
        Assert.Single(deleted.OldQuads);
        Assert.Empty(deleted.NewQuads);
        Assert.Equal(686 - 2.484, deleted.OldQuads[0].BottomLeft.Y, 3); // the second line, down to the descender

        TextChange inserted = changes[2];
        Assert.Equal((TextChangeKind.Inserted, -1, "", 0, "today"), (inserted.Kind, inserted.OldPageIndex, inserted.OldText, inserted.NewPageIndex, inserted.NewText));
        Assert.Empty(inserted.OldQuads);
        Assert.Single(inserted.NewQuads);
    }

    [Fact]
    public void A_change_over_two_lines_has_one_quad_per_line()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Lines("keep alpha beta", "gamma delta keep"));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Lines("keep", "keep"));

        TextChange change = Assert.Single(old.CompareText(@new, cancellationToken: Token).Changes);

        Assert.Equal(TextChangeKind.Deleted, change.Kind);
        Assert.Equal("alpha beta gamma delta", change.OldText);
        Assert.Equal(2, change.OldQuads.Count);
        Assert.True(change.OldQuads[0].BottomLeft.Y > change.OldQuads[1].BottomLeft.Y);
        Assert.Equal(72, change.OldQuads[1].BottomLeft.X, 3); // "gamma" starts the second line
    }

    [Fact]
    public void A_change_across_a_page_boundary_is_split_per_page()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Pages([["alpha beta gamma"], ["delta epsilon"]]));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Pages([["alpha beta"], ["epsilon"]]));

        IReadOnlyList<TextChange> changes = old.CompareText(@new, cancellationToken: Token).Changes;

        Assert.Equal(2, changes.Count);
        Assert.Equal((TextChangeKind.Deleted, 0, "gamma"), (changes[0].Kind, changes[0].OldPageIndex, changes[0].OldText));
        Assert.Equal((TextChangeKind.Deleted, 1, "delta"), (changes[1].Kind, changes[1].OldPageIndex, changes[1].OldText));
    }

    [Fact]
    public void A_changed_run_across_a_page_boundary_pairs_its_pages_in_order()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Pages([["one two"], ["three four"]]));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Pages([["one 2"], ["3 four"]]));

        IReadOnlyList<TextChange> changes = old.CompareText(@new, cancellationToken: Token).Changes;

        Assert.Equal(2, changes.Count);
        Assert.Equal((TextChangeKind.Changed, 0, "two", 0, "2"), (changes[0].Kind, changes[0].OldPageIndex, changes[0].OldText, changes[0].NewPageIndex, changes[0].NewText));
        Assert.Equal((TextChangeKind.Changed, 1, "three", 1, "3"), (changes[1].Kind, changes[1].OldPageIndex, changes[1].OldText, changes[1].NewPageIndex, changes[1].NewText));
    }

    [Fact]
    public void Text_that_moves_to_the_next_page_is_not_a_change()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Pages([["one two three"], ["four"]]));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Pages([["one two"], ["three four"]]));

        Assert.True(old.CompareText(@new, cancellationToken: Token).IsIdentical);
    }

    [Fact]
    public void Different_page_counts_compare_their_words()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Pages([["one"]]));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Pages([["one"], ["two three"]]));

        TextChange change = Assert.Single(old.CompareText(@new, cancellationToken: Token).Changes);

        Assert.Equal((TextChangeKind.Inserted, 1, "two three"), (change.Kind, change.NewPageIndex, change.NewText));
    }

    [Fact]
    public void Artifacts_are_ignored_unless_asked_for()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Pages([["Body text"]], footers: ["Page 1"]));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Pages([["Body text"]], footers: ["Page 7"]));

        Assert.True(old.CompareText(@new, cancellationToken: Token).IsIdentical);

        TextChange change = Assert.Single(old.CompareText(@new, new TextComparisonOptions().WithArtifacts(), Token).Changes);
        Assert.Equal((TextChangeKind.Changed, "1", "7"), (change.Kind, change.OldText, change.NewText));
    }

    [Fact]
    public void Case_and_punctuation_count_unless_the_options_ignore_them()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Lines("Hello, World! - again"));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Lines("hello world again"));

        TextChange exact = Assert.Single(old.CompareText(@new, cancellationToken: Token).Changes);
        Assert.Equal(("Hello, World! -", "hello world"), (exact.OldText, exact.NewText));

        // Case folded, punctuation kept: "Hello," is still not "hello", and the dash is a word of its own.
        TextChange folded = Assert.Single(old.CompareText(@new, new TextComparisonOptions().WithMatchCase(false), Token).Changes);
        Assert.Equal(("Hello, World! -", "hello world"), (folded.OldText, folded.NewText));

        Assert.True(old.CompareText(@new, new TextComparisonOptions().WithMatchCase(false).WithIgnorePunctuation(), Token).IsIdentical);

        // Punctuation ignored, case kept: only the capitals differ.
        TextChange cased = Assert.Single(old.CompareText(@new, new TextComparisonOptions().WithIgnorePunctuation(), Token).Changes);
        Assert.Equal(("Hello, World!", "hello world"), (cased.OldText, cased.NewText));
    }

    [Fact]
    public void Past_the_edit_distance_bound_the_pages_are_compared_pairwise()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Pages([["a b c d"], ["e f g h"], ["i"]]));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Pages([["e f g h"], ["a b c d"]]));

        // The whole-document script moves "a b c d" after "e f g h": four deletions, four insertions and the last page's deletion.
        TextComparison whole = old.CompareText(@new, cancellationToken: Token);
        Assert.False(whole.ComparedByPage);
        Assert.Equal(9, whole.Changes.Sum(change => change.OldText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length + change.NewText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));

        TextComparison paged = old.CompareText(@new, new TextComparisonOptions().WithMaxEditDistance(4), Token);
        Assert.True(paged.ComparedByPage);
        Assert.Equal(3, paged.Changes.Count);
        Assert.Equal((TextChangeKind.Changed, 0, "a b c d", 0, "e f g h"), (paged.Changes[0].Kind, paged.Changes[0].OldPageIndex, paged.Changes[0].OldText, paged.Changes[0].NewPageIndex, paged.Changes[0].NewText));
        Assert.Equal((TextChangeKind.Changed, 1, "e f g h", 1, "a b c d"), (paged.Changes[1].Kind, paged.Changes[1].OldPageIndex, paged.Changes[1].OldText, paged.Changes[1].NewPageIndex, paged.Changes[1].NewText));
        Assert.Equal((TextChangeKind.Deleted, 2, "i", -1), (paged.Changes[2].Kind, paged.Changes[2].OldPageIndex, paged.Changes[2].OldText, paged.Changes[2].NewPageIndex));
    }

    [Fact]
    public void The_options_validate_and_chain()
    {
        var options = new TextComparisonOptions();
        Assert.Equal((true, false, false, 100_000), (options.MatchCase, options.IgnorePunctuation, options.IncludeArtifacts, options.MaxEditDistance));
        Assert.Same(options, options.WithMatchCase(false).WithIgnorePunctuation().WithArtifacts().WithMaxEditDistance(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithMaxEditDistance(-1));
        Assert.Throws<ArgumentNullException>(() => PdfDocument.Open(ComparisonPdf.Lines("x")).CompareText(null!, cancellationToken: Token));
    }

    [Fact]
    public void A_canceled_comparison_throws()
    {
        using PdfDocument old = PdfDocument.Open(ComparisonPdf.Lines("one"));
        using PdfDocument @new = PdfDocument.Open(ComparisonPdf.Lines("two"));

        Assert.ThrowsAny<OperationCanceledException>(() => old.CompareText(@new, cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void The_diff_finds_a_shortest_edit_script()
    {
        var random = new Random(644);
        for (int round = 0; round < 500; round++)
        {
            int[] a = Sequence(random), b = Sequence(random);
            var deleted = new bool[a.Length];
            var inserted = new bool[b.Length];

            Assert.True(MyersDiff.TryDiff(a, b, deleted, inserted, int.MaxValue, Token));

            // What is kept is a common subsequence, in order, as long as the longest one.
            int[] keptA = [.. a.Where((_, i) => !deleted[i])];
            int[] keptB = [.. b.Where((_, i) => !inserted[i])];
            Assert.Equal(keptA, keptB);
            Assert.Equal(LongestCommonSubsequence(a, b), keptA.Length);
        }

        static int[] Sequence(Random random) => [.. Enumerable.Range(0, random.Next(0, 40)).Select(_ => random.Next(0, 5))];
    }

    [Fact]
    public void The_diff_gives_up_past_its_bound()
    {
        int[] a = [.. Enumerable.Range(0, 100)];
        int[] b = [.. Enumerable.Range(100, 100)];

        Assert.False(MyersDiff.TryDiff(a, b, new bool[100], new bool[100], 50, Token));
        Assert.True(MyersDiff.TryDiff(a, b, new bool[100], new bool[100], 200, Token));
    }

    private static int LongestCommonSubsequence(int[] a, int[] b)
    {
        int[][] table = [.. Enumerable.Range(0, a.Length + 1).Select(_ => new int[b.Length + 1])];
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                table[i][j] = a[i - 1] == b[j - 1] ? table[i - 1][j - 1] + 1 : Math.Max(table[i - 1][j], table[i][j - 1]);
            }
        }

        return table[a.Length][b.Length];
    }
}
