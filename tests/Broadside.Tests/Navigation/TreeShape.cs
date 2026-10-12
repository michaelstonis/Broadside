using Broadside.Objects;

namespace Broadside.Tests.Navigation;

/// <summary>
/// An independent check of a name or number tree's structure over the public COS objects (ISO 32000-2 §7.9.6, Table 36; §7.9.7,
/// Table 37): the root has no <c>Limits</c>; every other node has <c>Limits</c> equal to the least and greatest key below it; every
/// node has <c>Kids</c> or the pairs array, not both; kids are indirect references; keys ascend strictly in tree order; no node
/// below the root is empty; and every leaf is at the same depth.
/// </summary>
internal static class TreeShape
{
    private static readonly CosName Kids = new("Kids");
    private static readonly CosName Limits = new("Limits");

    /// <summary>Checks the tree and returns its leaves' depth and its node count.</summary>
    /// <param name="document">The document the tree is in.</param>
    /// <param name="root">The root node.</param>
    /// <param name="entriesKey"><c>Names</c> or <c>Nums</c>.</param>
    /// <param name="key">Turns a key object into a value that compares as the clause orders keys.</param>
    /// <returns>The depth of every leaf (0 when the root is a leaf) and the number of nodes.</returns>
    public static (int Depth, int Nodes) AssertWellFormed(PdfDocument document, CosDictionary root, CosName entriesKey, Func<CosObject, IComparable> key)
    {
        Assert.False(root.ContainsKey(Limits), "The root of a tree has no Limits.");
        var state = new State(document, entriesKey, key);
        state.Visit(root, depth: 0, isRoot: true);
        return (state.LeafDepth ?? 0, state.Nodes);
    }

    private sealed class State(PdfDocument document, CosName entriesKey, Func<CosObject, IComparable> key)
    {
        private IComparable? _previous;

        public int? LeafDepth { get; private set; }

        public int Nodes { get; private set; }

        /// <summary>Visits a node; returns its least and greatest keys, or nulls when it is an empty root.</summary>
        public (IComparable? Least, IComparable? Greatest) Visit(CosDictionary node, int depth, bool isRoot)
        {
            Nodes++;
            bool hasEntries = node.TryGetValue(entriesKey, out CosObject? entries);
            bool hasKids = node.TryGetValue(Kids, out CosObject? kids);
            Assert.True(hasEntries != hasKids, "A node has Kids or the pairs array, not both.");
            IComparable? least = null;
            IComparable? greatest = null;
            if (hasEntries)
            {
                CosArray pairs = Assert.IsType<CosArray>(document.Resolve(entries));
                Assert.True(pairs.Count % 2 == 0, "A pairs array has an even length.");
                Assert.True(isRoot || pairs.Count > 0, "A leaf below the root is not empty.");
                for (int index = 0; index < pairs.Count; index += 2)
                {
                    IComparable current = key(document.Resolve(pairs[index]));
                    Assert.True(_previous is null || _previous.CompareTo(current) < 0, $"Keys ascend strictly: {_previous} then {current}.");
                    _previous = current;
                    least ??= current;
                    greatest = current;
                }

                Assert.True(LeafDepth is null || LeafDepth == depth, $"Every leaf is at depth {LeafDepth}; one is at {depth}.");
                LeafDepth = depth;
            }
            else
            {
                CosArray children = Assert.IsType<CosArray>(document.Resolve(kids));
                Assert.True(isRoot || children.Count > 0, "An intermediate node below the root has kids.");
                foreach (CosObject child in children)
                {
                    Assert.IsType<CosReference>(child);
                    (IComparable? childLeast, IComparable? childGreatest) = Visit(Assert.IsType<CosDictionary>(document.Resolve(child)), depth + 1, isRoot: false);
                    least ??= childLeast;
                    greatest = childGreatest;
                }
            }

            if (!isRoot)
            {
                CosArray limits = Assert.IsType<CosArray>(document.Resolve(node[Limits]));
                Assert.Equal(2, limits.Count);
                Assert.Equal(least, key(document.Resolve(limits[0])));
                Assert.Equal(greatest, key(document.Resolve(limits[1])));
            }

            return (least, greatest);
        }
    }
}
