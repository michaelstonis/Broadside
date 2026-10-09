using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>The labels the document gives its pages, such as <c>i</c>, <c>ii</c>, <c>1</c>, <c>A-1</c>: a live view over the catalog's <c>PageLabels</c> number tree.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.4.2 and §7.9.7. The tree maps the index of each range's first page to a page label dictionary; a page takes the
/// range with the greatest key at or below its index. Labels are computed on every call from the tree, which is read through the
/// document's shared number tree reader (Limits-guided lookup, with a checked index of the whole tree as the fallback).
/// </para>
/// <para>
/// Repairs, each reported the first time it is met (in strict mode, the call throws): pages before the first range (the tree
/// "shall include a value for page index 0") are numbered in decimal from 1, as Acrobat and PDFBox do (<c>PageLabelsMissingZeroKey</c>);
/// a value that is not a dictionary, and a negative key, are skipped, so the range before continues (<c>PageLabelInvalid</c>). See
/// <see cref="PdfPageLabelRange"/> for the repairs inside a range.
/// </para>
/// </remarks>
public sealed class PdfPageLabels
{
    private readonly PdfDocument _document;
    private readonly NumberTreeReader _tree;

    internal PdfPageLabels(PdfDocument document, NumberTreeReader tree)
    {
        _document = document;
        _tree = tree;
    }

    /// <summary>Gets the root of the number tree.</summary>
    /// <remarks>ISO 32000-2 §7.9.7, Table 37.</remarks>
    public CosDictionary Root => _tree.Root;

    /// <summary>Gets the ranges, in page order: the entries of the number tree whose key is a page index and whose value is a page label dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.4.2. Read from the tree on every call.</remarks>
    public IReadOnlyList<PdfPageLabelRange> Ranges
    {
        get
        {
            var ranges = new List<PdfPageLabelRange>();
            foreach ((int key, CosObject value) in _tree.Enumerate())
            {
                if (Range(key, value) is { } range)
                {
                    ranges.Add(range);
                }
            }

            return ranges;
        }
    }

    /// <summary>Returns the label of the page at <paramref name="pageIndex"/>.</summary>
    /// <param name="pageIndex">The page's index, from 0.</param>
    /// <returns>The label: the range's prefix followed by the page's number in the range's style.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not the index of a page of the document.</exception>
    /// <exception cref="DiagnosticException">In strict mode, the label needs a repair.</exception>
    /// <remarks>ISO 32000-2 §12.4.2, Table 161.</remarks>
    public string GetLabel(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _document.Pages.Count);
        return Label(pageIndex);
    }

    /// <summary>Returns the label of every page, in page order.</summary>
    /// <returns>One label per page.</returns>
    /// <exception cref="DiagnosticException">In strict mode, a label needs a repair.</exception>
    /// <remarks>ISO 32000-2 §12.4.2, Table 161.</remarks>
    public IReadOnlyList<string> GetLabels()
    {
        int count = _document.Pages.Count;
        string[] labels = new string[count];
        for (int index = 0; index < count; index++)
        {
            labels[index] = Label(index);
        }

        return labels;
    }

    private string Label(int pageIndex)
    {
        int key = pageIndex;
        while (_tree.TryGetFloor(key, out int start, out CosObject? value))
        {
            if (Range(start, value) is { } range)
            {
                return range.GetLabel(pageIndex - start);
            }

            if (start <= 0)
            {
                break;
            }

            key = start - 1;
        }

        Report(DiagnosticCodes.PageLabelsMissingZeroKey, "The page labels number tree shall have a range for page index 0; pages before its first range are numbered in decimal from 1.");
        return (pageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The range for a tree entry, or null (reported) when the key is negative or the value is not a dictionary.</summary>
    private PdfPageLabelRange? Range(int key, CosObject value)
    {
        if (key < 0)
        {
            Report(DiagnosticCodes.PageLabelInvalid, "A page labels number tree key shall be a page index; a negative key is ignored.");
            return null;
        }

        if (value is not CosDictionary dictionary)
        {
            Report(DiagnosticCodes.PageLabelInvalid, "A page labels number tree value shall be a page label dictionary; the entry is ignored.");
            return null;
        }

        return new PdfPageLabelRange(_document, key, dictionary, _tree.RootReference ?? _document.CatalogReference);
    }

    private void Report(string code, string message) =>
        _document.DiagnosticSink.Report(code, DiagnosticSeverity.Warning, message, objectReference: _tree.RootReference ?? _document.CatalogReference);
}
