using Broadside.Images;

namespace Broadside.Filters;

/// <summary>An image codestream's header: its size, components and precision as the codestream declares them.</summary>
/// <param name="Width">The number of samples in each row.</param>
/// <param name="Height">The number of rows.</param>
/// <param name="Components">The number of colour components (without an opacity channel).</param>
/// <param name="BitsPerComponent">The precision of the components; the greatest when they differ.</param>
/// <remarks>ISO 32000-2 §7.4.9 and §8.9.2. Returned by <see cref="IImageFilter.TryReadHeader"/>.</remarks>
public readonly record struct ImageHeader(int Width, int Height, int Components, int BitsPerComponent)
{
    /// <summary>Gets a value indicating whether the codestream also has an opacity channel.</summary>
    /// <remarks>ISO 32000-2 §7.4.9 (<c>SMaskInData</c>).</remarks>
    public bool HasAlpha { get; init; }

    /// <summary>Gets the colour model the codestream declares, or <see cref="ImageColorModel.Unknown"/>.</summary>
    /// <remarks>ISO 32000-2 §7.4.9.</remarks>
    public ImageColorModel ColorModel { get; init; }
}
