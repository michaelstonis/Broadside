using Broadside.Rendering;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>A render surface that keeps what it is given as a PNG data URL (thumbnails, previews): the <see cref="Bitmap"/> is only lent.</summary>
public sealed class PngSurface : IRenderSurface
{
    /// <summary>Gets the last presented tile as a <c>data:image/png</c> URL, or null.</summary>
    public string? Image { get; private set; }

    /// <inheritdoc/>
    public void Present(Tile tile, Bitmap pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        using var stream = new MemoryStream();
        pixels.SavePng(stream);
        Image = "data:image/png;base64," + Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length);
    }
}
