using Broadside.Content;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>content-rewrite</c> target (issue #147): parse, write, parse. The input is a page's decoded content. An identity rewrite
/// (every operator replaced with its own bytes) must reproduce the input byte for byte. Then an editor removes some operators and
/// wraps others in <c>q</c>/<c>Q</c> (which ones the input's length decides), and the output, read again, must hold exactly the
/// operators kept, each byte-identical to its source and in order, with the wraps around them. Content holding an inline image is
/// checked for the identity only: where its data ends depends on what follows <c>EI</c> (§8.9.7), which an edit may change.
/// </summary>
/// <remarks>ISO 32000-2 §7.8.2, §8.9.7.</remarks>
internal static class ContentRewriteTarget
{
    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] content = data.ToArray();
        ContentOptions options = ContentInterpreter.DefaultOptions;

        var identity = new Recorder(replaceAll: true);
        ReadOnlySpan<byte> same = ContentRewriting.RewriteBytes(content, document, identity, options);
        Check(same.SequenceEqual(content), "an identity rewrite changed the content");
        if (identity.Operators.Count == 0 || identity.HasInlineImage)
        {
            return;
        }

        int seed = content.Length;
        var editor = new Editor(seed);
        byte[] edited = ContentRewriting.RewriteBytes(content, document, editor, options).ToArray();

        var expected = new List<byte[]>();
        for (int index = 0; index < identity.Operators.Count; index++)
        {
            switch (Editor.Choice(seed, index))
            {
                case EditChoice.Remove:
                    break;
                case EditChoice.Wrap:
                    expected.Add("q"u8.ToArray());
                    expected.Add(identity.Operators[index]);
                    expected.Add("Q"u8.ToArray());
                    break;
                default:
                    expected.Add(identity.Operators[index]);
                    break;
            }
        }

        var reread = new Recorder(replaceAll: false);
        _ = ContentRewriting.RewriteBytes(edited, document, reread, options);
        Check(reread.Operators.Count == expected.Count, "the edited content reads back with a different number of operators");
        for (int index = 0; index < expected.Count; index++)
        {
            Check(reread.Operators[index].AsSpan().SequenceEqual(expected[index]), "an operator kept by the edit reads back changed");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private enum EditChoice
    {
        Keep,
        Remove,
        Wrap,
    }

    /// <summary>Copies every operator's bytes at the end of the run; optionally replaces each with itself.</summary>
    private sealed class Recorder(bool replaceAll) : ContentEditor
    {
        public List<byte[]> Operators { get; } = [];

        public bool HasInlineImage { get; private set; }

        public override ContentEvents Events => ContentEvents.None;

        public override void EndRun(ContentContext context)
        {
            for (int index = 0; index < OperatorCount; index++)
            {
                ContentOperatorRange range = GetOperator(index);
                ReadOnlySpan<byte> bytes = Content.Span.Slice(range.Offset, range.Length);
                Operators.Add(bytes.ToArray());
                HasInlineImage |= range.Code == ContentOperatorCode.BeginInlineImage;
                if (replaceAll)
                {
                    Replace(index, bytes);
                }
            }
        }
    }

    /// <summary>Removes or wraps operators as <see cref="Choice"/> picks, as they execute.</summary>
    private sealed class Editor(int seed) : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.Operators;

        public static EditChoice Choice(int seed, int index) => (((index * 7) + seed) % 5) switch
        {
            0 => EditChoice.Remove,
            1 => EditChoice.Wrap,
            _ => EditChoice.Keep,
        };

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            if (context.Depth != 0)
            {
                return;
            }

            int index = CurrentOperator;
            switch (Choice(seed, index))
            {
                case EditChoice.Remove:
                    Remove(index);
                    break;
                case EditChoice.Wrap:
                    Wrap(index, index, "q"u8, "Q"u8);
                    break;
            }
        }
    }
}
