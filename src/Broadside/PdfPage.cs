using Broadside.Annotations;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Rendering;

namespace Broadside;

/// <summary>One page of a document: a live view over a page object and the page tree nodes it inherits attributes from.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.3.3 (page objects, Table 31), §7.7.3.4 (inheritance) and §14.11.2 (page boundaries). Every property reads the COS
/// objects when called, so a change made through <see cref="Dictionary"/> or an ancestor node is visible at once.
/// </para>
/// <para>
/// <c>Resources</c>, <c>MediaBox</c>, <c>CropBox</c> and <c>Rotate</c> are inherited: when the page does not have the entry, the
/// nearest ancestor node that has it supplies it, as-is and never merged (§7.7.3.4). Ancestors are the nodes on the path from the
/// page tree root that the page was found through. A value that is missing or malformed reads as the default the specification
/// gives; the reader records a diagnostic for it when it first walks the page tree.
/// </para>
/// </remarks>
public sealed class PdfPage
{
    /// <summary>The media box used when a page has none, US Letter, as pdf.js and PDFBox use.</summary>
    private static readonly PdfRectangle DefaultMediaBox = new(0, 0, 612, 792);

    private readonly PdfDocument _document;
    private PageTreeAncestor? _ancestors;
    private volatile AnnotationList? _annotations;

    internal PdfPage(PdfDocument document, CosDictionary dictionary, CosReference? reference, PageTreeAncestor? ancestors)
    {
        _document = document;
        _ancestors = ancestors;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the page object.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the page object, or <see langword="null"/> when the page tree holds it directly (until a page edit through <see cref="PdfDocument.Pages"/> makes it indirect).</summary>
    public CosReference? Reference { get; private set; }

    /// <summary>Gets the page's additional actions (<c>AA</c>, PDF 1.2): actions when the page opens and closes; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §12.6.3, Table 198. Not inheritable; read on every call, never created by reading.</remarks>
    public PdfPageAdditionalActions? AdditionalActions =>
        PdfAdditionalActions.Create(_document, Dictionary.TryGetValue(ActionNames.AA, out CosObject? aa) ? aa : null, Reference, static (d, a, r, o) => new PdfPageAdditionalActions(d, a, r, o));

    /// <summary>Gets the media box: the boundaries of the physical medium the page is displayed or printed on. Inheritable.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Required; when missing or malformed, US Letter <c>[0 0 612 792]</c>.</remarks>
    public PdfRectangle MediaBox => ReadBox(KnownNames.MediaBox, inheritable: true, out PdfRectangle box) == PageAttributeState.Valid ? box : DefaultMediaBox;

    /// <summary>Gets the crop box: the region the page's contents are clipped to when displayed or printed. Inheritable.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Defaults to the media box; reduced to its intersection with the media box.</remarks>
    public PdfRectangle CropBox => ClipToMediaBox(KnownNames.CropBox, inheritable: true, MediaBox);

    /// <summary>Gets the bleed box: the region the contents are clipped to in a production environment.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Defaults to the crop box; reduced to its intersection with the media box.</remarks>
    public PdfRectangle BleedBox => ClipToMediaBox(KnownNames.BleedBox, inheritable: false, CropBox);

    /// <summary>Gets the trim box: the intended dimensions of the finished page after trimming.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Defaults to the crop box; reduced to its intersection with the media box.</remarks>
    public PdfRectangle TrimBox => ClipToMediaBox(KnownNames.TrimBox, inheritable: false, CropBox);

    /// <summary>Gets the art box: the extent of the page's meaningful content.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §14.11.2. Defaults to the crop box; reduced to its intersection with the media box.</remarks>
    public PdfRectangle ArtBox => ClipToMediaBox(KnownNames.ArtBox, inheritable: false, CropBox);

    /// <summary>Gets or sets the number of degrees the page is rotated clockwise when displayed or printed: 0, 90, 180 or 270. Inheritable.</summary>
    /// <exception cref="ArgumentOutOfRangeException">On set, the value is not a multiple of 90.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.7.3.3, Table 31 (PDF 1.0). The entry shall be a multiple of 90; it is normalized into 0 to 270 (−90 reads as 270,
    /// 450 as 90). A value that is not a multiple of 90 reads as 0.
    /// </para>
    /// <para>
    /// Setting writes the page's own <c>Rotate</c>, normalized into 0 to 270, which overrides an inherited one (§7.7.3.4); setting 0
    /// when no ancestor gives a rotation removes the entry instead. A mutation: no read may run concurrently with it.
    /// </para>
    /// </remarks>
    public int Rotation
    {
        get => ReadRotation(out int degrees) is PageAttributeState.Valid or PageAttributeState.Repaired ? degrees : 0;
        set
        {
            if (value % 90 != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A page's rotation shall be a multiple of 90 degrees.");
            }

            int degrees = ((value % 360) + 360) % 360;
            if (degrees == 0 && !AncestorsHave(KnownNames.Rotate))
            {
                Dictionary.Remove(KnownNames.Rotate);
                return;
            }

            Dictionary[KnownNames.Rotate] = new CosInteger(degrees);
        }
    }

    /// <summary>Gets the resource dictionary the page's content streams use, or <see langword="null"/> when the page has none. Inheritable.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31, and §7.8.3.</remarks>
    public CosDictionary? Resources => FindInheritable(KnownNames.Resources) as CosDictionary;

    /// <summary>Gets the size of a default user space unit in multiples of 1/72 inch. 1.0 when absent or not positive.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31 (PDF 1.6).</remarks>
    public double UserUnit => ReadUserUnit(out double value) == PageAttributeState.Valid ? value : 1.0;

    /// <summary>Returns the font the page's resources name <paramref name="resourceName"/>, as the <c>Tf</c> operator selects it.</summary>
    /// <param name="resourceName">The key in the <c>Font</c> subdictionary of the page's (possibly inherited) resource dictionary, without the slash.</param>
    /// <returns>The font view, or <see langword="null"/> when the resources have no font of that name.</returns>
    /// <exception cref="DiagnosticException">In strict mode, when the font dictionary's <c>Type</c> or <c>Subtype</c> is not a font's.</exception>
    /// <remarks>ISO 32000-2 §7.8.3, Table 34 (<c>Font</c>), and §9.5. The same font dictionary always gives the same view.</remarks>
    public PdfFont? GetFont(string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        return Resources is { } resources
            && resources.TryGetValue(FontNames.Font, out CosObject? fonts)
            && _document.Resolve(fonts) is CosDictionary fontResources
            && fontResources.TryGetValue(new CosName(resourceName), out CosObject? font)
            ? _document.GetFont(font)
            : null;
    }

    /// <summary>Returns the image XObject the page's resources name <paramref name="resourceName"/>, as the <c>Do</c> operator paints it.</summary>
    /// <param name="resourceName">The key in the <c>XObject</c> subdictionary of the page's (possibly inherited) resource dictionary, without the slash.</param>
    /// <returns>The image view; <see langword="null"/> when the resources have no XObject of that name or it is not an image.</returns>
    /// <remarks>ISO 32000-2 §7.8.3, Table 34 (<c>XObject</c>), §8.8 and §8.9.5.</remarks>
    public PdfImage? GetImage(string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        return Resources is { } resources
            && resources.TryGetValue(ImageNames.XObject, out CosObject? xobjects)
            && _document.Resolve(xobjects) is CosDictionary xobjectResources
            && xobjectResources.TryGetValue(new CosName(resourceName), out CosObject? image)
            ? _document.GetImage(image)
            : null;
    }
    /// <summary>Gets the files associated with the page (<c>AF</c>).</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31 (PDF 2.0), and §14.13.4. Read on every call; see <see cref="PdfDocument.ReadAssociatedFiles"/>.</remarks>
    public IReadOnlyList<PdfFileSpecification> AssociatedFiles => _document.ReadAssociatedFiles(Dictionary, Reference);

    /// <summary>Gets the page's annotations, in the order of its <c>Annots</c> array, each typed by its subtype.</summary>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the <c>Annots</c> array.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.7.3.3, Table 31 (<c>Annots</c>), and §12.5. The list is built on first use and rebuilt when the page's
    /// <c>Annots</c> entry or array, or an annotation's <c>Subtype</c>, changes, or an object is added or removed (§7.3.10); each annotation is a live view over its dictionary, and the same dictionary always
    /// gives the same instance. An element that is not an annotation dictionary is skipped; one listed twice appears twice. An
    /// <c>Annots</c> entry on a page tree node above the page, which is not inheritable, is used as viewers use it.
    /// </para>
    /// <para>Deviations in the array are recorded in <see cref="PdfDocument.Diagnostics"/> when the list is built.</para>
    /// </remarks>
    public IReadOnlyList<PdfAnnotation> Annotations
    {
        get
        {
            int objectsVersion = _document.ObjectsVersion;
            (CosObject? entry, bool inherited) = FindAnnots();
            var array = _document.Resolve(entry) as CosArray;
            AnnotationList? cached = _annotations;
            if (cached is not null && cached.ObjectsVersion == objectsVersion && ReferenceEquals(cached.Entry, entry) && ReferenceEquals(cached.Array, array)
                && cached.ArrayVersion == (array?.Version ?? 0)
                && cached.Items.All(annotation => Equals(annotation.CreatedSubtype, PdfAnnotation.ReadSubtype(_document, annotation.Dictionary, out _))))
            {
                return cached.Items;
            }

            IReadOnlyList<PdfAnnotation> items = _document.AnnotationIndex.Read(this, entry, inherited);
            _annotations = new AnnotationList(entry, array, array?.Version ?? 0, objectsVersion, items);
            return items;
        }
    }

    /// <summary>
    /// Fetches, asynchronously, the objects reading this page needs, so that interpreting it afterwards never waits on the network:
    /// the page object, its resources (inherited ones included), its content streams, annotations and their appearances, and
    /// everything they refer to, but not other pages.
    /// </summary>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>A task that completes when the objects are cached; already completed for a document whose bytes are all at hand.</returns>
    /// <exception cref="HttpRequestException">The server answered with an error status, or could not be reached.</exception>
    /// <exception cref="IOException">The server's answers are inconsistent: the file changed while it was read, for instance.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// Spec #33, viewer capability 5; ADR 0010. Only a document opened through
    /// <see cref="PdfEngine.OpenAsync(HttpClient, Uri, CancellationToken)"/> fetches anything. Where
    /// <see cref="PdfOptions.SynchronousRangeFetch"/> is off (always on the browser, which cannot block on HTTP), a synchronous read
    /// of a page before its prefetch throws <see cref="PdfRangeNotLoadedException"/>; elsewhere it blocks on the requests instead, and
    /// a prefetch only makes them parallel and asynchronous. The bytes stay cached up to <see cref="PdfOptions.RangeCacheLimit"/>.
    /// </remarks>
    public Task PrefetchAsync(CancellationToken cancellationToken = default) => _document.PrefetchAsync(this, cancellationToken);

    /// <summary>Runs the page's content through the content interpreter and reports what it paints to <paramref name="processor"/>.</summary>
    /// <param name="processor">The processor; for several at once, a <see cref="CompositeContentProcessor"/>.</param>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.8.2, §8.2 to §8.5. The content is the page's <c>Contents</c> stream, or the streams of its <c>Contents</c>
    /// array read as one (Table 31), decoded through their filters; the names it uses resolve in <see cref="Resources"/>. Geometry is
    /// reported in user space with the CTM to default user space, starting at the identity: the media box, crop box, rotation and
    /// user unit are the consumer's to apply. In lenient mode malformed content is repaired and recorded in
    /// <see cref="PdfDocument.Diagnostics"/>, once per kind of deviation per content stream. Reads the page live, on every call.
    /// </remarks>
    public void ProcessContent(ContentProcessor processor) => ProcessContent(processor, ContentInterpreter.DefaultOptions);

    /// <summary>Runs the page's content through the content interpreter, with limits and cancellation.</summary>
    /// <param name="processor">The processor; for several at once, a <see cref="CompositeContentProcessor"/>.</param>
    /// <param name="options">Limits and cancellation for the run.</param>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <exception cref="OperationCanceledException">The options' cancellation token was canceled.</exception>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2 to §8.5. As <see cref="ProcessContent(ContentProcessor)"/>.</remarks>
    public void ProcessContent(ContentProcessor processor, ContentOptions options)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(options);
        ContentInterpreter.RunPage(this, _document, processor, options);
    }

    /// <summary>
    /// Runs the page's content through <paramref name="editor"/> and, when it made edits, replaces the content with the result: one
    /// stream in which every byte the edits did not touch is as it was.
    /// </summary>
    /// <param name="editor">The editor; it numbers the operators and makes its edits during the run.</param>
    /// <returns><see langword="true"/> when the editor made edits and the content was replaced; otherwise the page is unchanged.</returns>
    /// <exception cref="InvalidOperationException">The editor is already rewriting.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.8.2; Table 31. The run is the one <see cref="ProcessContent(ContentProcessor)"/> makes. The result is written
    /// unfiltered (§7.3.8.2; a save compresses it) into a new stream (§7.3.10, <see cref="PdfDocument.AddObject"/>) that the page's
    /// <c>Contents</c> then names alone; a <c>Contents</c> array's parts are joined by line feeds. The old streams are not written
    /// into, so another page that shows them keeps its content; they stay objects of the document, which a save with
    /// <see cref="PdfSaveOptions.RemoveUnreachableObjects"/> drops when nothing else refers to them.
    /// </remarks>
    public bool RewriteContent(ContentEditor editor) => RewriteContent(editor, ContentInterpreter.DefaultOptions);

    /// <summary>Runs the page's content through <paramref name="editor"/> with limits and cancellation, and stores the edited content.</summary>
    /// <param name="editor">The editor.</param>
    /// <param name="options">Limits and cancellation for the run.</param>
    /// <returns><see langword="true"/> when the content was replaced.</returns>
    /// <exception cref="InvalidOperationException">The editor is already rewriting.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <exception cref="OperationCanceledException">The options' cancellation token was canceled; the page is unchanged.</exception>
    /// <remarks>ISO 32000-2 §7.8.2; Table 31. As <see cref="RewriteContent(ContentEditor)"/>.</remarks>
    public bool RewriteContent(ContentEditor editor, ContentOptions options)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(options);
        return ContentRewriting.RewritePage(this, _document, editor, options);
    }

    /// <summary>Extracts the page's text: every glyph its content shows, with its Unicode text, position, font, size and rendering mode.</summary>
    /// <returns>The glyphs in reading order and their text, with their words, lines and plain text assembled on first use.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content, its fonts or the structure tree.</exception>
    /// <remarks>
    /// ISO 32000-2 §9.4.4, §9.10, §14.8.2.6.2, §14.8.2.5 and §14.9; see <see cref="Text.TextExtractor"/>. A tagged page reads in
    /// logical content order, the order of its structure tree, with structure elements' <c>ActualText</c> and <c>Lang</c> applied
    /// (<see cref="Text.TextReadingOrder.Structure"/>); other pages in content order. Text hidden by optional content and artifacts
    /// are left out; invisible text and text replaced by <c>ActualText</c> are extracted and flagged (<see cref="Text.TextGlyphFlags"/>).
    /// A structure element whose content spans pages gives its replacement once, on the page where it shows its first glyph in
    /// logical order, so pages read one after another hold it once, in whatever order they are read; on its other pages its glyphs
    /// are still returned, in order, with their element and replacement flag, but each with an empty text range, so they add no
    /// text, words or lines. When the element's content starts with items that show no glyph (an image) on an earlier page, that
    /// page is extracted once per document to find out (in strict mode its deviations then throw here too). Positions are in
    /// default user space. Annotation appearances are not part of the page's content and are not extracted.
    /// </remarks>
    public Text.TextPage ExtractText() => ExtractText(new Text.TextExtractionOptions());

    /// <summary>Extracts the page's text with options.</summary>
    /// <param name="options">What to extract, in which order, and the content run's options.</param>
    /// <returns>The glyphs in the order the options ask for and their text.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content, its fonts or the structure tree.</exception>
    /// <exception cref="OperationCanceledException">The content options' cancellation token was canceled.</exception>
    /// <remarks>ISO 32000-2 §9.4.4, §9.10, §8.11.3.1, §14.8.2 and §14.9. As <see cref="ExtractText()"/>.</remarks>
    public Text.TextPage ExtractText(Text.TextExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Issue #284: the thread's extractor is reused, so only the returned page (its glyphs and text, copied at their exact size) is allocated per page.
        // Issue #185: structure order reads the extractor's glyphs and content items, so it runs before the extractor is returned.
        Text.TextExtractor extractor = Text.TextExtractor.Rent(options);
        try
        {
            ProcessContent(extractor, options.ContentOptions ?? ContentInterpreter.DefaultOptions);
            return options.ReadingOrder == Text.TextReadingOrder.Structure && Text.StructureOrder.Build(_document, this, extractor, options) is { } ordered
                ? ordered
                : extractor.ToTextPage();
        }
        finally
        {
            Text.TextExtractor.Return(extractor);
        }
    }

    /// <summary>Builds the page's display list: its content interpreted once into resolved drawing operations any backend replays.</summary>
    /// <returns>The list; a new one on every call, reading the page live.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <remarks>
    /// ADR 0006; ISO 32000-2 §8.5.3 (path painting) and §8.6 (colour). The content runs through the same interpreter as
    /// <see cref="ProcessContent(ContentProcessor)"/>, so lenient repairs are recorded in <see cref="PdfDocument.Diagnostics"/> the
    /// same way. Geometry is in user space with the transform to default user space; the media box, crop box, rotation and user unit
    /// are the renderer's to apply. Colours keep their colour space; a backend converts them (see <see cref="DisplayColor"/>).
    /// </remarks>
    public DisplayList CreateDisplayList() => CreateDisplayList(ContentInterpreter.DefaultOptions);

    /// <summary>Builds the page's display list, with limits and cancellation.</summary>
    /// <param name="options">Limits, optional-content state and cancellation for the content run.</param>
    /// <returns>The list.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <exception cref="OperationCanceledException">The options' cancellation token was canceled.</exception>
    /// <remarks>ADR 0006; ISO 32000-2 §8.5.3, §8.6. As <see cref="CreateDisplayList()"/>.</remarks>
    public DisplayList CreateDisplayList(ContentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return DisplayListBuilder.Build(this, _document, options);
    }

    /// <summary>Builds the page's hit-test index: its display list's glyphs, images and paths, and its link annotations.</summary>
    /// <returns>The index, from a display list built for it under the document's default optional-content states.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <remarks>Viewer capability 4; ADR 0006. As <see cref="CreateHitTestIndex(DisplayList, PdfOptionalContentState?)"/>.</remarks>
    public HitTestIndex CreateHitTestIndex() => CreateHitTestIndex(CreateDisplayList());

    /// <summary>Builds the page's hit-test index from a display list the caller already has, adding the page's link annotations.</summary>
    /// <param name="displayList">The page's display list, such as the one a viewer caches to render tiles.</param>
    /// <param name="optionalContentState">
    /// The optional-content states that decide which links are visible, as the list was built with; <see langword="null"/> for the
    /// document's default configuration.
    /// </param>
    /// <returns>The index.</returns>
    /// <remarks>
    /// Viewer capability 4; ISO 32000-2 §12.5.6.5 (links: <c>QuadPoints</c> inside <c>Rect</c>, else <c>Rect</c>), §12.5.3
    /// (<c>Hidden</c> and <c>NoView</c> links are left out) and §8.11.3.3 (a link whose optional content is off is left out). The
    /// links are read from <see cref="Annotations"/> now; the list's content is indexed as by <see cref="DisplayList.CreateHitTestIndex"/>.
    /// </remarks>
    public HitTestIndex CreateHitTestIndex(DisplayList displayList, PdfOptionalContentState? optionalContentState = null)
    {
        ArgumentNullException.ThrowIfNull(displayList);
        return HitTestIndex.Create(displayList, HitTestIndex.CollectLinks(this, _document, optionalContentState));
    }

    /// <summary>Reads a page boundary; for an inheritable one, from the nearest node that has it.</summary>
    internal PageAttributeState ReadBox(CosName key, bool inheritable, out PdfRectangle box)
    {
        box = default;
        CosObject? value = inheritable ? FindInheritable(key) : Find(key);
        if (value is null)
        {
            return PageAttributeState.Absent;
        }

        if (_document.Resolve(value) is not CosArray { Count: 4 } array)
        {
            return PageAttributeState.Invalid;
        }

        Span<double> corners = stackalloc double[4];
        for (int index = 0; index < 4; index++)
        {
            if (_document.Resolve(array[index]) is not CosNumber number)
            {
                return PageAttributeState.Invalid;
            }

            corners[index] = number.ToDouble();
        }

        box = new PdfRectangle(corners[0], corners[1], corners[2], corners[3]);
        return PageAttributeState.Valid;
    }

    /// <summary>Reads <c>Rotate</c> normalized into 0 to 270; a real holding a multiple of 90 is <see cref="PageAttributeState.Repaired"/>.</summary>
    internal PageAttributeState ReadRotation(out int degrees)
    {
        degrees = 0;
        switch (FindInheritable(KnownNames.Rotate))
        {
            case null:
                return PageAttributeState.Absent;
            case CosInteger { Value: var value } when value % 90 == 0:
                degrees = (int)(((value % 360) + 360) % 360);
                return PageAttributeState.Valid;
            case CosReal { Value: var value } when value % 90 == 0 && Math.Abs(value) <= int.MaxValue:
                degrees = (((int)value % 360) + 360) % 360;
                return PageAttributeState.Repaired;
            default:
                return PageAttributeState.Invalid;
        }
    }

    /// <summary>Reads <c>Resources</c>.</summary>
    internal PageAttributeState ReadResources() => FindInheritable(KnownNames.Resources) switch
    {
        null => PageAttributeState.Absent,
        CosDictionary => PageAttributeState.Valid,
        _ => PageAttributeState.Invalid,
    };

    /// <summary>Reads <c>UserUnit</c>, which shall be positive.</summary>
    internal PageAttributeState ReadUserUnit(out double value)
    {
        value = 1.0;
        switch (Find(KnownNames.UserUnit))
        {
            case null:
                return PageAttributeState.Absent;
            case CosNumber number when number.ToDouble() > 0:
                value = number.ToDouble();
                return PageAttributeState.Valid;
            default:
                return PageAttributeState.Invalid;
        }
    }

    /// <summary>A box that defaults to <paramref name="fallback"/>, reduced to its intersection with the media box (§14.11.2).</summary>
    private PdfRectangle ClipToMediaBox(CosName key, bool inheritable, PdfRectangle fallback)
    {
        if (ReadBox(key, inheritable, out PdfRectangle box) != PageAttributeState.Valid)
        {
            return fallback;
        }

        return box.Intersect(MediaBox) ?? fallback;
    }

    /// <summary>The page's <c>Annots</c> entry as stored, else the nearest ancestor's (not inheritable, but read as viewers read it).</summary>
    private (CosObject? Entry, bool Inherited) FindAnnots()
    {
        if (Dictionary.TryGetValue(AnnotationNames.Annots, out CosObject? own))
        {
            return (own, false);
        }

        for (PageTreeAncestor? node = _ancestors; node is not null; node = node.Parent)
        {
            if (node.Dictionary.TryGetValue(AnnotationNames.Annots, out CosObject? value))
            {
                return (value, true);
            }
        }

        return (null, false);
    }

    /// <summary>
    /// The <c>Annots</c> entry the page reads from a page tree node above it, as stored (not resolved); <see langword="null"/> when the
    /// page has its own or no node has one. Not inheritable (Table 31), but read as viewers read it (§7.7.3.3).
    /// </summary>
    internal CosObject? FindInheritedAnnots() => FindAnnots() is (CosObject entry, true) ? entry : null;

    /// <summary>Gets the document the page belongs to.</summary>
    internal PdfDocument Document => _document;

    /// <summary>
    /// The value an inheritable entry has when the page does not have it itself: the nearest ancestor's entry as stored, not resolved
    /// (a reference stays a reference); <see langword="null"/> when the page has its own or no ancestor has one (§7.7.3.4).
    /// </summary>
    internal CosObject? FindInheritedEntry(CosName key)
    {
        if (Find(key) is not null)
        {
            return null;
        }

        for (PageTreeAncestor? node = _ancestors; node is not null; node = node.Parent)
        {
            if (EntryReader.Get(_document, node.Dictionary, key) is not null)
            {
                return node.Dictionary[key];
            }
        }

        return null;
    }

    /// <summary>Whether a node above the page has <paramref name="key"/>, resolving to a value.</summary>
    private bool AncestorsHave(CosName key)
    {
        for (PageTreeAncestor? node = _ancestors; node is not null; node = node.Parent)
        {
            if (EntryReader.Get(_document, node.Dictionary, key) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the page tree nodes above the page, nearest first, as the page tree was last read or rebuilt.</summary>
    internal PageTreeAncestor? Ancestors => _ancestors;

    /// <summary>Points the view at the page's place in a rebuilt page tree (issue #158): its reference and its new ancestors.</summary>
    /// <param name="reference">The page object's reference; a page the tree held directly gets one when the tree is rebuilt.</param>
    /// <param name="ancestors">The nodes above the page, nearest first.</param>
    internal void Relink(CosReference reference, PageTreeAncestor? ancestors)
    {
        Reference = reference;
        _ancestors = ancestors;
    }

    /// <summary>The page's own entry, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    private CosObject? Find(CosName key) => EntryReader.Get(_document, Dictionary, key);

    /// <summary>The page's entry, or the nearest ancestor's, resolved, used as-is (§7.7.3.4).</summary>
    private CosObject? FindInheritable(CosName key)
    {
        if (Find(key) is { } own)
        {
            return own;
        }

        for (PageTreeAncestor? node = _ancestors; node is not null; node = node.Parent)
        {
            if (EntryReader.Get(_document, node.Dictionary, key) is { } resolved)
            {
                return resolved;
            }
        }

        return null;
    }
}

/// <summary>A page tree node on the path from the root to a page, nearest first: the nodes a page inherits from (§7.7.3.4).</summary>
/// <param name="Dictionary">The node's dictionary.</param>
/// <param name="Parent">The node above it, or <see langword="null"/> for the root.</param>
/// <param name="Reference">The node's reference, or <see langword="null"/> when its parent holds it directly.</param>
internal sealed record PageTreeAncestor(CosDictionary Dictionary, PageTreeAncestor? Parent, CosReference? Reference);

/// <summary>
/// A page's annotation list and the <c>Annots</c> entry, array and array version it was built from, with the document's
/// <c>ObjectsVersion</c> then (a removed annotation object leaves the array unchanged, §7.3.10).
/// </summary>
internal sealed record AnnotationList(CosObject? Entry, CosArray? Array, int ArrayVersion, int ObjectsVersion, IReadOnlyList<PdfAnnotation> Items);

/// <summary>What reading a page attribute found.</summary>
internal enum PageAttributeState : byte
{
    /// <summary>The entry is absent; the default applies.</summary>
    Absent,

    /// <summary>The entry is well-formed.</summary>
    Valid,

    /// <summary>The entry deviates from its type but its value can be used.</summary>
    Repaired,

    /// <summary>The entry is malformed; the default applies.</summary>
    Invalid,
}
