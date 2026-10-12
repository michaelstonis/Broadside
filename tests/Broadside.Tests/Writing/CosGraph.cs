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

    private sealed class Comparer(PdfDocument expected, PdfDocument actual)
    {
        private readonly Dictionary<CosReference, CosReference> _reverse = [];
        private readonly Queue<(CosObject Expected, CosObject Actual, string Path)> _pending = new();

        public Dictionary<CosReference, CosReference> Forward { get; } = [];

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
                case CosDictionary leftDictionary:
                    CompareDictionaries(leftDictionary, Assert.IsType<CosDictionary>(right), path, ignoreLength: false);
                    break;
                default:
                    Assert.True(left.Equals(right), $"{path}: {left} became {right}.");
                    break;
            }
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
