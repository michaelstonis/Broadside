namespace Broadside;

/// <summary>
/// Options for the imposition operations, <see cref="PdfDocument.CreateNUp"/>, <see cref="PdfDocument.CreateBooklet"/> and
/// <see cref="PdfDocument.CreateTiles"/>: the sheet, its margins, the order of the cells and what of each source page is drawn.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.10.1 and §14.11.2: each source page is drawn as a form XObject of its <see cref="Boundary"/>
/// (<see cref="PdfDocument.CreateFormXObject(PdfPage, PdfPageFormOptions)"/>). Lengths are in points (default user space units).
/// </remarks>
public sealed class PdfImpositionOptions
{
    private PdfRectangle? _sheetSize;
    private double _margin;
    private double _gutter;
    private double _overlap;
    private PdfImpositionOrder _order;
    private PdfPageBoundary _boundary = PdfPageBoundary.CropBox;

    /// <summary>
    /// Gets or sets the media box of each output sheet; <see langword="null"/> (the default) derives it from the first source page as
    /// it is displayed: for n-up that page's size, turned to landscape when there are more columns than rows and to portrait when
    /// there are more rows than columns; for a booklet two such pages side by side; for tiles that page's size.
    /// </summary>
    /// <exception cref="ArgumentException">On set, a coordinate is not finite or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §14.11.2 (<c>MediaBox</c>).</remarks>
    public PdfRectangle? SheetSize
    {
        get => _sheetSize;
        set
        {
            if (value is { } box && (!double.IsFinite(box.Left) || !double.IsFinite(box.Bottom) || !double.IsFinite(box.Right) || !double.IsFinite(box.Top)
                || box.Width <= 0 || box.Height <= 0))
            {
                throw new ArgumentException("A sheet shall have finite coordinates and an area (ISO 32000-2 §14.11.2).", nameof(value));
            }

            _sheetSize = value;
        }
    }

    /// <summary>Gets or sets the blank space kept at every edge of a sheet, in points. The default is 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">On set, the value is negative or not finite.</exception>
    public double Margin
    {
        get => _margin;
        set => _margin = Length(value);
    }

    /// <summary>Gets or sets the blank space between neighboring cells of a sheet, in points. The default is 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">On set, the value is negative or not finite.</exception>
    public double Gutter
    {
        get => _gutter;
        set => _gutter = Length(value);
    }

    /// <summary>Gets or sets, for <see cref="PdfDocument.CreateTiles"/> only, how much of the source page neighboring tiles share, in points of the source page. The default is 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">On set, the value is negative or not finite.</exception>
    public double Overlap
    {
        get => _overlap;
        set => _overlap = Length(value);
    }

    /// <summary>Gets or sets the order in which the cells of a sheet are filled. The default is <see cref="PdfImpositionOrder.LeftToRightThenDown"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">On set, the value is not a defined order.</exception>
    public PdfImpositionOrder Order
    {
        get => _order;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not an imposition order.");
            }

            _order = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether a thin black frame is stroked around each filled cell. The default is <see langword="false"/>.</summary>
    public bool DrawCellFrames { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the normal appearances of each source page's visible, printable annotations are drawn
    /// into the sheet. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.5.5 and §12.5.3 (<c>Print</c> set, <c>Hidden</c> clear). The annotations themselves are not carried.</remarks>
    public bool IncludeAnnotations { get; set; } = true;

    /// <summary>Gets or sets the page boundary of each source page that is placed. The default is <see cref="PdfPageBoundary.CropBox"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">On set, the value is not a defined boundary.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfPageBoundary Boundary
    {
        get => _boundary;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a page boundary.");
            }

            _boundary = value;
        }
    }

    /// <summary>Sets <see cref="SheetSize"/>.</summary>
    /// <param name="sheetSize">The media box of each sheet; <see langword="null"/> to derive it.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException">A coordinate is not finite or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfImpositionOptions WithSheetSize(PdfRectangle? sheetSize)
    {
        SheetSize = sheetSize;
        return this;
    }

    /// <summary>Sets <see cref="Margin"/>.</summary>
    /// <param name="margin">The space at every edge, in points.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="margin"/> is negative or not finite.</exception>
    public PdfImpositionOptions WithMargin(double margin)
    {
        Margin = margin;
        return this;
    }

    /// <summary>Sets <see cref="Gutter"/>.</summary>
    /// <param name="gutter">The space between cells, in points.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gutter"/> is negative or not finite.</exception>
    public PdfImpositionOptions WithGutter(double gutter)
    {
        Gutter = gutter;
        return this;
    }

    /// <summary>Sets <see cref="Overlap"/>.</summary>
    /// <param name="overlap">The width neighboring tiles share, in points of the source page.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="overlap"/> is negative or not finite.</exception>
    public PdfImpositionOptions WithOverlap(double overlap)
    {
        Overlap = overlap;
        return this;
    }

    /// <summary>Sets <see cref="Order"/>.</summary>
    /// <param name="order">The order of the cells.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="order"/> is not a defined order.</exception>
    public PdfImpositionOptions WithOrder(PdfImpositionOrder order)
    {
        Order = order;
        return this;
    }

    /// <summary>Sets <see cref="DrawCellFrames"/>.</summary>
    /// <param name="draw">Whether a frame is drawn around each cell.</param>
    /// <returns>These options.</returns>
    public PdfImpositionOptions WithCellFrames(bool draw = true)
    {
        DrawCellFrames = draw;
        return this;
    }

    /// <summary>Sets <see cref="IncludeAnnotations"/>.</summary>
    /// <param name="include">Whether annotation appearances are drawn in.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.5.5.</remarks>
    public PdfImpositionOptions WithAnnotations(bool include = true)
    {
        IncludeAnnotations = include;
        return this;
    }

    /// <summary>Sets <see cref="Boundary"/>.</summary>
    /// <param name="boundary">The page boundary placed.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="boundary"/> is not a defined boundary.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfImpositionOptions WithBoundary(PdfPageBoundary boundary)
    {
        Boundary = boundary;
        return this;
    }

    private static double Length(double value) =>
        double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A length is a finite number of points, 0 or more.");
}
