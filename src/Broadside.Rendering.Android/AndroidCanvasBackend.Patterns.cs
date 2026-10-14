using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Rendering.Android;

/// <summary>Tiling patterns (ISO 32000-2 §8.7.3), not drawn by the Android backend yet: skipped and reported.</summary>
public sealed partial class AndroidCanvasBackend
{
    private sealed partial class Backend
    {
        private static readonly Diagnostic TilingPatternSkipped = new(
            "BackendTilingPatternUnsupported",
            DiagnosticSeverity.Warning,
            "The Android backend does not draw tiling patterns yet (ISO 32000-2 §8.7.3); the page's fills and strokes with a tiling pattern were left out of this render.");

        private bool _tilingReported;

        /// <summary>Skips a fill with a tiling pattern (§8.7.3.1) and reports it once per render (ADR 0005: never silently).</summary>
        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern) => ReportTiling();

        /// <summary>Skips a stroke with a tiling pattern (§8.7.3.1) and reports it once per render.</summary>
        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern) => ReportTiling();

        private void ReportTiling()
        {
            if (!_tilingReported)
            {
                _tilingReported = true;
                Diagnostics.Add(TilingPatternSkipped);
            }
        }
    }
}
