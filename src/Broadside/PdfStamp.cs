using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Structure;

namespace Broadside;

/// <summary>
/// Text, an image, a form XObject or another page to draw on existing pages with <see cref="PdfDocument.Stamp(PdfStamp, Range?)"/>:
/// a watermark, a logo, "Page 3 of 12" or a letterhead, aligned in the page's box as a viewer shows it and marked as an artifact.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.2.2 (artifacts, Table 363), §8.11.3 (optional content), §12.4.2 (page labels), §14.11.2 (page boundaries) and
/// §8.10.1 (form XObjects). A stamp is a description: it changes nothing until it is stamped, and one stamp may be stamped on several
/// documents' pages as long as its font, image or form belongs to the document it is stamped on.
/// </para>
/// <para>
/// Placement: the stamp's content box (text lines, the image at one point per sample, the form's transformed bounding box, the
/// page's displayed box) is scaled by <see cref="Scale"/> (or to fit, <see cref="FitToPage"/>), turned by <see cref="Rotation"/>, and
/// the box around the result is aligned in <see cref="Boundary"/>: <see cref="PdfStampAlignment.Start"/> is the left or top edge,
/// <see cref="PdfStampAlignment.End"/> the right or bottom edge, both as the page is viewed when <see cref="FollowPageRotation"/>.
/// <see cref="OffsetX"/> and <see cref="OffsetY"/> then move it right and up, in points.
/// </para>
/// </remarks>
public sealed class PdfStamp
{
    private PdfStampLayer _layer;
    private PdfStampAlignment _horizontal = PdfStampAlignment.Center;
    private PdfStampAlignment _vertical = PdfStampAlignment.Center;
    private double _offsetX;
    private double _offsetY;
    private double _rotation;
    private double _scale = 1;
    private double _opacity = 1;
    private PdfColor _textColor = PdfColor.FromGray(0);
    private PdfPageBoundary _boundary = PdfPageBoundary.CropBox;
    private PdfArtifactType _artifactType = PdfArtifactType.Pagination;
    private string? _artifactSubtype = "Watermark";

    private PdfStamp(PdfStampKind kind)
    {
        Kind = kind;
    }

    /// <summary>Gets or sets whether the stamp is drawn over the page's content (the default) or under it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined layer.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.8.2 and §7.7.3.3 (<c>Contents</c>). A background stamp is a content stream put first in <c>Contents</c>: a page
    /// that paints an opaque white rectangle over its whole box hides it. Use <see cref="PdfStampLayer.Foreground"/> with an
    /// <see cref="Opacity"/> below 1 for such pages.
    /// </remarks>
    public PdfStampLayer Layer
    {
        get => _layer;
        set => _layer = Defined(value);
    }

    /// <summary>Gets or sets where the stamp goes across the box: the left edge, the center (the default) or the right edge.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined alignment.</exception>
    /// <remarks>ISO 32000-2 §14.11.2. For a text stamp it also aligns the lines with each other.</remarks>
    public PdfStampAlignment HorizontalAlignment
    {
        get => _horizontal;
        set => _horizontal = Defined(value);
    }

    /// <summary>Gets or sets where the stamp goes down the box: the top edge, the center (the default) or the bottom edge.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined alignment.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfStampAlignment VerticalAlignment
    {
        get => _vertical;
        set => _vertical = Defined(value);
    }

    /// <summary>Gets or sets how far the aligned stamp moves to the right, in points; negative moves it left.</summary>
    /// <exception cref="ArgumentException">The value is not finite.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3 (default user space), in the page's orientation as viewed when <see cref="FollowPageRotation"/>.</remarks>
    public double OffsetX
    {
        get => _offsetX;
        set => _offsetX = Finite(value);
    }

    /// <summary>Gets or sets how far the aligned stamp moves up, in points; negative moves it down.</summary>
    /// <exception cref="ArgumentException">The value is not finite.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, in the page's orientation as viewed when <see cref="FollowPageRotation"/>.</remarks>
    public double OffsetY
    {
        get => _offsetY;
        set => _offsetY = Finite(value);
    }

    /// <summary>Gets or sets the angle the stamp is turned by, in degrees counterclockwise, about its own center.</summary>
    /// <exception cref="ArgumentException">The value is not finite.</exception>
    /// <remarks>ISO 32000-2 §8.3.3 (rotation in a transformation matrix).</remarks>
    public double Rotation
    {
        get => _rotation;
        set => _rotation = Finite(value);
    }

    /// <summary>Gets or sets the factor the stamp's content box is scaled by; 1 by default. Ignored with <see cref="FitToPage"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    /// <remarks>ISO 32000-2 §8.3.3 (scaling in a transformation matrix).</remarks>
    public double Scale
    {
        get => _scale;
        set => _scale = Positive(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the stamp is scaled, keeping its aspect ratio, so the box around it after
    /// <see cref="Rotation"/> fills the page's box in one direction. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.11.2. A page whose box has no area is skipped.</remarks>
    public bool FitToPage { get; set; }

    /// <summary>Gets or sets the stamp's constant opacity, from 0 to 1; 1 (opaque) by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    /// <remarks>ISO 32000-2 §11.6.4.4 (<c>CA</c> and <c>ca</c>, PDF 1.4). Below 1 records PDF 1.4.</remarks>
    public double Opacity
    {
        get => _opacity;
        set => _opacity = double.IsFinite(value) && value is >= 0 and <= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The opacity shall be between 0 and 1.");
    }

    /// <summary>Gets or sets the colour a text stamp is filled with; black by default.</summary>
    /// <remarks>ISO 32000-2 §8.6 and §9.3.6 (fill for text rendering mode 0). Its space shall belong to the stamped document or be a device space.</remarks>
    public PdfColor TextColor
    {
        get => _textColor;
        set => _textColor = value;
    }

    /// <summary>Gets or sets the page boundary the stamp is aligned in. The default is <see cref="PdfPageBoundary.CropBox"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined boundary.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfPageBoundary Boundary
    {
        get => _boundary;
        set => _boundary = Defined(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether alignment, offsets and rotation are in the page's orientation as viewed (its
    /// <c>Rotate</c> undone), so the stamp is upright on screen. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31 (<c>Rotate</c>).</remarks>
    public bool FollowPageRotation { get; set; } = true;

    /// <summary>Gets or sets the optional content group the stamp belongs to, so a viewer can hide it; <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §8.11.3.2 (PDF 1.5): the stamp is also wrapped in <c>/OC /name BDC</c>. The group shall belong to the stamped document.</remarks>
    public PdfOptionalContentGroup? OptionalContentGroup { get; set; }

    /// <summary>Gets or sets the type of the artifact the stamp is marked as. The default is <see cref="PdfArtifactType.Pagination"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined type.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363.</remarks>
    public PdfArtifactType ArtifactType
    {
        get => _artifactType;
        set => _artifactType = Defined(value);
    }

    /// <summary>Gets or sets the artifact's subtype; <c>Watermark</c> by default, <see langword="null"/> for none.</summary>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363 (PDF 1.7; <c>PageNum</c>, <c>LineNum</c>, <c>Redaction</c> and <c>Bates</c> PDF 2.0).</remarks>
    public string? ArtifactSubtype
    {
        get => _artifactSubtype;
        set => _artifactSubtype = value is { Length: 0 } ? throw new ArgumentException("An artifact subtype shall not be empty.", nameof(value)) : value;
    }

    /// <summary>Gets or sets the number the first page of the stamped range has for <c>{page}</c>; 1 by default.</summary>
    /// <remarks>ISO 32000-2 §12.4.2.</remarks>
    public int FirstPageNumber { get; set; } = 1;

    /// <summary>Gets the kind of content the stamp draws.</summary>
    internal PdfStampKind Kind { get; }

    /// <summary>Gets the text of a text stamp, with its tokens.</summary>
    internal string? Text { get; private init; }

    /// <summary>Gets the font of a text stamp.</summary>
    internal PdfCanvasFont? Font { get; private init; }

    /// <summary>Gets the font size of a text stamp.</summary>
    internal double FontSize { get; private init; }

    /// <summary>Gets the image of an image stamp.</summary>
    internal PdfImage? Image { get; private init; }

    /// <summary>Gets the form of a form stamp.</summary>
    internal PdfFormXObject? Form { get; private init; }

    /// <summary>Gets the page of a page stamp.</summary>
    internal PdfPage? SourcePage { get; private init; }

    /// <summary>Creates a text stamp. The text may hold the tokens <c>{page}</c>, <c>{pages}</c> and <c>{label}</c>, and line feeds.</summary>
    /// <param name="text">The text: <c>{page}</c> is the page's number counted from <see cref="FirstPageNumber"/> in the stamped range, <c>{pages}</c> the number of pages in the range, <c>{label}</c> the page's label (or its number when the document has none); <c>\n</c> starts a line.</param>
    /// <param name="font">A font of the document the stamp is stamped on (<see cref="PdfDocument.GetStandard14Font"/>, an embedded font).</param>
    /// <param name="fontSize">The font size, in points.</param>
    /// <returns>The stamp.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not a positive finite number.</exception>
    /// <remarks>ISO 32000-2 §9.2 to §9.4 and §12.4.2. Text that needs right-to-left or Arabic shaping is shaped (<see cref="PdfCanvasFont.Shape"/>).</remarks>
    public static PdfStamp FromText(string text, PdfCanvasFont font, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        return new PdfStamp(PdfStampKind.Text) { Text = text, Font = font, FontSize = Positive(fontSize, nameof(fontSize)) };
    }

    /// <summary>Creates an image stamp: the image at one point per sample before <see cref="Scale"/>.</summary>
    /// <param name="image">An image XObject of the document the stamp is stamped on.</param>
    /// <returns>The stamp.</returns>
    /// <remarks>ISO 32000-2 §8.9.5. One image XObject is drawn on every page.</remarks>
    public static PdfStamp FromImage(PdfImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new PdfStamp(PdfStampKind.Image) { Image = image };
    }

    /// <summary>Creates a form stamp: the form's bounding box, through its matrix, before <see cref="Scale"/>.</summary>
    /// <param name="form">A form XObject of the document the stamp is stamped on.</param>
    /// <returns>The stamp.</returns>
    /// <remarks>ISO 32000-2 §8.10.1. One form XObject is drawn on every page.</remarks>
    public static PdfStamp FromForm(PdfFormXObject form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new PdfStamp(PdfStampKind.Form) { Form = form };
    }

    /// <summary>Creates a page stamp: <paramref name="page"/> made into a form XObject (<see cref="PdfDocument.CreateFormXObject(PdfPage)"/>) of the stamped document, its displayed crop box before <see cref="Scale"/>.</summary>
    /// <param name="page">A page of the stamped document or of any other open document.</param>
    /// <returns>The stamp.</returns>
    /// <remarks>ISO 32000-2 §8.10.1. The form is made once per stamped document and drawn on every page.</remarks>
    public static PdfStamp FromPage(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new PdfStamp(PdfStampKind.Page) { SourcePage = page };
    }

    /// <summary>Sets <see cref="Layer"/>.</summary>
    /// <param name="layer">The layer.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §7.8.2.</remarks>
    public PdfStamp WithLayer(PdfStampLayer layer)
    {
        Layer = layer;
        return this;
    }

    /// <summary>Sets <see cref="HorizontalAlignment"/>.</summary>
    /// <param name="alignment">The alignment.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfStamp WithHorizontalAlignment(PdfStampAlignment alignment)
    {
        HorizontalAlignment = alignment;
        return this;
    }

    /// <summary>Sets <see cref="VerticalAlignment"/>.</summary>
    /// <param name="alignment">The alignment.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfStamp WithVerticalAlignment(PdfStampAlignment alignment)
    {
        VerticalAlignment = alignment;
        return this;
    }

    /// <summary>Sets <see cref="OffsetX"/>.</summary>
    /// <param name="offset">The offset, in points.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §8.3.2.3.</remarks>
    public PdfStamp WithOffsetX(double offset)
    {
        OffsetX = offset;
        return this;
    }

    /// <summary>Sets <see cref="OffsetY"/>.</summary>
    /// <param name="offset">The offset, in points.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §8.3.2.3.</remarks>
    public PdfStamp WithOffsetY(double offset)
    {
        OffsetY = offset;
        return this;
    }

    /// <summary>Sets <see cref="Rotation"/>.</summary>
    /// <param name="degrees">The angle, in degrees counterclockwise.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §8.3.3.</remarks>
    public PdfStamp WithRotation(double degrees)
    {
        Rotation = degrees;
        return this;
    }

    /// <summary>Sets <see cref="Scale"/>.</summary>
    /// <param name="scale">The factor.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §8.3.3.</remarks>
    public PdfStamp WithScale(double scale)
    {
        Scale = scale;
        return this;
    }

    /// <summary>Sets <see cref="FitToPage"/>.</summary>
    /// <param name="fit">Whether the stamp is scaled to the box.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfStamp WithFitToPage(bool fit = true)
    {
        FitToPage = fit;
        return this;
    }

    /// <summary>Sets <see cref="Opacity"/>.</summary>
    /// <param name="opacity">The opacity, from 0 to 1.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §11.6.4.4.</remarks>
    public PdfStamp WithOpacity(double opacity)
    {
        Opacity = opacity;
        return this;
    }

    /// <summary>Sets <see cref="TextColor"/>.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §8.6.</remarks>
    public PdfStamp WithTextColor(PdfColor color)
    {
        TextColor = color;
        return this;
    }

    /// <summary>Sets <see cref="Boundary"/>.</summary>
    /// <param name="boundary">The page boundary.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfStamp WithBoundary(PdfPageBoundary boundary)
    {
        Boundary = boundary;
        return this;
    }

    /// <summary>Sets <see cref="FollowPageRotation"/>.</summary>
    /// <param name="follow">Whether the stamp is upright as the page is viewed.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31.</remarks>
    public PdfStamp WithFollowPageRotation(bool follow = true)
    {
        FollowPageRotation = follow;
        return this;
    }

    /// <summary>Sets <see cref="OptionalContentGroup"/>.</summary>
    /// <param name="group">The group, or <see langword="null"/> for none.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §8.11.3.2.</remarks>
    public PdfStamp WithOptionalContentGroup(PdfOptionalContentGroup? group)
    {
        OptionalContentGroup = group;
        return this;
    }

    /// <summary>Sets <see cref="ArtifactType"/>.</summary>
    /// <param name="type">The artifact type.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363.</remarks>
    public PdfStamp WithArtifactType(PdfArtifactType type)
    {
        ArtifactType = type;
        return this;
    }

    /// <summary>Sets <see cref="ArtifactSubtype"/>.</summary>
    /// <param name="subtype">The subtype, or <see langword="null"/> for none.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363.</remarks>
    public PdfStamp WithArtifactSubtype(string? subtype)
    {
        ArtifactSubtype = subtype;
        return this;
    }

    /// <summary>Sets <see cref="FirstPageNumber"/>.</summary>
    /// <param name="number">The number of the range's first page.</param>
    /// <returns>This stamp.</returns>
    /// <remarks>ISO 32000-2 §12.4.2.</remarks>
    public PdfStamp WithFirstPageNumber(int number)
    {
        FirstPageNumber = number;
        return this;
    }

    private static T Defined<T>(T value)
        where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, $"Not a defined {typeof(T).Name}.");

    private static double Finite(double value) =>
        double.IsFinite(value) ? value : throw new ArgumentException("The value shall be a finite number.", nameof(value));

    private static double Positive(double value, string name = "value") =>
        double.IsFinite(value) && value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The value shall be a positive finite number.");
}

/// <summary>What a <see cref="PdfStamp"/> draws.</summary>
internal enum PdfStampKind
{
    Text,
    Image,
    Form,
    Page,
}
