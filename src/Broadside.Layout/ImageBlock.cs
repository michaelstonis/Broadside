using Broadside.Images;

namespace Broadside.Layout.Flow;

/// <summary>
/// An image on its own between blocks, aligned left, centred or right in the content area. An image wider than the content area or
/// taller than a page's content area is scaled down, keeping its aspect ratio; one that does not fit below the cursor starts a page.
/// </summary>
/// <remarks>ISO 32000-2 §8.9.5 (image XObjects, painted with <c>Do</c> in a <c>cm</c> that maps the unit square to the placed size) and §14.8.4.8.5 (<c>Figure</c>).</remarks>
public sealed class ImageBlock : FlowBlock
{
    /// <summary>Creates an image block with structure type <c>Figure</c>.</summary>
    /// <param name="image">The image; it shall belong to the document the flow renders into.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive and finite.</exception>
    public ImageBlock(PdfImage image, double width, double height)
        : base("Figure")
    {
        ArgumentNullException.ThrowIfNull(image);
        Image = image;
        Width = Positive(width, nameof(width));
        Height = Positive(height, nameof(height));
    }

    /// <summary>The image.</summary>
    public PdfImage Image { get; }

    /// <summary>The width in points before any scaling down.</summary>
    public double Width { get; }

    /// <summary>The height in points before any scaling down.</summary>
    public double Height { get; }

    /// <summary>The horizontal placement; <see cref="TextAlignment.Justify"/> places it as <see cref="TextAlignment.Left"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined alignment.</exception>
    public TextAlignment Alignment
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown alignment.");
    }

    /// <summary>The space above in points, dropped at the top of a page.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceBefore
    {
        get;
        init => field = NotNegative(value, nameof(value));
    }

    /// <summary>The space below in points.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceAfter
    {
        get;
        init => field = NotNegative(value, nameof(value));
    }

    /// <summary>The alternate description of the image for tagged output (§14.9.3), or null.</summary>
    public string? AlternateText { get; init; }

    internal override void Validate(int index)
    {
    }

    internal override ContentWidths MeasureWidths(FlowServices services) => new(Width, Width);

    internal override void LayOut(FlowContext context)
    {
        context.AddSpace(SpaceBefore);
        double scale = Math.Min(1, Math.Min(context.Width / Width, (context.Top - context.Bottom) / Height));
        double height = Height * scale;
        context.EnsureRoom(height);

        // The page the image lands on decides its width.
        scale = Math.Min(scale, context.Width / Width);
        double width = Width * scale;
        height = Height * scale;
        double x = Alignment switch
        {
            TextAlignment.Right => context.Right - width,
            TextAlignment.Center => context.Left + ((context.Width - width) / 2),
            _ => context.Left,
        };
        context.PlaceMarkersAtTop();
        context.BeginBlock(this);
        if (context.Drawing)
        {
            context.Canvas.DrawImage(Image, x, context.CursorY - height, width, height);
        }

        context.Advance(height);
        context.EndBlock();
        context.AddSpace(SpaceAfter);
    }

    internal static double Positive(double value, string name) =>
        double.IsFinite(value) && value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The value shall be positive and finite.");

    internal static double NotNegative(double value, string name) =>
        double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The value shall be finite and not negative.");
}
