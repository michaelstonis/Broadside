using Broadside.Fonts;

namespace Broadside.Graphics;

/// <summary>
/// How <see cref="PdfDocument.CreateFormXObjectFromSvg(ReadOnlySpan{byte}, SvgImportOptions?)"/> imports SVG: the size of the form,
/// the font used when the SVG names none that resolves, and the resolver for images the SVG refers to by URL or file name.
/// </summary>
/// <remarks>
/// <para>
/// Immutable: each <c>With*</c> method returns a changed copy, so one instance can be shared across threads and documents. The defaults
/// size the form from the SVG (1 CSS pixel is 0.75 point), resolve fonts through the document's font resolvers, and read no resource
/// outside the SVG bytes (<c>data:</c> URIs inside them are always read).
/// </para>
/// <para>ISO 32000-2 §8.10.1 (form XObjects) and SVG 1.1 §7.2 (the initial viewport).</para>
/// </remarks>
public sealed class SvgImportOptions
{
    /// <summary>Initializes a new instance of the <see cref="SvgImportOptions"/> class with the defaults.</summary>
    public SvgImportOptions()
    {
    }

    private SvgImportOptions(SvgImportOptions other)
    {
        Width = other.Width;
        Height = other.Height;
        DefaultFont = other.DefaultFont;
        ResourceResolver = other.ResourceResolver;
    }

    /// <summary>Gets the width of the form in points; <see langword="null"/>, the default, takes it from the SVG.</summary>
    /// <remarks>SVG 1.1 §7.2: the root element's width, height and view box set the viewport; this size replaces it.</remarks>
    public double? Width { get; private init; }

    /// <summary>Gets the height of the form in points; <see langword="null"/>, the default, takes it from the SVG.</summary>
    public double? Height { get; private init; }

    /// <summary>Gets the font used for text whose families none resolves; <see langword="null"/>, the default, uses Helvetica.</summary>
    /// <remarks>ISO 32000-2 §9.6.2. Resolved through the document's font resolvers, as <see cref="PdfDocument.EmbedFont(FontQuery)"/> does.</remarks>
    public FontQuery? DefaultFont { get; private init; }

    /// <summary>
    /// Gets the resolver for <c>href</c> values of <c>image</c> elements that are not <c>data:</c> URIs; <see langword="null"/>, the
    /// default, draws no such image.
    /// </summary>
    /// <remarks>
    /// The importer never opens a file or a URL itself: the resolver receives the <c>href</c> as written and returns the bytes (PNG,
    /// JPEG or SVG), or <see langword="null"/> when it will not supply them.
    /// </remarks>
    public Func<string, ReadOnlyMemory<byte>?>? ResourceResolver { get; private init; }

    /// <summary>Returns a copy that sizes the form to <paramref name="width"/> by <paramref name="height"/> points, the SVG fitted into it by its view box and <c>preserveAspectRatio</c>.</summary>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <returns>The copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive and finite.</exception>
    /// <remarks>SVG 1.1 §7.8 (<c>preserveAspectRatio</c>).</remarks>
    public SvgImportOptions WithSize(double width, double height)
    {
        if (!(width > 0 && double.IsFinite(width)))
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width shall be positive and finite.");
        }

        if (!(height > 0 && double.IsFinite(height)))
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height shall be positive and finite.");
        }

        return new SvgImportOptions(this) { Width = width, Height = height };
    }

    /// <summary>Returns a copy whose text falls back to the font <paramref name="query"/> finds.</summary>
    /// <param name="query">The font to look for.</param>
    /// <returns>The copy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <remarks>ISO 32000-2 §9.6.2.</remarks>
    public SvgImportOptions WithDefaultFont(FontQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new SvgImportOptions(this) { DefaultFont = query };
    }

    /// <summary>Returns a copy that asks <paramref name="resolver"/> for the bytes of images referred to by URL or file name.</summary>
    /// <param name="resolver">Receives the <c>href</c> as written; returns the image bytes or <see langword="null"/>.</param>
    /// <returns>The copy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> is <see langword="null"/>.</exception>
    /// <remarks>SVG 1.1 §5.7 (the <c>image</c> element).</remarks>
    public SvgImportOptions WithResourceResolver(Func<string, ReadOnlyMemory<byte>?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return new SvgImportOptions(this) { ResourceResolver = resolver };
    }
}
