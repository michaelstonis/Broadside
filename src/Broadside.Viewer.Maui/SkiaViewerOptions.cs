using Broadside.Rendering.Skia;

namespace Broadside.Viewer.Maui;

/// <summary>The options <see cref="PdfView"/> opens a document from its <see cref="PdfView.Source"/> with.</summary>
internal static class SkiaViewerOptions
{
    /// <summary>
    /// Returns a copy of <paramref name="options"/> whose tiles draw with Skia unless it names a backend, and whose engine (when it
    /// gives none) decodes JPEG images with SkiaSharp's decoder.
    /// </summary>
    public static ViewerOptions For(ViewerOptions? options)
    {
        // Issue #428: the Skia tiles decode images as the options say (at the resolution they are drawn at, by default).
        bool scaled = options?.ScaledImageDecode ?? true;
        double samplesPerPixel = options?.ImageSamplesPerPixel ?? Broadside.Rendering.RenderOptions.DefaultImageSamplesPerPixel;
        // Issue #374: the engine the view builds decodes JPEG images with SkiaSharp's decoder before a tile is drawn (bit-identical
        // to the managed codec); options can turn it off by name, and an engine the app gives is used as it is.
        PdfOptions? documentOptions = options?.DocumentOptions;
        if (options?.Engine is null)
        {
            documentOptions = (documentOptions ?? new PdfOptions()).UseSkiaImageDecoder();
        }

        var copy = new ViewerOptions
        {
            Engine = options?.Engine,
            DocumentOptions = documentOptions,
            Credentials = options?.Credentials,
            Threading = options?.Threading ?? ViewerThreading.Automatic,
            Intent = options?.Intent ?? default,
            BackendFactory = options?.BackendFactory ?? (() => new SkiaTileBackend(scaled, samplesPerPixel)),
            ScaledImageDecode = scaled,
            ImageSamplesPerPixel = samplesPerPixel,
            Yield = options?.Yield,
        };
        if (options is not null)
        {
            copy.SliceBudget = options.SliceBudget;
            copy.MaxConcurrency = options.MaxConcurrency;
        }

        return copy;
    }
}
