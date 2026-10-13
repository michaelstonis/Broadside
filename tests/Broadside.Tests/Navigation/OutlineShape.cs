using System.Globalization;
using Broadside.Objects;

namespace Broadside.Tests.Navigation;

/// <summary>
/// An independent checker of an outline's links over the public COS objects (ISO 32000-2 §12.3.3, Tables 150 and 151): every item
/// indirect, <c>Parent</c> naming its parent, <c>Prev</c> and <c>Next</c> chaining the siblings, <c>First</c> and <c>Last</c> naming the
/// ends of each level (absent on a leaf), and every <c>Count</c> as the clause's recursive process gives it.
/// </summary>
internal static class OutlineShape
{
    private static readonly CosName First = new("First");
    private static readonly CosName Last = new("Last");
    private static readonly CosName Next = new("Next");
    private static readonly CosName Prev = new("Prev");
    private static readonly CosName Parent = new("Parent");
    private static readonly CosName Count = new("Count");
    private static readonly CosName Title = new("Title");
    private static readonly CosName Outlines = new("Outlines");

    /// <summary>Checks the outline and returns its items, depth first, as <c>"level title count"</c> (<c>-</c> for no Count).</summary>
    public static IReadOnlyList<string> AssertConsistent(PdfDocument document)
    {
        CosReference rootReference = Assert.IsType<CosReference>(document.Catalog[Outlines]);
        CosDictionary root = Assert.IsType<CosDictionary>(document.Resolve(rootReference));
        var lines = new List<string>();
        int visible = Check(document, root, rootReference, 0, lines);
        if (visible == 0)
        {
            Assert.False(root.ContainsKey(Count), "An outline without visible items has no Count.");
        }
        else
        {
            Assert.Equal(visible, (int)Assert.IsType<CosInteger>(root[Count]).Value);
        }

        return lines;
    }

    /// <summary>Checks the children of <paramref name="node"/> and returns how many are visible when it is open.</summary>
    private static int Check(PdfDocument document, CosDictionary node, CosReference reference, int level, List<string> lines)
    {
        if (!node.ContainsKey(First))
        {
            Assert.False(node.ContainsKey(Last), "A node without First has no Last.");
            return 0;
        }

        int visible = 0;
        CosReference? previous = null;
        CosObject? entry = node[First];
        var seen = new HashSet<CosReference>();
        while (entry is not null)
        {
            CosReference itemReference = Assert.IsType<CosReference>(entry);
            Assert.True(seen.Add(itemReference), "An item appears twice in one level.");
            CosDictionary item = Assert.IsType<CosDictionary>(document.Resolve(itemReference));
            Assert.Equal(reference, item[Parent]);
            Assert.Equal(previous, item.TryGetValue(Prev, out CosObject? prev) ? prev : null);
            int? count = item.TryGetValue(Count, out CosObject? stored) ? (int)Assert.IsType<CosInteger>(stored).Value : null;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{level} {Assert.IsType<CosString>(item[Title]).DecodeText()} {count?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));
            int descendants = Check(document, item, itemReference, level + 1, lines);
            if (descendants == 0)
            {
                Assert.Null(count);
            }
            else
            {
                Assert.Equal(descendants, Math.Abs(count ?? 0));
            }

            visible += 1 + (count > 0 ? count.Value : 0);
            previous = itemReference;
            entry = item.TryGetValue(Next, out CosObject? next) ? next : null;
        }

        Assert.Equal(previous, node[Last]);
        return visible;
    }
}
