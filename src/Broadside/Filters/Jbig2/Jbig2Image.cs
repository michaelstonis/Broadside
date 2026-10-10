namespace Broadside.Filters.Jbig2;

/// <summary>
/// A JBIG2 bitmap that outlives one procedure: a symbol, a halftone pattern, or an intermediate region's auxiliary buffer (ITU-T T.88
/// §8.2 step 5 b). Packed rows, 1 = black, padding bits 0. Symbols and patterns are never written after their dictionary is decoded,
/// so dictionaries of a cached <c>JBIG2Globals</c> stream are shared across threads.
/// </summary>
internal sealed class Jbig2Image
{
    /// <summary>The empty bitmap (a symbol of width or height 0).</summary>
    public static readonly Jbig2Image Empty = new(0, 0);

    /// <summary>Initializes a new instance of the <see cref="Jbig2Image"/> class, all 0.</summary>
    /// <param name="width">The pixels per row.</param>
    /// <param name="height">The rows.</param>
    public Jbig2Image(int width, int height)
    {
        Width = width;
        Height = height;
        Stride = Jbig2Bitmap.StrideOf(width);
        Data = width == 0 || height == 0 ? [] : new byte[Stride * height];
    }

    /// <summary>Gets the pixels per row.</summary>
    public int Width { get; }

    /// <summary>Gets the rows.</summary>
    public int Height { get; }

    /// <summary>Gets the bytes per row.</summary>
    public int Stride { get; }

    /// <summary>Gets the packed rows.</summary>
    public byte[] Data { get; }

    /// <summary>Gets a view of the bitmap.</summary>
    public Jbig2Bitmap View => Width == 0 || Height == 0 ? default : new Jbig2Bitmap(Data, Width, Height, Stride);

    /// <summary>Copies the columns <paramref name="x"/> to <paramref name="x"/> + <paramref name="width"/> - 1 of <paramref name="source"/>.</summary>
    /// <param name="source">The source bitmap (a collective bitmap).</param>
    /// <param name="x">The first column.</param>
    /// <param name="width">The number of columns.</param>
    /// <returns>A new bitmap of <paramref name="source"/>'s height.</returns>
    public static Jbig2Image CopyColumns(Jbig2Bitmap source, int x, int width)
    {
        var image = new Jbig2Image(Math.Max(0, width), source.Height);
        image.View.Compose(source, -(long)x, 0, Jbig2CombinationOperator.Replace);
        return image;
    }
}
