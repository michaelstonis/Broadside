using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>Transparency groups (ISO 32000-2 §11.4, §11.6.6, §11.7.4.4), not composited as groups by the CoreGraphics backend yet.</summary>
internal sealed partial class CoreGraphicsBackend
{
    private static readonly Diagnostic GroupApproximated = new(
        "BackendTransparencyGroupApproximated",
        DiagnosticSeverity.Warning,
        "The CoreGraphics backend does not composite transparency groups yet (ISO 32000-2 §11.4); groups with an opacity, a blend mode, knockout, isolation or a colour space of their own were drawn as their content alone.");

    private static readonly Diagnostic SoftMaskIgnored = new(
        "BackendSoftMaskIgnored",
        DiagnosticSeverity.Warning,
        "The CoreGraphics backend does not apply soft masks yet (ISO 32000-2 §11.5, §11.6.5.1); paints and groups under a soft mask were drawn unmasked.");

    private bool _groupReported;
    private bool _softMaskReported;

    /// <summary>Ignores a soft mask (ISO 32000-2 §11.6.5.1): the paints that follow are drawn unmasked, reported once per replay (ADR 0005).</summary>
    public void SetSoftMask(in DisplaySoftMask mask)
    {
        if (!mask.IsNone)
        {
            if (!_softMaskReported)
            {
                _softMaskReported = true;
                Diagnostics.Add(SoftMaskIgnored);
            }
        }
    }

    /// <summary>
    /// Draws a transparency group's content straight onto the context (§11.4.4 NOTE 5: exact for a non-isolated, non-knockout group in
    /// the Normal blend mode at opacity 1); any other group is approximated that way and reported once per replay (ADR 0005).
    /// </summary>
    public void DrawGroup(in DisplayGroup group)
    {
        bool plain = !group.IsKnockout && group.Opacity >= 1 && group.BlendMode == BlendMode.Normal && (!group.IsIsolated || group.ColorSpace is null)
            && group.SoftMask.IsNone;
        if (!plain && !_groupReported)
        {
            _groupReported = true;
            Diagnostics.Add(GroupApproximated);
        }

        group.Replay(this);
    }
}
