using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// The pages of a linearized file read through range requests, found through its page offset hint table instead of its page tree:
/// page <i>k</i>'s page object is the first object of the hint table's <i>k</i>-th entry, at the entry's offset (issue #208).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 F.3.7, F.3.8 and F.3.10: the pages of a linearized file are stored in page order, each page object first in its
/// page's objects, and "the PDF processor never needs to consult" the page tree, because every inheritable attribute is pushed
/// down to the pages. Table F.4 items 1 and 2 give each page's first object number (the parameter dictionary's <c>O</c> for the
/// first page, 1 for the second, then accumulated) and location; Annex G, G.4 uses them to go to any page without the tree.
/// </para>
/// <para>
/// Each page is checked before it is used: its page object's <c>N 0 obj</c> header shall be at the hinted offset (for the first
/// page, the offset the first-page cross-reference section gives), and the object shall be a page dictionary with its own
/// <c>MediaBox</c> and <c>Resources</c>, as pushing attributes down makes it. A page that fails is reported as
/// <see cref="DiagnosticCodes.LinearizationHintsInvalid"/> and the page collection reads the page tree instead. Only a first page
/// numbered 0 (no <c>P</c>, F.3.4) is supported.
/// </para>
/// </remarks>
internal sealed class HintedPageList
{
    private readonly PdfDocument _document;
    private readonly PdfSource _source;
    private readonly long _headerOffset;
    private readonly PdfLinearization _linearization;
    private readonly Func<int, XrefEntry?> _firstPageEntry;
    private readonly DiagnosticSink _diagnostics;
    private readonly OnceCache<int, PdfPage?> _pages = new();
    private readonly ConcurrentDictionary<CosReference, PdfPage> _created = new();

    /// <summary>Initializes a new instance of the <see cref="HintedPageList"/> class.</summary>
    /// <param name="document">The document.</param>
    /// <param name="source">The file.</param>
    /// <param name="headerOffset">The absolute offset of the header.</param>
    /// <param name="linearization">The file's linearization information.</param>
    /// <param name="firstPageEntry">Looks up an object number in the first-page cross-reference section.</param>
    /// <param name="diagnostics">Where to report hints that do not locate a page.</param>
    public HintedPageList(
        PdfDocument document,
        PdfSource source,
        long headerOffset,
        PdfLinearization linearization,
        Func<int, XrefEntry?> firstPageEntry,
        DiagnosticSink diagnostics)
    {
        _document = document;
        _source = source;
        _headerOffset = headerOffset;
        _linearization = linearization;
        _firstPageEntry = firstPageEntry;
        _diagnostics = diagnostics;
    }

    /// <summary>Gets the number of pages the hints describe, or <see langword="null"/> when they cannot be used.</summary>
    public int? Count =>
        _linearization.FirstPageNumber == 0
        && _linearization.Hints is { Pages.Count: > 0 } hints
        && hints.Pages.Count == _linearization.PageCount
            ? hints.Pages.Count
            : null;

    /// <summary>Returns the page at <paramref name="index"/>, or <see langword="null"/> when the hints do not locate it (reported).</summary>
    /// <param name="index">The page index, less than <see cref="Count"/>.</param>
    /// <returns>The page, or <see langword="null"/>.</returns>
    public PdfPage? Get(int index) => _pages.GetOrCreate(index, this, static (index, self) => new Created<PdfPage?>(self.Create(index)), static (_, _) => null);

    /// <summary>Returns the page view already handed out for the page object <paramref name="reference"/>, so a later walk of the tree keeps it.</summary>
    /// <param name="reference">The page object's reference.</param>
    /// <param name="page">The view.</param>
    /// <returns><see langword="true"/> when one was handed out.</returns>
    public bool TryGetCreated(CosReference reference, [MaybeNullWhen(false)] out PdfPage page) => _created.TryGetValue(reference, out page);

    private PdfPage? Create(int index)
    {
        PdfPageHint hint = _linearization.Hints!.Pages[index];
        int number = index == 0 ? _linearization.FirstPageObjectNumber : hint.FirstObjectNumber;
        bool located = index == 0
            ? hint.FirstObjectNumber == number && _firstPageEntry(number) is { Kind: XrefEntryKind.InUse } entry && _headerOffset + entry.Offset == hint.Offset
            : StartsWithHeader(hint.Offset, number);
        var reference = new CosReference(number, 0);
        if (located
            && _document.Resolve(reference) is CosDictionary dictionary
            && dictionary.TryGetValue(KnownNames.Type, out CosObject? type) && KnownNames.Page.Equals(type)
            && dictionary.ContainsKey(KnownNames.MediaBox)
            && dictionary.ContainsKey(KnownNames.Resources))
        {
            var page = new PdfPage(_document, dictionary, reference, ancestors: null);
            PageTreeReader.CheckPage(page, _diagnostics);
            return _created.GetOrAdd(reference, page);
        }

        _diagnostics.Report(
            DiagnosticCodes.LinearizationHintsInvalid,
            DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"The page offset hint table does not locate page {index + 1}'s page object (object {number} at offset {hint.Offset}, with its own MediaBox and Resources); the pages are read from the page tree instead."),
            objectReference: _linearization.Reference);
        return null;
    }

    /// <summary>Whether the bytes at <paramref name="offset"/> start with the header <c>N 0 obj</c> of object <paramref name="number"/>.</summary>
    private bool StartsWithHeader(long offset, int number)
    {
        if (offset < _headerOffset || offset >= _source.Length)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[32];
        number.TryFormat(expected, out int written, provider: CultureInfo.InvariantCulture);
        " 0 obj"u8.CopyTo(expected[written..]);
        written += " 0 obj"u8.Length;
        ReadOnlySpan<byte> actual = _source.GetWindow(offset, written + 1).Span;
        return actual.StartsWith(expected[..written]) && (actual.Length == written || !CosLexer.IsRegular(actual[written]));
    }
}
