using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>
    /// What this backend approximates (transparency groups and soft masks until #124) or leaves out, each reported once per kind per
    /// render (ADR 0005: nothing is skipped silently). The diagnostics are shared immutable instances, so reporting allocates nothing;
    /// the replayers of pattern cells report through the backend's own.
    /// </summary>
    private sealed partial class Replayer
    {
        private static readonly Diagnostic GroupApproximated = new(
            "BackendTransparencyGroupApproximated",
            DiagnosticSeverity.Warning,
            "The Skia backend does not composite transparency groups yet (ISO 32000-2 §11.4); groups with an opacity, a blend mode, knockout, isolation or a colour space of their own were drawn as their content alone.");

        private static readonly Diagnostic SoftMaskIgnored = new(
            "BackendSoftMaskIgnored",
            DiagnosticSeverity.Warning,
            "The Skia backend does not apply soft masks yet (ISO 32000-2 §11.5, §11.6.5.1); paints and groups under a soft mask were drawn unmasked.");

        private int _reported;

        /// <summary>Gets the diagnostics of the current or last render.</summary>
        public List<Diagnostic> Diagnostics { get; } = new(4);

        /// <summary>
        /// Draws a transparency group's content straight onto the canvas (ISO 32000-2 §11.4.4 NOTE 5: exact for a non-isolated,
        /// non-knockout group in the Normal blend mode at opacity 1); any other group is approximated that way and reported (#124
        /// composites groups in layers).
        /// </summary>
        public void DrawGroup(in DisplayGroup group)
        {
            if (group.IsKnockout || group.Opacity < 1 || group.BlendMode != BlendMode.Normal || (group.IsIsolated && group.ColorSpace is not null)
                || !group.SoftMask.IsNone)
            {
                Report(32, GroupApproximated);
            }

            group.Replay(this);
        }

        /// <summary>Ignores a soft mask (ISO 32000-2 §11.6.5.1): the paints that follow are drawn unmasked, reported once per replay (ADR 0005).</summary>
        public void SetSoftMask(in DisplaySoftMask mask)
        {
            if (!mask.IsNone)
            {
                Report(64, SoftMaskIgnored);
            }
        }

        private void Report(int kind, Diagnostic diagnostic)
        {
            if (_owner is not null)
            {
                _owner.Report(kind, diagnostic);
                return;
            }

            if ((_reported & kind) == 0)
            {
                _reported |= kind;
                Diagnostics.Add(diagnostic);
            }
        }
    }
}
