namespace Broadside.Viewer.WinUI;

/// <summary>A tile's pixels in a form the host can show: a <c>WriteableBitmap</c> on WinUI, managed pixels in tests.</summary>
internal interface ITileImage
{
    /// <summary>Gets the width in pixels.</summary>
    int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    int Height { get; }
}

/// <summary>Makes tile images on the UI thread from the premultiplied BGRA pixels a worker copied.</summary>
internal interface ITileImageFactory
{
    /// <summary>Makes an image from rows of premultiplied BGRA pixels.</summary>
    ITileImage Create(int width, int height, ReadOnlySpan<byte> bgra, int stride);
}
