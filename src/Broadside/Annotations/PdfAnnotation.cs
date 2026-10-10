using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Annotations;

/// <summary>An annotation: an object on a page, such as a note, a link or a form field's widget. A live view over the annotation dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.5. <see cref="PdfPage.Annotations"/> lists a page's annotations; each is an instance of the class its
/// <c>Subtype</c> selects (Table 171), and any other subtype, or none, is a <see cref="PdfUnknownAnnotation"/>. Markup annotations
/// (§12.5.6.2) derive from <see cref="PdfMarkupAnnotation"/>.
/// </para>
/// <para>
/// Every property reads the dictionary when called and returns the default the specification gives when an entry is absent,
/// without writing it. A malformed entry reads as its default and records a diagnostic the first time it is read (lenient mode); in
/// strict mode the property throws. Values such as <see cref="Rect"/>, <see cref="Border"/> and <see cref="Color"/> are snapshots
/// computed per call.
/// </para>
/// <para>
/// The same annotation dictionary always gives the same instance, whether it is reached from the page or from another annotation
/// (<see cref="PdfMarkupAnnotation.Popup"/>, <see cref="PdfMarkupAnnotation.InReplyTo"/>, <see cref="PdfPopupAnnotation.Parent"/>).
/// </para>
/// </remarks>
public abstract class PdfAnnotation
{
    private protected PdfAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page, PdfAnnotationKind kind)
    {
        Document = document;
        Dictionary = dictionary;
        Reference = reference;
        Page = page;
        Kind = kind;
    }

    /// <summary>Gets the annotation dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the annotation dictionary, or <see langword="null"/> when the page's <c>Annots</c> array holds it directly.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31: <c>Annots</c> "shall contain indirect references".</remarks>
    public CosReference? Reference { get; }

    /// <summary>
    /// Gets the page whose <c>Annots</c> array holds the annotation, or <see langword="null"/> when no page holds it (an annotation reached only
    /// from another one). Not the <c>P</c> entry, which is advisory: see <see cref="PageObject"/>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.5.2: "A given annotation dictionary shall be referenced from the Annots array of only one page." When a file
    /// breaks that rule, this is the first page that listed the annotation, and the others record <c>AnnotationSharedAcrossPages</c>.
    /// </remarks>
    public PdfPage? Page { get; }

    /// <summary>Gets the standard type the annotation is, from its <c>Subtype</c> when it was read; <see cref="PdfAnnotationKind.Unknown"/> for any other.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.1, Table 171.</remarks>
    public PdfAnnotationKind Kind { get; }

    /// <summary>Gets the <c>Subtype</c> entry as stored (a string reads as the name it spells), or <see langword="null"/> when it is absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166 (required).</remarks>
    public CosName? Subtype => ReadSubtype(Document, Dictionary, out _);

    /// <summary>Gets the annotation rectangle in default user space, normalized; <c>[0 0 0 0]</c> when <c>Rect</c> is missing or malformed.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166 (required), and §7.9.5: any two diagonally opposite corners, so an unnormalized rectangle is legal.</remarks>
    public PdfRectangle Rect
    {
        get
        {
            if (AnnotationValues.ReadRectangle(Document, Get(AnnotationNames.Rect)) is { } rect)
            {
                return rect;
            }

            Report(DiagnosticCodes.AnnotationRectInvalid, "The annotation's Rect entry is missing or not four numbers; it reads as [0 0 0 0].");
            return default;
        }
    }

    /// <summary>Gets the text displayed for the annotation, or an alternate description of it (<c>Contents</c>); <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166; a text string (§7.9.2.2).</remarks>
    public string? Contents => ReadText(AnnotationNames.Contents);

    /// <summary>Gets the page object the <c>P</c> entry names (PDF 1.3), or <see langword="null"/>. Advisory: <see cref="Page"/> is the page that holds the annotation.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public CosDictionary? PageObject => Get(AnnotationNames.P) as CosDictionary;

    /// <summary>Gets the annotation name, unique among the annotations of its page (<c>NM</c>, PDF 1.4), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public string? Name => ReadText(AnnotationNames.NM);

    /// <summary>Gets the modification date as stored (<c>M</c>, PDF 1.1), or <see langword="null"/>: a date string or text in any format.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166: processors "shall accept and display a string in any format".</remarks>
    public string? ModifiedText => ReadText(AnnotationNames.M);

    /// <summary>Gets the modification date (<c>M</c>) when it is a date (§7.9.4); <see langword="null"/> when absent or in another format, which is legal.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, and §7.9.4.</remarks>
    public PdfDate? Modified => PdfDate.TryParse(ModifiedText, out PdfDate date) ? date : null;

    /// <summary>Gets the annotation flags (<c>F</c>, PDF 1.1), every bit as stored; <see cref="PdfAnnotationFlags.None"/> when absent.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.5.3, Table 167. A real number is truncated, and bits the table does not define are kept; both record a
    /// diagnostic. Text annotations behave as if <see cref="PdfAnnotationFlags.NoZoom"/> and <see cref="PdfAnnotationFlags.NoRotate"/>
    /// were set, whatever this says (§12.5.6.4).
    /// </remarks>
    public PdfAnnotationFlags Flags
    {
        get
        {
            long bits;
            switch (Get(AnnotationNames.F))
            {
                case null:
                    return PdfAnnotationFlags.None;
                case CosInteger integer:
                    bits = integer.Value;
                    break;
                case CosReal real when double.IsFinite(real.Value) && Math.Abs(real.Value) <= uint.MaxValue:
                    bits = (long)real.Value;
                    Report(DiagnosticCodes.AnnotationFlagsInvalid, "The annotation's F entry shall be an integer; it is a real number, truncated.");
                    break;
                default:
                    Report(DiagnosticCodes.AnnotationFlagsInvalid, "The annotation's F entry shall be an integer; it reads as 0.");
                    return PdfAnnotationFlags.None;
            }

            if ((bits & ~0x3FFL) != 0)
            {
                Report(DiagnosticCodes.AnnotationFlagsInvalid, "The annotation's F entry sets bits Table 167 does not define, which shall be 0; they are kept.");
            }

            return (PdfAnnotationFlags)(int)(uint)bits;
        }
    }

    /// <summary>Gets the appearance dictionary (<c>AP</c>, PDF 1.2), or <see langword="null"/> when the annotation has none.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.5.5, Table 170, and §12.5.2, Table 166. Every annotation shall have one except a popup, link or projection
    /// annotation and one whose rectangle has zero width and zero height; that wording is PDF 2.0's, so a missing one records
    /// <c>AppearanceMissing</c> only in a PDF 2.0 file.
    /// </remarks>
    public PdfAppearanceDictionary? AppearanceDictionary
    {
        get
        {
            switch (Get(AnnotationNames.AP))
            {
                case CosDictionary dictionary:
                    return new PdfAppearanceDictionary(this, dictionary);
                case null:
                    if (RequiresAppearance())
                    {
                        Report(DiagnosticCodes.AppearanceMissing, "The annotation has no appearance dictionary, which a PDF 2.0 annotation of its type shall have; it has no appearance.");
                    }

                    return null;
                default:
                    Report(DiagnosticCodes.AppearanceEntryInvalid, "The annotation's AP entry is not a dictionary; it has no appearance.");
                    return null;
            }
        }
    }

    /// <summary>Gets the appearance state (<c>AS</c>, PDF 1.2) that selects an appearance from a state subdictionary, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, and §12.5.5.</remarks>
    public CosName? AppearanceState => ReadName(AnnotationNames.AS);

    /// <summary>Gets the border: from <c>BS</c> when present, else from <c>Border</c>, else solid and 1 point wide.</summary>
    /// <remarks>ISO 32000-2 §12.5.4, Table 168, and §12.5.2, Table 166 (<c>Border</c>, default <c>[0 0 1]</c>).</remarks>
    public PdfAnnotationBorder Border => ReadBorder();

    /// <summary>Gets the annotation's colour (<c>C</c>, PDF 1.1): its icon background, pop-up title bar or link border; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166. An empty array is transparent; 2 or more than 4 numbers read as absent with a diagnostic.</remarks>
    public PdfDeviceColor? Color => ReadColor(AnnotationNames.C);

    /// <summary>Gets the annotation's key in the structural parent tree (<c>StructParent</c>, PDF 1.3), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, and §14.7.5.4.</remarks>
    public int? StructParent => ReadInteger(AnnotationNames.StructParent);

    /// <summary>Gets the optional content group or membership dictionary that decides whether the annotation is visible (<c>OC</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, and §8.11.3.3.</remarks>
    public CosDictionary? OptionalContent => ReadDictionary(AnnotationNames.OC);

    /// <summary>Gets the files associated with the annotation (<c>AF</c>, PDF 2.0); empty when there are none.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, and §14.13.9; read as <see cref="PdfDocument.ReadAssociatedFiles"/> reads any owner's.</remarks>
    public IReadOnlyList<PdfFileSpecification> AssociatedFiles => Document.ReadAssociatedFiles(Dictionary, DiagnosticReference);

    /// <summary>Gets the stroking opacity used when the appearance is regenerated (<c>CA</c>, PDF 1.4); default 1.0.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166. Not used when the annotation has an appearance stream.</remarks>
    public double StrokingOpacity => ReadNumber(AnnotationNames.StrokingOpacity, 1.0);

    /// <summary>Gets the non-stroking opacity used when the appearance is regenerated (<c>ca</c>, PDF 2.0); default <see cref="StrokingOpacity"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166: "If a ca entry is not present ... the value of this CA entry shall also be used for nonstroking operations".</remarks>
    public double NonStrokingOpacity => ReadOptionalNumber(AnnotationNames.NonStrokingOpacity) ?? StrokingOpacity;

    /// <summary>Gets the blend mode used when painting the annotation onto the page (<c>BM</c>, PDF 2.0); default <see cref="BlendMode.Normal"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, and §11.3.5, Tables 134 and 135. A name the tables do not list reads as Normal with a diagnostic.</remarks>
    public BlendMode BlendMode
    {
        get
        {
            CosName? name = ReadName(AnnotationNames.BM);
            if (name is null || name.Value is "Normal" or "Compatible")
            {
                return BlendMode.Normal;
            }

            foreach (BlendMode mode in Enum.GetValues<BlendMode>())
            {
                if (string.Equals(Enum.GetName(mode), name.Value, StringComparison.Ordinal))
                {
                    return mode;
                }
            }

            Report(DiagnosticCodes.AnnotationValueInvalid, "The annotation's BM entry is not a standard blend mode; Normal is used.");
            return BlendMode.Normal;
        }
    }

    /// <summary>Gets the natural language of the annotation's text (<c>Lang</c>, PDF 2.0), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, and §14.9.2.</remarks>
    public string? Language => ReadText(AnnotationNames.Lang);

    internal PdfDocument Document { get; }

    /// <summary>Gets the reference diagnostics name: the annotation's own, or the page's when the annotation is direct.</summary>
    internal CosReference? DiagnosticReference => Reference ?? Page?.Reference;

    /// <summary>Gets the subtype the view was created for, to tell when a mutation changed it.</summary>
    internal CosName? CreatedSubtype { get; private set; }

    /// <summary>Returns whether the annotation's optional content (<c>OC</c>) leaves it visible in <paramref name="state"/>.</summary>
    /// <param name="state">Group states from the document's <see cref="PdfDocument.OptionalContent"/>, such as its default states.</param>
    /// <returns><see langword="false"/> only when the annotation is optional content that is hidden; flags such as <see cref="PdfAnnotationFlags.Hidden"/> are not applied.</returns>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166 (<c>OC</c>, PDF 1.5), and §8.11.3.3, through <see cref="PdfOptionalContentProperties.IsVisible"/>.</remarks>
    public bool IsVisible(PdfOptionalContentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Document.OptionalContent is not { } properties
            || !Dictionary.TryGetValue(AnnotationNames.OC, out CosObject? value)
            || properties.IsVisible(value, state);
    }

    /// <summary>
    /// Computes the matrix that maps an appearance's form space to default user space so that the appearance fills the annotation
    /// rectangle: Algorithm "Appearance streams" of ISO 32000-2 §12.5.5.
    /// </summary>
    /// <param name="rect">The annotation rectangle (normalized).</param>
    /// <param name="boundingBox">The appearance's <c>BBox</c>, in form space.</param>
    /// <param name="matrix">The appearance's <c>Matrix</c>.</param>
    /// <returns>
    /// The matrix <c>AA = Matrix × A</c>, where <c>A</c> maps the transformed bounding box onto the rectangle; <see langword="null"/>
    /// when either has zero width or height (nothing can be drawn).
    /// </returns>
    /// <remarks>
    /// ISO 32000-2 §12.5.5: the bounding box is transformed by the form matrix and the smallest upright rectangle containing the
    /// result is mapped onto <paramref name="rect"/> (lower-left to lower-left, upper-right to upper-right). The appearance's content
    /// is painted with this matrix concatenated to the page's CTM and clipped to its bounding box in form space; the form matrix
    /// is already part of the result and shall not be applied again.
    /// </remarks>
    public static Matrix? ComputeAppearanceMatrix(PdfRectangle rect, PdfRectangle boundingBox, Matrix matrix)
    {
        PathPoint a = matrix.Transform(boundingBox.Left, boundingBox.Bottom);
        PathPoint b = matrix.Transform(boundingBox.Right, boundingBox.Bottom);
        PathPoint c = matrix.Transform(boundingBox.Right, boundingBox.Top);
        PathPoint d = matrix.Transform(boundingBox.Left, boundingBox.Top);
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double bottom = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double top = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        if (right - left <= 0 || top - bottom <= 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        double scaleX = rect.Width / (right - left);
        double scaleY = rect.Height / (top - bottom);
        var toRect = new Matrix(scaleX, 0, 0, scaleY, rect.Left - (left * scaleX), rect.Bottom - (bottom * scaleY));
        return Matrix.Multiply(matrix, toRect);
    }

    /// <summary>Returns the normal appearance for the current appearance state.</summary>
    /// <returns>The form XObject, or <see langword="null"/> when there is none for the state.</returns>
    /// <remarks>ISO 32000-2 §12.5.5. As <see cref="GetAppearance(PdfAppearanceMode)"/> with <see cref="PdfAppearanceMode.Normal"/>.</remarks>
    public PdfFormXObject? GetAppearance() => GetAppearance(PdfAppearanceMode.Normal);

    /// <summary>Returns the appearance for <paramref name="mode"/> and the current appearance state (<see cref="AppearanceState"/>).</summary>
    /// <param name="mode">Normal, rollover or down.</param>
    /// <returns>
    /// The form XObject; <see langword="null"/> when the annotation has no appearance, or when the entry is a state subdictionary
    /// and <c>AS</c> is absent (a diagnostic: Table 166 requires it) or names a state the subdictionary lacks (legal: nothing is shown).
    /// </returns>
    /// <remarks>
    /// ISO 32000-2 §12.5.5, Table 170. A missing rollover or down entry falls back to the normal one; a rollover or down state
    /// subdictionary that lacks the state falls back to the normal appearance of that state.
    /// </remarks>
    public PdfFormXObject? GetAppearance(PdfAppearanceMode mode)
    {
        if (AppearanceDictionary is not { } appearance)
        {
            return null;
        }

        if (AppearanceState is { } state)
        {
            return Select(appearance, mode, state);
        }

        if (appearance.GetEntry(mode) is not { } entry)
        {
            return null;
        }

        if (!entry.HasStates)
        {
            return Checked(entry.Form);
        }

        Report(DiagnosticCodes.AppearanceStateMissing, "The annotation's appearance has states but no AS entry selects one, which Table 166 requires; it has no appearance.");
        return null;
    }

    /// <summary>Returns the appearance for <paramref name="mode"/> in the appearance state <paramref name="state"/>, whatever <c>AS</c> says.</summary>
    /// <param name="mode">Normal, rollover or down.</param>
    /// <param name="state">The appearance state, such as <c>On</c> or <c>Off</c>; ignored when the entry is a single stream.</param>
    /// <returns>The form XObject, or <see langword="null"/> when there is none.</returns>
    /// <remarks>ISO 32000-2 §12.5.5, Table 170, with the fallbacks of <see cref="GetAppearance(PdfAppearanceMode)"/>.</remarks>
    public PdfFormXObject? GetAppearance(PdfAppearanceMode mode, CosName state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return AppearanceDictionary is { } appearance ? Select(appearance, mode, state) : null;
    }

    /// <summary>Computes the matrix that places <paramref name="appearance"/> on the annotation rectangle (§12.5.5).</summary>
    /// <param name="appearance">One of this annotation's appearances.</param>
    /// <returns>The matrix from form space to default user space, or <see langword="null"/> when nothing can be drawn.</returns>
    /// <remarks>
    /// ISO 32000-2 §12.5.5, as <see cref="ComputeAppearanceMatrix(PdfRectangle, PdfRectangle, Matrix)"/> with <see cref="Rect"/>. An
    /// appearance without a bounding box uses <c>[0 0 width height]</c> of the rectangle, as pdf.js does. <c>NoZoom</c>,
    /// <c>NoRotate</c> and a watermark's <c>FixedPrint</c> add transformations of their own, which are the renderer's.
    /// </remarks>
    public Matrix? GetAppearanceMatrix(PdfFormXObject appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        PdfRectangle rect = Rect;
        PdfRectangle box = appearance.BoundingBox ?? new PdfRectangle(0, 0, rect.Width, rect.Height);
        return ComputeAppearanceMatrix(rect, box, appearance.Matrix);
    }

    /// <summary>Runs one of this annotation's appearance streams through the content interpreter, placed on the annotation rectangle.</summary>
    /// <param name="appearance">The appearance, such as <see cref="GetAppearance()"/> or one state of <see cref="AppearanceDictionary"/>.</param>
    /// <param name="processor">The processor; for several at once, a <see cref="CompositeContentProcessor"/>.</param>
    /// <returns><see langword="true"/> when the appearance ran; <see langword="false"/> when nothing can be drawn (<see cref="GetAppearanceMatrix"/> is null).</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <remarks>As <see cref="ProcessAppearance(PdfFormXObject, ContentProcessor, ContentOptions)"/> with default limits.</remarks>
    public bool ProcessAppearance(PdfFormXObject appearance, ContentProcessor processor) => ProcessAppearance(appearance, processor, ContentInterpreter.DefaultOptions);

    /// <summary>Runs one of this annotation's appearance streams through the content interpreter, placed on the annotation rectangle, with limits and cancellation.</summary>
    /// <param name="appearance">The appearance, such as <see cref="GetAppearance()"/> or one state of <see cref="AppearanceDictionary"/>.</param>
    /// <param name="processor">The processor; for several at once, a <see cref="CompositeContentProcessor"/>.</param>
    /// <param name="options">Limits and cancellation for the run.</param>
    /// <returns><see langword="true"/> when the appearance ran; <see langword="false"/> when nothing can be drawn (<see cref="GetAppearanceMatrix"/> is null).</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <exception cref="OperationCanceledException">The options' cancellation token was canceled.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.5.5, Algorithm "Appearance streams", and §8.10.1: one top-level run of kind <see cref="ContentRunKind.Appearance"/>
    /// in default user space, from the initial graphics state with the CTM set to the matrix <c>AA</c> (<see cref="GetAppearanceMatrix"/>;
    /// the form matrix is part of it and is not applied again), the clip intersected with the form's <c>BBox</c> in form space, names
    /// resolving in the form's resources (the page's when it has none, with <c>AppearanceResourcesMissing</c>: §7.8.3 requires an
    /// appearance's own). An appearance that draws itself is not run inside itself
    /// (<c>ContentFormCycle</c>). <c>NoZoom</c>, <c>NoRotate</c>, optional content and the annotation's transparency group are the caller's.
    /// </remarks>
    public bool ProcessAppearance(PdfFormXObject appearance, ContentProcessor processor, ContentOptions options)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(options);
        if (GetAppearanceMatrix(appearance) is not { } matrix)
        {
            return false;
        }

        ContentInterpreter.RunAppearance(appearance, matrix, Page, Reference ?? Page?.Reference, Document, processor, options);
        return true;
    }

    /// <summary>Creates the view of the class the dictionary's subtype selects, recording what the dictionary's type entries lack.</summary>
    internal static PdfAnnotation Create(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
    {
        CosName? subtype = ReadSubtype(document, dictionary, out bool isString);
        CosReference? diagnosticReference = reference ?? page?.Reference;
        if (subtype is null)
        {
            Report(document, DiagnosticCodes.AnnotationSubtypeMissing, "The annotation has no Subtype name, which Table 166 requires; it reads as an annotation of unknown type.", diagnosticReference);
        }
        else if (isString)
        {
            Report(document, DiagnosticCodes.AnnotationSubtypeNotName, "The annotation's Subtype entry shall be a name; it is a string, read as the name it spells.", diagnosticReference);
        }

        if (dictionary.TryGetValue(AnnotationNames.Type, out CosObject? type) && document.Resolve(type) is not CosNull and var typeValue && !AnnotationNames.Annot.Equals(typeValue))
        {
            Report(document, DiagnosticCodes.AnnotationTypeInvalid, "The annotation's Type entry, if present, shall be Annot; it is ignored.", diagnosticReference);
        }

        PdfAnnotation annotation = subtype?.Value switch
        {
            "Text" => new PdfTextAnnotation(document, dictionary, reference, page),
            "Link" => new PdfLinkAnnotation(document, dictionary, reference, page),
            "FreeText" => new PdfFreeTextAnnotation(document, dictionary, reference, page),
            "Line" => new PdfLineAnnotation(document, dictionary, reference, page),
            "Square" => new PdfSquareAnnotation(document, dictionary, reference, page),
            "Circle" => new PdfCircleAnnotation(document, dictionary, reference, page),
            "Polygon" => new PdfPolygonAnnotation(document, dictionary, reference, page),
            "PolyLine" => new PdfPolyLineAnnotation(document, dictionary, reference, page),
            "Highlight" => new PdfHighlightAnnotation(document, dictionary, reference, page),
            "Underline" => new PdfUnderlineAnnotation(document, dictionary, reference, page),
            "Squiggly" => new PdfSquigglyAnnotation(document, dictionary, reference, page),
            "StrikeOut" => new PdfStrikeOutAnnotation(document, dictionary, reference, page),
            "Caret" => new PdfCaretAnnotation(document, dictionary, reference, page),
            "Stamp" => new PdfStampAnnotation(document, dictionary, reference, page),
            "Ink" => new PdfInkAnnotation(document, dictionary, reference, page),
            "Popup" => new PdfPopupAnnotation(document, dictionary, reference, page),
            "FileAttachment" => new PdfFileAttachmentAnnotation(document, dictionary, reference, page),
            "Sound" => new PdfSoundAnnotation(document, dictionary, reference, page),
            "Movie" => new PdfMovieAnnotation(document, dictionary, reference, page),
            "Screen" => new PdfScreenAnnotation(document, dictionary, reference, page),
            "Widget" => new PdfWidgetAnnotation(document, dictionary, reference, page),
            "PrinterMark" => new PdfPrinterMarkAnnotation(document, dictionary, reference, page),
            "TrapNet" => new PdfTrapNetAnnotation(document, dictionary, reference, page),
            "Watermark" => new PdfWatermarkAnnotation(document, dictionary, reference, page),
            "3D" => new Pdf3DAnnotation(document, dictionary, reference, page),
            "Redact" => new PdfRedactAnnotation(document, dictionary, reference, page),
            "Projection" => new PdfProjectionAnnotation(document, dictionary, reference, page),
            "RichMedia" => new PdfRichMediaAnnotation(document, dictionary, reference, page),
            _ => new PdfUnknownAnnotation(document, dictionary, reference, page),
        };
        return annotation.WithCreatedSubtype(subtype);
    }

    /// <summary>Reads <c>Subtype</c>: a name, or a string read as a name (<paramref name="isString"/>); <see langword="null"/> otherwise.</summary>
    internal static CosName? ReadSubtype(PdfDocument document, CosDictionary dictionary, out bool isString)
    {
        isString = false;
        switch (dictionary.TryGetValue(AnnotationNames.Subtype, out CosObject? value) ? document.Resolve(value) : null)
        {
            case CosName name:
                return name;
            case CosString text when !text.Bytes.Contains((byte)0):
                isString = true;
                return new CosName(text.Bytes);
            default:
                return null;
        }
    }

    /// <summary>Whether an appearance dictionary is required: PDF 2.0, not a popup, link or projection, rectangle not of zero size.</summary>
    internal bool RequiresAppearance()
    {
        if (Kind is PdfAnnotationKind.Popup or PdfAnnotationKind.Link or PdfAnnotationKind.Projection || Document.Version < new PdfVersion(2, 0))
        {
            return false;
        }

        return !(Get(AnnotationNames.Rect) is CosArray { Count: 4 } rect
            && AnnotationValues.ReadNumber(Document, rect[0]) is { } x1 && x1 == AnnotationValues.ReadNumber(Document, rect[2])
            && AnnotationValues.ReadNumber(Document, rect[1]) is { } y1 && y1 == AnnotationValues.ReadNumber(Document, rect[3]));
    }

    /// <summary>Reports a deviation in this annotation.</summary>
    internal void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        Document.DiagnosticSink.Report(code, severity, message, objectReference: DiagnosticReference);

    /// <summary>Returns the entry for <paramref name="key"/>, resolved; <see langword="null"/> when absent, null, or a reference to nothing.</summary>
    private protected CosObject? Get(CosName key) => EntryReader.Get(Document, Dictionary, key);

    /// <summary>Records that a required entry is absent.</summary>
    private protected void ReportMissing(CosName key, string table) =>
        Report(DiagnosticCodes.AnnotationRequiredEntryMissing, $"The {Kind} annotation has no {key.Value} entry, which {table} requires.");

    /// <summary>Records that an entry has the wrong type and is ignored.</summary>
    private protected void ReportInvalid(CosName key, string expected) => Issue.Ignored(key, expected);

    /// <summary>The report for an entry of the wrong type (<c>AnnotationValueInvalid</c>).</summary>
    private protected EntryReport Issue => new(Document, DiagnosticCodes.AnnotationValueInvalid, DiagnosticReference, "The annotation");

    /// <summary>Reads a text string entry (§7.9.2.2) with <see cref="EntryReader"/>'s repairs.</summary>
    private protected string? ReadText(CosName key) => EntryReader.Text(Get(key), key, Issue);

    /// <summary>Reads a text string or text stream entry (§7.9.3): a stream's decoded bytes read as a text string.</summary>
    private protected string? ReadTextOrStream(CosName key)
    {
        switch (Get(key))
        {
            case null:
                return null;
            case CosString text:
                return text.DecodeText();
            case CosStream stream:
                return new CosString(Document.DecodeStream(stream).Span).DecodeText();
            default:
                ReportInvalid(key, "a text string or text stream");
                return null;
        }
    }

    /// <summary>Reads a byte string entry, such as a default appearance string, as Latin-1 text.</summary>
    private protected string? ReadByteString(CosName key)
    {
        switch (Get(key))
        {
            case null:
                return null;
            case CosString text:
                return System.Text.Encoding.Latin1.GetString(text.Bytes);
            default:
                ReportInvalid(key, "a string");
                return null;
        }
    }

    /// <summary>Reads a boolean entry.</summary>
    private protected bool ReadBoolean(CosName key, bool fallback) => EntryReader.Boolean(Get(key), key, Issue) ?? fallback;

    /// <summary>Reads a number entry.</summary>
    private protected double ReadNumber(CosName key, double fallback) => ReadOptionalNumber(key) ?? fallback;

    /// <summary>Reads an optional number entry.</summary>
    private protected double? ReadOptionalNumber(CosName key) => EntryReader.Number(Get(key), key, Issue);

    /// <summary>Reads an integer entry with <see cref="EntryReader"/>'s repairs.</summary>
    private protected int? ReadInteger(CosName key) => EntryReader.Int32(Get(key), key, Issue);

    /// <summary>Reads a name entry.</summary>
    private protected CosName? ReadName(CosName key) => EntryReader.Typed<CosName>(Get(key), key, "a name", Issue);

    /// <summary>Reads a name entry from a fixed set; an unknown name reads as <paramref name="fallback"/> silently (the sets are open), another type with a diagnostic.</summary>
    private protected T ReadChoice<T>(CosName key, T fallback, params ReadOnlySpan<(string Name, T Value)> choices)
    {
        if (ReadName(key) is { } name)
        {
            foreach ((string text, T value) in choices)
            {
                if (string.Equals(name.Value, text, StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        return fallback;
    }

    /// <summary>Reads a dictionary entry.</summary>
    private protected CosDictionary? ReadDictionary(CosName key) => EntryReader.Typed<CosDictionary>(Get(key), key, "a dictionary", Issue);

    /// <summary>Reads a stream entry.</summary>
    private protected CosStream? ReadStream(CosName key) => EntryReader.Typed<CosStream>(Get(key), key, "a stream", Issue);

    /// <summary>Reads an array entry.</summary>
    private protected CosArray? ReadArray(CosName key) => EntryReader.Typed<CosArray>(Get(key), key, "an array", Issue);

    /// <summary>Reads a colour array entry (Table 166 <c>C</c>).</summary>
    private protected PdfDeviceColor? ReadColor(CosName key)
    {
        PdfDeviceColor? color = AnnotationValues.ReadColor(Document, Get(key), out bool valid);
        if (!valid)
        {
            Report(DiagnosticCodes.ColorArrayInvalid, $"The annotation's {key.Value} entry is not an array of 0, 1, 3 or 4 numbers; it reads as absent.");
        }

        return color;
    }

    /// <summary>Reads an action entry (§12.6).</summary>
    private protected PdfAction? ReadAction(CosName key) =>
        Dictionary.TryGetValue(key, out CosObject? value) ? PdfAction.Create(Document, value, DiagnosticReference) : null;

    /// <summary>Reads a form XObject entry (§8.10).</summary>
    private protected PdfFormXObject? ReadForm(CosName key) =>
        ReadStream(key) is not null && Dictionary.TryGetValue(key, out CosObject? value) ? PdfFormXObject.Create(Document, value) : null;

    /// <summary>Reads a date entry (§7.9.4), recording a deviation from the date format.</summary>
    private protected PdfDate? ReadDate(CosName key)
    {
        if (ReadText(key) is not { } text)
        {
            return null;
        }

        switch (PdfDate.Parse(text, out PdfDate date))
        {
            case DateParseOutcome.Valid:
                return date;
            case DateParseOutcome.Repaired:
                Report(DiagnosticCodes.DateInvalid, $"The annotation's {key.Value} entry does not follow the date format of §7.9.4; it is read with the deviating fields repaired.");
                return date;
            default:
                Report(DiagnosticCodes.DateUnreadable, $"The annotation's {key.Value} entry is not a date (§7.9.4).");
                return null;
        }
    }

    /// <summary>Reads a flat array of x y pairs, such as <c>Vertices</c> or <c>L</c>; an odd count or a non-number records a diagnostic.</summary>
    private protected IReadOnlyList<PathPoint> ReadPoints(CosName key, bool required, string table)
    {
        CosObject? value = Get(key);
        if (value is null)
        {
            if (required)
            {
                ReportMissing(key, table);
            }

            return [];
        }

        List<double>? numbers = AnnotationValues.ReadNumbers(Document, value, out bool complete);
        if (numbers is null || !complete || numbers.Count % 2 != 0)
        {
            ReportInvalid(key, "an array of pairs of numbers");
            if (numbers is null)
            {
                return [];
            }
        }

        var points = new PathPoint[numbers.Count / 2];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = new PathPoint(numbers[2 * index], numbers[(2 * index) + 1]);
        }

        return points;
    }

    /// <summary>Reads an array of arrays of x y pairs, such as <c>InkList</c>.</summary>
    private protected IReadOnlyList<IReadOnlyList<PathPoint>> ReadPointLists(CosName key, bool required, string table)
    {
        CosObject? value = Get(key);
        if (value is null)
        {
            if (required)
            {
                ReportMissing(key, table);
            }

            return [];
        }

        if (value is not CosArray outer)
        {
            ReportInvalid(key, "an array of arrays of numbers");
            return [];
        }

        var lists = new List<IReadOnlyList<PathPoint>>(outer.Count);
        bool valid = true;
        foreach (CosObject element in outer)
        {
            List<double>? numbers = AnnotationValues.ReadNumbers(Document, element, out bool complete);
            valid &= numbers is not null && complete && numbers.Count % 2 == 0;
            if (numbers is null)
            {
                continue;
            }

            var points = new PathPoint[numbers.Count / 2];
            for (int index = 0; index < points.Length; index++)
            {
                points[index] = new PathPoint(numbers[2 * index], numbers[(2 * index) + 1]);
            }

            lists.Add(points);
        }

        if (!valid)
        {
            ReportInvalid(key, "an array of arrays of pairs of numbers");
        }

        return lists;
    }

    /// <summary>Reads <c>Path</c> (PDF 2.0): arrays of 2 numbers (a point) or 6 (a Bézier curve's two control points and end point).</summary>
    private protected IReadOnlyList<IReadOnlyList<double>>? ReadPath()
    {
        if (ReadArray(AnnotationNames.Path) is not { } outer)
        {
            return null;
        }

        var segments = new List<IReadOnlyList<double>>(outer.Count);
        bool valid = true;
        foreach (CosObject element in outer)
        {
            List<double>? numbers = AnnotationValues.ReadNumbers(Document, element, out bool complete);
            bool shape = numbers is not null && complete && (numbers.Count == 2 || (numbers.Count == 6 && segments.Count > 0));
            valid &= shape;
            if (shape)
            {
                segments.Add(numbers!);
            }
        }

        if (!valid)
        {
            ReportInvalid(AnnotationNames.Path, "an array of arrays of 2 or 6 numbers, the first of 2");
        }

        return segments;
    }

    /// <summary>Reads <c>QuadPoints</c>: complete quadrilaterals only; a count not a multiple of 8 or a non-number records a diagnostic.</summary>
    private protected IReadOnlyList<PdfQuadrilateral> ReadQuadPoints(bool required, string table)
    {
        CosObject? value = Get(AnnotationNames.QuadPoints);
        if (value is null)
        {
            if (required)
            {
                ReportMissing(AnnotationNames.QuadPoints, table);
            }

            return [];
        }

        List<double>? numbers = AnnotationValues.ReadNumbers(Document, value, out bool complete);
        if (numbers is null || !complete || numbers.Count % 8 != 0 || numbers.Count == 0)
        {
            Report(DiagnosticCodes.QuadPointsInvalid, "The annotation's QuadPoints entry is not an array of 8n numbers; only its complete quadrilaterals are read.");
            if (numbers is null)
            {
                return [];
            }
        }

        var quads = new PdfQuadrilateral[numbers.Count / 8];
        for (int index = 0; index < quads.Length; index++)
        {
            int at = index * 8;
            quads[index] = new PdfQuadrilateral(
                new PathPoint(numbers[at], numbers[at + 1]),
                new PathPoint(numbers[at + 2], numbers[at + 3]),
                new PathPoint(numbers[at + 4], numbers[at + 5]),
                new PathPoint(numbers[at + 6], numbers[at + 7]));
        }

        return quads;
    }

    /// <summary>Reads <c>RD</c>: four non-negative numbers.</summary>
    private protected PdfRectangleDifferences? ReadRectangleDifferences()
    {
        CosObject? value = Get(AnnotationNames.RD);
        if (value is null)
        {
            return null;
        }

        Span<double> numbers = stackalloc double[4];
        if (value is CosArray { Count: 4 } array && AnnotationValues.TryReadNumbers(Document, array, numbers)
            && numbers[0] >= 0 && numbers[1] >= 0 && numbers[2] >= 0 && numbers[3] >= 0)
        {
            return new PdfRectangleDifferences(numbers[0], numbers[1], numbers[2], numbers[3]);
        }

        ReportInvalid(AnnotationNames.RD, "an array of four non-negative numbers");
        return null;
    }

    /// <summary>Reads a line ending name (Table 179); unknown names read as None.</summary>
    private protected static PdfLineEnding ParseLineEnding(CosObject? value) => value is CosName name
        ? name.Value switch
        {
            "Square" => PdfLineEnding.Square,
            "Circle" => PdfLineEnding.Circle,
            "Diamond" => PdfLineEnding.Diamond,
            "OpenArrow" => PdfLineEnding.OpenArrow,
            "ClosedArrow" => PdfLineEnding.ClosedArrow,
            "Butt" => PdfLineEnding.Butt,
            "ROpenArrow" => PdfLineEnding.ROpenArrow,
            "RClosedArrow" => PdfLineEnding.RClosedArrow,
            "Slash" => PdfLineEnding.Slash,
            _ => PdfLineEnding.None,
        }
        : PdfLineEnding.None;

    /// <summary>Reads <c>LE</c> as two line endings, start and end (line and polyline annotations).</summary>
    private protected (PdfLineEnding Start, PdfLineEnding End) ReadLineEndings()
    {
        switch (Get(AnnotationNames.LE))
        {
            case null:
                return (PdfLineEnding.None, PdfLineEnding.None);
            case CosArray { Count: 2 } array:
                return (ParseLineEnding(Document.Resolve(array[0])), ParseLineEnding(Document.Resolve(array[1])));
            default:
                ReportInvalid(AnnotationNames.LE, "an array of two names");
                return (PdfLineEnding.None, PdfLineEnding.None);
        }
    }

    /// <summary>Reads the border effect dictionary <c>BE</c> (Table 169).</summary>
    private protected PdfBorderEffect? ReadBorderEffect()
    {
        if (ReadDictionary(AnnotationNames.BE) is not { } effect)
        {
            return null;
        }

        var view = new DictionaryView(Document, effect, DiagnosticReference);
        PdfBorderEffectStyle style = view.Get(AnnotationNames.S) is CosName { Value: "C" } ? PdfBorderEffectStyle.Cloudy : PdfBorderEffectStyle.None;
        double intensity = AnnotationValues.ReadNumber(Document, view.Get(AnnotationNames.I)) ?? 0;
        if (intensity is < 0 or > 2)
        {
            Report(DiagnosticCodes.AnnotationValueInvalid, "The border effect's intensity I shall be from 0 to 2; it is clamped.");
            intensity = Math.Clamp(intensity, 0, 2);
        }

        return new PdfBorderEffect(style, intensity);
    }

    /// <summary>Reads the icon name entry <c>Name</c>, with the subtype's default.</summary>
    private protected string ReadIconName(string fallback) => ReadName(AnnotationNames.Name)?.Value ?? fallback;

    private static void Report(PdfDocument document, string code, string message, CosReference? reference) =>
        document.DiagnosticSink.Report(code, DiagnosticSeverity.Warning, message, objectReference: reference);

    private PdfAnnotation WithCreatedSubtype(CosName? subtype)
    {
        CreatedSubtype = subtype;
        return this;
    }

    private PdfFormXObject? Select(PdfAppearanceDictionary appearance, PdfAppearanceMode mode, CosName state)
    {
        if (appearance.GetEntry(mode) is not { } entry)
        {
            return null;
        }

        if (!entry.HasStates)
        {
            return Checked(entry.Form);
        }

        if (entry.GetAppearance(state) is { } form)
        {
            return Checked(form);
        }

        // A rollover or down subdictionary without the state shows the normal appearance of that state.
        return mode != PdfAppearanceMode.Normal && appearance.Normal is { HasStates: true } normal ? Checked(normal.GetAppearance(state)) : null;
    }

    private PdfFormXObject? Checked(PdfFormXObject? form)
    {
        if (form is null)
        {
            return null;
        }

        if (!form.IsForm)
        {
            Report(DiagnosticCodes.AppearanceNotForm, "An appearance stream's Subtype is not Form (§8.10.1, Table 93); it is read as a form XObject.");
        }

        if (form.BoundingBox is null)
        {
            Report(DiagnosticCodes.AppearanceBBoxMissing, "An appearance stream has no BBox of four numbers, which Table 93 requires; the annotation rectangle's size is used.");
        }

        return form;
    }

    private PdfAnnotationBorder ReadBorder()
    {
        if (Get(AnnotationNames.BS) is { } styleValue)
        {
            if (styleValue is CosDictionary style)
            {
                return ReadBorderStyle(style);
            }

            ReportInvalid(AnnotationNames.BS, "a border style dictionary");
        }

        CosObject? border = Get(AnnotationNames.Border);
        if (border is null)
        {
            return new PdfAnnotationBorder(1, PdfBorderStyle.Solid, [], 0, 0, PdfBorderSource.Default);
        }

        if (border is not CosArray array)
        {
            Report(DiagnosticCodes.BorderArrayInvalid, "The annotation's Border entry is not an array; the default border is used.");
            return new PdfAnnotationBorder(1, PdfBorderStyle.Solid, [], 0, 0, PdfBorderSource.Default);
        }

        Span<double> numbers = stackalloc double[3];
        bool valid = array.Count is 3 or 4;
        for (int index = 0; index < 3; index++)
        {
            double? number = index < array.Count ? AnnotationValues.ReadNumber(Document, array[index]) : null;
            valid &= number is not null;
            numbers[index] = number ?? 0;
        }

        double[] dashes = [];
        if (array.Count >= 4)
        {
            List<double>? pattern = AnnotationValues.ReadNumbers(Document, array[3], out bool complete);
            valid &= pattern is not null && complete && pattern.TrueForAll(dash => dash >= 0);
            dashes = pattern is not null && complete && pattern.TrueForAll(dash => dash >= 0) && pattern.Exists(dash => dash > 0) ? [.. pattern] : [];
        }

        if (!valid)
        {
            Report(DiagnosticCodes.BorderArrayInvalid, "The annotation's Border entry is not three numbers and an optional dash array; missing elements read as 0.");
        }

        return new PdfAnnotationBorder(numbers[2], dashes.Length > 0 ? PdfBorderStyle.Dashed : PdfBorderStyle.Solid, dashes, numbers[0], numbers[1], PdfBorderSource.BorderArray);
    }

    private PdfAnnotationBorder ReadBorderStyle(CosDictionary style)
    {
        var view = new DictionaryView(Document, style, DiagnosticReference);
        if (view.Get(AnnotationNames.Type) is { } type && !(type is CosName { Value: "Border" }))
        {
            Report(DiagnosticCodes.BorderStyleTypeInvalid, "The border style dictionary's Type, if present, shall be Border; the dictionary is used anyway.");
        }

        double width = 1;
        if (view.Get(AnnotationNames.W) is { } widthValue)
        {
            if (AnnotationValues.ReadNumber(Document, widthValue) is { } number && number >= 0)
            {
                width = number;
            }
            else
            {
                Report(DiagnosticCodes.AnnotationValueInvalid, "The border style's width W is not a non-negative number; 1 is used.");
            }
        }

        PdfBorderStyle kind = view.Get(AnnotationNames.S) is CosName name
            ? name.Value switch
            {
                "D" => PdfBorderStyle.Dashed,
                "B" => PdfBorderStyle.Beveled,
                "I" => PdfBorderStyle.Inset,
                "U" => PdfBorderStyle.Underline,
                _ => PdfBorderStyle.Solid,
            }
            : PdfBorderStyle.Solid;

        double[] dashes = [];
        if (kind == PdfBorderStyle.Dashed)
        {
            dashes = [3];
            if (view.Get(AnnotationNames.D) is { } dashValue)
            {
                List<double>? pattern = AnnotationValues.ReadNumbers(Document, dashValue, out bool complete);
                if (pattern is not null && complete && pattern.Count > 0 && pattern.TrueForAll(dash => dash >= 0) && pattern.Exists(dash => dash > 0))
                {
                    dashes = [.. pattern];
                }
                else
                {
                    Report(DiagnosticCodes.AnnotationValueInvalid, "The border style's dash array D is not a dash pattern; [3] is used.");
                }
            }
        }

        return new PdfAnnotationBorder(width, kind, dashes, 0, 0, PdfBorderSource.BorderStyle);
    }
}
