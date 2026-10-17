using Broadside.Objects;

namespace Broadside.Tests.Writing;

/// <summary>
/// Compares the object graphs two documents reach from their trailers, up to the numbering of their indirect objects: the graphs are
/// isomorphic when one bijection between the references of the two maps every reachable object to an equal one. Independent of the
/// writer: it reads both documents only through the public API.
/// </summary>
/// <remarks>
/// A stream's <c>Length</c> is not compared or followed (a writer gives every stream a direct length, ISO 32000-2 §7.3.8.2). A
/// reference to an object that does not exist is the null object (§7.3.10), so it matches a direct <c>null</c>, and a dictionary
/// entry whose value is null is the same as no entry (§7.3.7).
/// </remarks>
internal static class CosGraph
{
    private static readonly CosName Length = new("Length");

    /// <summary>The trailer entries that describe the file rather than the document, which a rewrite replaces.</summary>
    private static readonly HashSet<string> FileTrailerKeys = new(StringComparer.Ordinal) { "Size", "Prev", "XRefStm", "ID", "Type", "W", "Index", "Filter", "DecodeParms", "Length", "DL" };

    /// <summary>
    /// Asserts that <paramref name="actual"/>'s trailer reaches a graph isomorphic to the one <paramref name="expected"/>'s trailer
    /// reaches, and returns the bijection between their references.
    /// </summary>
    public static IReadOnlyDictionary<CosReference, CosReference> AssertIsomorphic(PdfDocument expected, PdfDocument actual)
    {
        var comparer = new Comparer(expected, actual);
        comparer.Compare(DocumentEntries(expected.Trailer), DocumentEntries(actual.Trailer), "trailer");
        comparer.Drain();
        return comparer.Forward;
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> reaches an object graph from <paramref name="actualRoot"/> isomorphic to the one
    /// <paramref name="expected"/> reaches from <paramref name="expectedRoot"/>.
    /// </summary>
    public static void AssertIsomorphic(PdfDocument expected, CosObject expectedRoot, PdfDocument actual, CosObject actualRoot)
    {
        var comparer = new Comparer(expected, actual);
        comparer.Compare(expectedRoot, actualRoot, "root");
        comparer.Drain();
    }

    /// <summary>
    /// Asserts that the page object <paramref name="actualPage"/> of <paramref name="actual"/> is a copy of the page object
    /// <paramref name="expectedPage"/> of <paramref name="expected"/>, cut out of its page tree: every page the graphs reach compares
    /// without its <c>Parent</c> entry, an expected page's inherited attributes (ISO 32000-2 §7.7.3.4) count as its own, and
    /// everything else compares as <see cref="AssertIsomorphic(PdfDocument, PdfDocument)"/> compares it. Pass the comparer of earlier
    /// pages of the same copy session to check that they share one bijection.
    /// </summary>
    public static PageCopyComparer AssertPageCopy(PdfDocument expected, CosReference expectedPage, PdfDocument actual, CosReference actualPage, PageCopyComparer? session = null)
    {
        session ??= new PageCopyComparer(new Comparer(expected, actual) { PageCopies = true });
        session.Inner.Compare(expectedPage, actualPage, "page");
        session.Inner.Drain();
        return session;
    }

    /// <summary>Every indirect object the document reaches from its trailer, stream lengths excepted.</summary>
    public static IReadOnlyList<CosReference> Reachable(PdfDocument document)
    {
        var seen = new HashSet<CosReference>();
        var order = new List<CosReference>();
        var pending = new Stack<CosObject>();
        pending.Push(DocumentEntries(document.Trailer));
        while (pending.TryPop(out CosObject? value))
        {
            switch (value)
            {
                case CosReference reference:
                    CosObject target = document.Resolve(reference);
                    if (target is not CosNull && seen.Add(reference))
                    {
                        order.Add(reference);
                        pending.Push(target);
                    }

                    break;
                case CosArray array:
                    array.ToList().ForEach(pending.Push);
                    break;
                case CosStream stream:
                    stream.Dictionary.Where(entry => !entry.Key.Equals(Length)).ToList().ForEach(entry => pending.Push(entry.Value));
                    break;
                case CosDictionary dictionary:
                    dictionary.Values.ToList().ForEach(pending.Push);
                    break;
                default:
                    break;
            }
        }

        return order;
    }

    /// <summary>Every object number below the trailer's <c>Size</c> that holds an object under generation 0.</summary>
    public static IReadOnlyList<CosReference> AllObjects(PdfDocument document)
    {
        int size = (int)((CosInteger)document.Trailer[new CosName("Size")]).Value;
        var objects = new List<CosReference>();
        for (int number = 1; number < size; number++)
        {
            var reference = new CosReference(number, 0);
            if (document.Resolve(reference) is not CosNull)
            {
                objects.Add(reference);
            }
        }

        return objects;
    }

    private static CosDictionary DocumentEntries(CosDictionary trailer)
    {
        var entries = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in trailer)
        {
            if (!FileTrailerKeys.Contains(entry.Key.Value))
            {
                entries[entry.Key] = entry.Value;
            }
        }

        return entries;
    }

    /// <summary>One bijection shared by the page comparisons of one copy session.</summary>
    internal sealed class PageCopyComparer
    {
        internal PageCopyComparer(Comparer inner) => Inner = inner;

        internal Comparer Inner { get; }

        /// <summary>Gets the bijection between the references compared so far.</summary>
        public IReadOnlyDictionary<CosReference, CosReference> Bijection => Inner.Forward;
    }

    internal sealed class Comparer(PdfDocument expected, PdfDocument actual)
    {
        private static readonly CosName TypeKey = new("Type");
        private static readonly CosName PageType = new("Page");
        private static readonly CosName ParentKey = new("Parent");
        private static readonly CosName[] Inheritable = [new("Resources"), new("MediaBox"), new("CropBox"), new("Rotate")];

        private readonly Dictionary<CosReference, CosReference> _reverse = [];
        private readonly Queue<(CosObject Expected, CosObject Actual, string Path)> _pending = new();

        public Dictionary<CosReference, CosReference> Forward { get; } = [];

        /// <summary>Gets a value indicating whether pages compare as copies cut out of their page tree.</summary>
        public bool PageCopies { get; init; }

        public void Drain()
        {
            while (_pending.TryDequeue(out (CosObject Expected, CosObject Actual, string Path) item))
            {
                CompareDirect(item.Expected, item.Actual, item.Path);
            }
        }

        public void Compare(CosObject left, CosObject right, string path)
        {
            CosObject leftValue = expected.Resolve(left);
            CosObject rightValue = actual.Resolve(right);
            if (leftValue is CosNull || rightValue is CosNull)
            {
                Assert.True(leftValue is CosNull && rightValue is CosNull, $"{path}: {leftValue} became {rightValue}.");
                return;
            }

            if (left is CosReference leftReference)
            {
                Assert.True(right is CosReference, $"{path}: the indirect object {leftReference} became the direct object {right}.");
                var rightReference = (CosReference)right;
                if (Forward.TryGetValue(leftReference, out CosReference? mapped))
                {
                    Assert.True(mapped.Equals(rightReference), $"{path}: {leftReference} maps to both {mapped} and {rightReference}.");
                    return;
                }

                Assert.False(_reverse.ContainsKey(rightReference), $"{path}: {rightReference} stands for two objects.");
                Forward[leftReference] = rightReference;
                _reverse[rightReference] = leftReference;
                _pending.Enqueue((leftValue, rightValue, $"{path} ({leftReference} -> {rightReference})"));
                return;
            }

            Assert.False(right is CosReference, $"{path}: the direct object {left} became the indirect object {right}.");
            CompareDirect(leftValue, rightValue, path);
        }

        private void CompareDirect(CosObject left, CosObject right, string path)
        {
            switch (left)
            {
                case CosArray leftArray:
                    CosArray rightArray = Assert.IsType<CosArray>(right);
                    Assert.True(leftArray.Count == rightArray.Count, $"{path}: array length {leftArray.Count} became {rightArray.Count}.");
                    for (int index = 0; index < leftArray.Count; index++)
                    {
                        Compare(leftArray[index], rightArray[index], $"{path}[{index}]");
                    }

                    break;
                case CosStream leftStream:
                    CosStream rightStream = Assert.IsType<CosStream>(right);
                    CompareDictionaries(leftStream.Dictionary, rightStream.Dictionary, path, ignoreLength: true);
                    Assert.True(leftStream.EncodedData.Span.SequenceEqual(rightStream.EncodedData.Span), $"{path}: the stream data changed.");
                    break;
                case CosDictionary leftDictionary when PageCopies && PageType.Equals(expected.Resolve(leftDictionary.TryGetValue(TypeKey, out CosObject? type) ? type : null)):
                    CosDictionary rightPage = Assert.IsType<CosDictionary>(right);
                    Assert.False(rightPage.ContainsKey(ParentKey), $"{path}: the copied page has a Parent.");
                    CompareDictionaries(CutOut(leftDictionary), rightPage, path, ignoreLength: false);
                    break;
                case CosDictionary leftDictionary:
                    CompareDictionaries(leftDictionary, Assert.IsType<CosDictionary>(right), path, ignoreLength: false);
                    break;
                default:
                    Assert.True(left.Equals(right), $"{path}: {left} became {right}.");
                    break;
            }
        }

        /// <summary>The page's entries without <c>Parent</c>, with the attributes it inherits through its <c>Parent</c> chain added.</summary>
        private CosDictionary CutOut(CosDictionary page)
        {
            var own = new CosDictionary();
            foreach (KeyValuePair<CosName, CosObject> entry in page.Where(entry => !entry.Key.Equals(ParentKey)))
            {
                own[entry.Key] = entry.Value;
            }

            foreach (CosName key in Inheritable.Where(key => expected.Resolve(own.TryGetValue(key, out CosObject? value) ? value : null) is CosNull))
            {
                for (CosObject node = expected.Resolve(page.TryGetValue(ParentKey, out CosObject? up) ? up : null);
                    node is CosDictionary ancestor;
                    node = expected.Resolve(ancestor.TryGetValue(ParentKey, out CosObject? next) ? next : null))
                {
                    if (ancestor.TryGetValue(key, out CosObject? inherited) && expected.Resolve(inherited) is not CosNull)
                    {
                        own[key] = inherited;
                        break;
                    }
                }
            }

            return own;
        }

        private void CompareDictionaries(CosDictionary left, CosDictionary right, string path, bool ignoreLength)
        {
            IEnumerable<CosName> Keys(CosDictionary dictionary, PdfDocument document) =>
                dictionary.Where(entry => !(ignoreLength && entry.Key.Equals(Length)) && document.Resolve(entry.Value) is not CosNull).Select(entry => entry.Key);

            CosName[] leftKeys = [.. Keys(left, expected).OrderBy(key => key.Value, StringComparer.Ordinal)];
            CosName[] rightKeys = [.. Keys(right, actual).OrderBy(key => key.Value, StringComparer.Ordinal)];
            Assert.True(leftKeys.SequenceEqual(rightKeys), $"{path}: keys {string.Join(' ', leftKeys.Select(key => key.Value))} became {string.Join(' ', rightKeys.Select(key => key.Value))}.");
            foreach (CosName key in leftKeys)
            {
                Compare(left[key], right[key], $"{path}{key}");
            }
        }
    }
}
