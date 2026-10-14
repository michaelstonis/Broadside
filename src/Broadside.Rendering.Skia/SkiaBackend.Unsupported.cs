using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>
    /// The operations of the list this backend does not draw yet: each is skipped, never approximated, and reported once per kind per
    /// render (ADR 0005: nothing is skipped silently). The diagnostics are shared immutable instances, so reporting allocates nothing.
    /// </summary>
    private sealed partial class Replayer
    {
        private static readonly Diagnostic ImageSkipped = new(
            "BackendImageUnsupported",
            DiagnosticSeverity.Information,
            "The Skia backend does not draw images yet (ISO 32000-2 §8.9); the page's images were left out of this render.");

        private static readonly Diagnostic ImageMaskSkipped = new(
            "BackendImageMaskUnsupported",
            DiagnosticSeverity.Information,
            "The Skia backend does not draw stencil masks yet (ISO 32000-2 §8.9.6.2); the page's stencil masks were left out of this render.");

        private static readonly Diagnostic GlyphsSkipped = new(
            "BackendGlyphsUnsupported",
            DiagnosticSeverity.Information,
            "The Skia backend does not draw text yet (ISO 32000-2 §9.4.3); the page's visible glyphs were left out of this render.");

        private static readonly Diagnostic ShadingSkipped = new(
            "BackendShadingUnsupported",
            DiagnosticSeverity.Information,
            "The Skia backend does not draw shadings yet (ISO 32000-2 §8.7.4); the page's shadings and shading-pattern paints were left out of this render.");

        private static readonly Diagnostic TilingPatternSkipped = new(
            "BackendTilingPatternUnsupported",
            DiagnosticSeverity.Information,
            "The Skia backend does not draw tiling patterns yet (ISO 32000-2 §8.7.3); the page's fills and strokes with a tiling pattern were left out of this render.");

        private int _reported;

        /// <summary>Gets the diagnostics of the current or last render.</summary>
        public List<Diagnostic> Diagnostics { get; } = new(4);

        /// <summary>Skips an image (ISO 32000-2 §8.9.4, §8.9.5).</summary>
        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) =>
            Report(1, ImageSkipped);

        /// <summary>Skips a stencil mask (ISO 32000-2 §8.9.6.2).</summary>
        public void FillImageMask(DisplayImageMask mask, in DisplayColor color) => Report(2, ImageMaskSkipped);

        /// <summary>Skips a glyph run (ISO 32000-2 §9.4.3); invisible text (§9.3.6, mode 3) paints nothing and is not reported.</summary>
        public void DrawGlyphs(in GlyphRun run)
        {
            if (run.Fills || run.Strokes)
            {
                Report(4, GlyphsSkipped);
            }
        }

        /// <summary>Skips a shading painted with <c>sh</c> (ISO 32000-2 §8.7.4.2).</summary>
        public void DrawShading(in DisplayShading shading) => Report(8, ShadingSkipped);

        /// <summary>Skips a fill with a shading pattern (ISO 32000-2 §8.7.4.1).</summary>
        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading) => Report(8, ShadingSkipped);

        /// <summary>Skips a stroke with a shading pattern (ISO 32000-2 §8.7.4.1).</summary>
        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading) => Report(8, ShadingSkipped);

        /// <summary>Skips a fill with a tiling pattern (ISO 32000-2 §8.7.3.1).</summary>
        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern) => Report(16, TilingPatternSkipped);

        /// <summary>Skips a stroke with a tiling pattern (ISO 32000-2 §8.7.3.1).</summary>
        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern) => Report(16, TilingPatternSkipped);

        private void Report(int kind, Diagnostic diagnostic)
        {
            if ((_reported & kind) == 0)
            {
                _reported |= kind;
                Diagnostics.Add(diagnostic);
            }
        }
    }
}
