using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Rendering.Android;

/// <summary>Transparency groups (ISO 32000-2 §11.4, §11.6.6, §11.7.4.4), not composited as groups by the Android backend yet.</summary>
public sealed partial class AndroidCanvasBackend
{
    private sealed partial class Backend
    {
        private static readonly Diagnostic GroupApproximated = new(
            "BackendTransparencyGroupApproximated",
            DiagnosticSeverity.Warning,
            "The Android backend does not composite transparency groups yet (ISO 32000-2 §11.4); groups with an opacity, a blend mode, knockout, isolation or a colour space of their own were drawn as their content alone.");

        private bool _groupReported;

        /// <summary>
        /// Draws a transparency group's content straight onto the canvas (§11.4.4 NOTE 5: exact for a non-isolated, non-knockout group
        /// in the Normal blend mode at opacity 1); any other group is approximated that way and reported once per render (ADR 0005).
        /// </summary>
        public void DrawGroup(in DisplayGroup group)
        {
            bool plain = !group.IsKnockout && group.Opacity >= 1 && group.BlendMode == BlendMode.Normal && (!group.IsIsolated || group.ColorSpace is null);
            if (!plain && !_groupReported)
            {
                _groupReported = true;
                Diagnostics.Add(GroupApproximated);
            }

            group.Replay(this);
        }
    }
}
