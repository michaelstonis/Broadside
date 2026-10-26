using System.Runtime.Versioning;

namespace Broadside.Viewer.Blazor;

/// <summary>Registers the browser's own image decoder with an engine's options.</summary>
[SupportedOSPlatform("browser")]
public static class BrowserImageDecoderExtensions
{
    /// <summary>
    /// Has the engine decode JPEG images with the browser's own decoder before they are drawn (<see cref="BrowserImageDecoder"/>):
    /// <see cref="BrowserImageDecoder.Shared"/>, or a decoder with <paramref name="decoderOptions"/>. <see cref="PdfViewer"/> and
    /// <see cref="RenderWorker"/> do this for the engines they create; an app that gives them its own engine calls this.
    /// </summary>
    /// <param name="options">The engine's options.</param>
    /// <param name="decoderOptions">Which browser decoders to use; <see langword="null"/> for the defaults.</param>
    /// <returns><paramref name="options"/>.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.4.8. Await <see cref="BrowserImageDecoder.InitializeAsync"/> before the first document is drawn; until then images
    /// decode with the managed codec.
    /// </remarks>
    public static PdfOptions UseBrowserImageDecoder(this PdfOptions options, BrowserImageDecoderOptions? decoderOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UsePlatformImageDecoder(decoderOptions is null ? BrowserImageDecoder.Shared : new BrowserImageDecoder(decoderOptions));
    }
}
