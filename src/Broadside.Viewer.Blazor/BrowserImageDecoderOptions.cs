namespace Broadside.Viewer.Blazor;

/// <summary>Which of the browser's decoders a <see cref="BrowserImageDecoder"/> uses, each detected at run time.</summary>
/// <remarks>ISO 32000-2 §7.4.8. The defaults follow the measured costs: see <see cref="BrowserImageDecoder"/>.</remarks>
public sealed class BrowserImageDecoderOptions
{
    /// <summary>Gets or sets a value indicating whether WebCodecs <c>ImageDecoder</c> is used where the browser has it. The default is <see langword="true"/>.</summary>
    /// <remarks>Chrome and Edge 94+, Firefox 133+, on the page and in workers; bit-identical to the managed decoder at full size.</remarks>
    public bool UseWebCodecs { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether <c>createImageBitmap</c> with a read back is used in the render worker of a browser
    /// without <c>ImageDecoder</c>. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>Safari: the read back's cost stays off the page's thread; within the tolerance of the managed decoder.</remarks>
    public bool UseImageBitmapInWorker { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether <c>createImageBitmap</c> with a read back is used on the page of a browser without
    /// <c>ImageDecoder</c>. The default is <see langword="false"/>: WebKit's read back takes 60 to 141 ms on the page's thread, so
    /// Safari's page decodes with the managed codec and its scaled decode.
    /// </summary>
    public bool UseImageBitmapOnPage { get; set; }

    /// <summary>Sets <see cref="UseWebCodecs"/>.</summary>
    /// <param name="enabled">Whether to use WebCodecs.</param>
    /// <returns>These options.</returns>
    public BrowserImageDecoderOptions WithWebCodecs(bool enabled)
    {
        UseWebCodecs = enabled;
        return this;
    }

    /// <summary>Sets <see cref="UseImageBitmapInWorker"/>.</summary>
    /// <param name="enabled">Whether to use <c>createImageBitmap</c> in the render worker.</param>
    /// <returns>These options.</returns>
    public BrowserImageDecoderOptions WithImageBitmapInWorker(bool enabled)
    {
        UseImageBitmapInWorker = enabled;
        return this;
    }

    /// <summary>Sets <see cref="UseImageBitmapOnPage"/>.</summary>
    /// <param name="enabled">Whether to use <c>createImageBitmap</c> on the page.</param>
    /// <returns>These options.</returns>
    public BrowserImageDecoderOptions WithImageBitmapOnPage(bool enabled)
    {
        UseImageBitmapOnPage = enabled;
        return this;
    }
}
