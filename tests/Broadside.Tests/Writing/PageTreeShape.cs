using Broadside.Objects;

namespace Broadside.Tests.Writing;

/// <summary>
/// Checks a page tree through the COS layer alone, independent of the reader and the editor: every node is a <c>Pages</c> dictionary
/// with indirect kids, every kid's <c>Parent</c> is the node listing it, every <c>Count</c> is the number of pages below, the root has
/// no <c>Parent</c>, no node has more than a given number of kids and every page is at the same depth. ISO 32000-2 §7.7.3.2, Table 30.
/// </summary>
internal static class PageTreeShape
{
    private static readonly CosName Pages = new("Pages");
    private static readonly CosName Page = new("Page");
    private static readonly CosName Type = new("Type");
    private static readonly CosName Kids = new("Kids");
    private static readonly CosName Count = new("Count");
    private static readonly CosName Parent = new("Parent");

    /// <summary>Asserts the shape and returns the page objects in tree order.</summary>
    public static IReadOnlyList<CosDictionary> AssertBalanced(PdfDocument document, int maxKids)
    {
        var rootReference = Assert.IsType<CosReference>(document.Catalog[Pages]);
        var root = Assert.IsType<CosDictionary>(document.Resolve(rootReference));
        Assert.False(root.ContainsKey(Parent), "The root has a Parent.");
        var pages = new List<CosDictionary>();
        var depths = new HashSet<int>();
        int count = Walk(document, root, rootReference, 0, maxKids, pages, depths);
        Assert.Equal(pages.Count, count);
        Assert.True(depths.Count <= 1, $"Pages are at depths {string.Join(", ", depths)}.");
        return pages;
    }

    private static int Walk(PdfDocument document, CosDictionary node, CosReference reference, int depth, int maxKids, List<CosDictionary> pages, HashSet<int> depths)
    {
        Assert.Equal(Pages, node[Type]);
        var kids = Assert.IsType<CosArray>(node[Kids]);
        Assert.True(kids.Count <= maxKids, $"A node has {kids.Count} kids.");
        int leaves = 0;
        foreach (CosObject kid in kids)
        {
            var kidReference = Assert.IsType<CosReference>(kid);
            var dictionary = Assert.IsType<CosDictionary>(document.Resolve(kidReference));
            Assert.Equal(reference, dictionary[Parent]);
            if (Page.Equals(dictionary[Type]))
            {
                pages.Add(dictionary);
                depths.Add(depth + 1);
                leaves++;
            }
            else
            {
                leaves += Walk(document, dictionary, kidReference, depth + 1, maxKids, pages, depths);
            }
        }

        Assert.Equal(new CosInteger(leaves), node[Count]);
        return leaves;
    }
}
