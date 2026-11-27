using System.Text;
using Broadside.Layout.Flow;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>markdown</c> target (issue #645): the input as UTF-8 (invalid sequences replaced) is parsed as CommonMark with and without the
/// GFM extensions, and the tree is checked to be well formed; an input of at most <see cref="MaxBuildLength"/> bytes is also converted
/// to a document through the builder and laid out. Markdown has no invalid input, so any exception is a finding.
/// </summary>
/// <remarks>CommonMark 0.31 and GFM 0.29; ISO 32000-2 §14.8.4 (the structure the conversion writes).</remarks>
internal static class MarkdownTarget
{
    /// <summary>The longest input also laid out as a document, which costs far more than parsing.</summary>
    private const int MaxBuildLength = 4096;

    public static void Target(ReadOnlySpan<byte> data)
    {
        string markdown = Encoding.UTF8.GetString(data);
        Check(MarkdownParser.Parse(markdown, gitHubExtensions: true));
        Check(MarkdownParser.Parse(markdown, gitHubExtensions: false));
        if (data.Length <= MaxBuildLength)
        {
            using PdfDocument document = PdfDocument.CreateFromMarkdown(markdown);
        }
    }

    /// <summary>Every child points back at its parent and its siblings, and inline nodes hold no blocks.</summary>
    private static void Check(MarkdownNode document)
    {
        foreach ((MarkdownNode node, bool entering) in document.Walk())
        {
            if (!entering)
            {
                continue;
            }

            MarkdownNode? previous = null;
            for (MarkdownNode? child = node.FirstChild; child is not null; child = child.Next)
            {
                if (!ReferenceEquals(child.Parent, node) || !ReferenceEquals(child.Previous, previous))
                {
                    throw new InvalidOperationException($"A {child.Kind} node is linked into the tree inconsistently.");
                }

                if (node.Kind >= MarkdownKind.Text && child.Kind < MarkdownKind.Text)
                {
                    throw new InvalidOperationException($"A {child.Kind} block is inside the inline {node.Kind}.");
                }

                previous = child;
            }

            if (!ReferenceEquals(node.LastChild, previous))
            {
                throw new InvalidOperationException($"The last child of a {node.Kind} node is not its last in order.");
            }
        }
    }
}
