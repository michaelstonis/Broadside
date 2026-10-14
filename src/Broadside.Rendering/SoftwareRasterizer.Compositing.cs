using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Constant alpha and blend mode (ISO 32000-2 §11.3, §11.6.3, §11.6.4.4): the compositors every paint goes through.</summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's compositing half.</summary>
    private sealed partial class Backend
    {
        // The compositors of strokes (CA) and of every other paint (ca), and the pairs saved by SaveState.
        private Compositor _fillCompositor;
        private Compositor _strokeCompositor;
        private Compositor[] _savedCompositors = new Compositor[32];
        private int _savedCompositorCount;

        /// <summary>Sets the alpha constants and blend mode of the paints that follow (§11.6.4.4, §11.6.3).</summary>
        /// <remarks>
        /// Every paint composites through <see cref="Compositor"/> in RGB, the rasterizer's blending colour space (§11.3.4): its
        /// coverage times the alpha constant is the source's shape and opacity, and the blend function combines it with the pixels
        /// already painted (§11.3.3, §11.3.6).
        /// </remarks>
        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
        {
            _strokeCompositor = new Compositor(strokeAlpha, blendMode);
            _fillCompositor = new Compositor(fillAlpha, blendMode);
        }

        /// <summary>Takes the overprint parameters and halftone of the paints that follow, and ignores them (§8.6.7, §10.6).</summary>
        /// <remarks>
        /// The rasterizer is a composite, continuous-tone RGB device that does not overprint: §8.6.7 says such a device ignores the
        /// overprint parameter, and §10.6.1 that a device that reproduces continuous tones needs no halftoning (owner decision 5 of
        /// the Phase 3 plan). Overprint preview is a separate render option (#227). Transfer functions are applied: they come with
        /// each colour's conversion (§10.5).
        /// </remarks>
        public void SetDeviceParameters(in DisplayDeviceParameters parameters)
        {
        }

        private void SaveCompositing()
        {
            if (_savedCompositorCount + 2 > _savedCompositors.Length)
            {
                Array.Resize(ref _savedCompositors, _savedCompositors.Length * 2);
            }

            _savedCompositors[_savedCompositorCount++] = _fillCompositor;
            _savedCompositors[_savedCompositorCount++] = _strokeCompositor;
        }

        private void RestoreCompositing()
        {
            if (_savedCompositorCount >= 2)
            {
                _strokeCompositor = _savedCompositors[--_savedCompositorCount];
                _fillCompositor = _savedCompositors[--_savedCompositorCount];
            }
        }

        private void ResetCompositing()
        {
            _fillCompositor = default;
            _strokeCompositor = default;
            _savedCompositorCount = 0;
        }
    }
}
