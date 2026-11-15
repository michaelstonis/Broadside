using Broadside.Rendering;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>A render surface for <see cref="PageThumbnails"/>: turns each whole-page tile into a PNG data URL and hands it, with its page, to a callback.</summary>
public sealed class ThumbnailSurface : IRenderSurface
{
    private readonly Action<int, string> _presented;

    /// <summary>Creates the surface.</summary>
    /// <param name="presented">Called for each thumbnail with its page and its <c>data:image/png</c> URL.</param>
    public ThumbnailSurface(Action<int, string> presented)
    {
        ArgumentNullException.ThrowIfNull(presented);
        _presented = presented;
    }

    /// <inheritdoc/>
    public void Present(Tile tile, Bitmap pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        using var stream = new MemoryStream();
        pixels.SavePng(stream);
        _presented(tile.PageIndex, "data:image/png;base64," + Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length));
    }
}
