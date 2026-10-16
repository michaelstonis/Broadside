using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Constant alpha and blend mode (ISO 32000-2 §11.6.3, §11.6.4.4): recorded before a paint when they differ from what the backend
/// holds at that point of the replay, and saved and restored with the state like the transform.
/// </summary>
internal sealed partial class DisplayListBuilder
{
    private DisplayCompositing[] _compositings = new DisplayCompositing[4];
    private int _compositingCount;

    // The compositing the backend holds at this point of the replay, and the ones saved by the open SaveState records.
    private DisplayCompositing _compositing = DisplayCompositing.Initial;
    private DisplayCompositing[] _savedCompositings = new DisplayCompositing[16];
    private int _savedCompositingCount;

    /// <summary>Records the alpha constants and blend mode of <paramref name="state"/> before a paint, when they differ from the backend's.</summary>
    /// <remarks>
    /// ISO 32000-2 §11.6.4.4: <c>CA</c> applies to strokes, <c>ca</c> to every other paint; §11.6.3: the blend mode. Inside a
    /// transparency group the interpreter starts both alphas at 1 and the blend mode at Normal (§11.6.6); the group itself is not in
    /// the list yet. The device parameters (overprint, halftone) are recorded here too, so every paint gets them.
    /// </remarks>
    /// <param name="state">The paint's graphics state.</param>
    /// <param name="context">The run, which builds the soft mask's group the first time it is needed.</param>
    /// <param name="ownMask">Whether the paint is an image with a mask of its own, which overrides the soft mask (§11.6.4.3).</param>
    private void SetCompositing(in GraphicsState state, ContentContext context, bool ownMask = false)
    {
        SetSoftMask(state, context, ownMask);
        SetDeviceParameters(state);
        SetCompositing(new DisplayCompositing(state.StrokeAlpha, state.FillAlpha, state.BlendMode));
    }

    /// <summary>
    /// Records the compositing of a paint in a shading pattern: the state's, with the pattern's own ExtGState applied as §11.6.7
    /// allows. Only parameters that affect <c>sh</c> count, so the pattern's <c>ca</c> scales the alpha of the paint (the stroking one
    /// for a stroke) and its blend mode, when not Normal, is the one the shading composites with; its <c>CA</c> is ignored.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §11.6.7 and §8.7.4.1, Table 75. The pattern's implicit knockout group holds one <c>sh</c>, so its opacity times the
    /// paint's is the shading's, and the blend it makes with the backdrop inside the non-isolated group is the one that shows.
    /// </remarks>
    private void SetPatternCompositing(in PdfColor color, ContentContext context, bool stroke)
    {
        ref readonly GraphicsState state = ref context.State;
        SetSoftMask(state, context, overridden: false);
        SetDeviceParameters(state);
        var compositing = new DisplayCompositing(state.StrokeAlpha, state.FillAlpha, state.BlendMode);
        if (context.GetPattern(color) is PdfShadingPattern { ExtGState: { } dictionary })
        {
            ExtGStateParameters parameters = context.GetGraphicsState(dictionary, null);
            double alpha = parameters.FillAlpha ?? 1;
            compositing = stroke
                ? compositing with { StrokeAlpha = compositing.StrokeAlpha * alpha }
                : compositing with { FillAlpha = compositing.FillAlpha * alpha };
            if (parameters.BlendMode is { } mode and not BlendMode.Normal)
            {
                compositing = compositing with { BlendMode = mode };
            }
        }

        SetCompositing(compositing);
    }

    private void SetCompositing(in DisplayCompositing compositing)
    {
        if (compositing == _compositing)
        {
            return;
        }

        _compositing = compositing;
        AddOperation(new DisplayOperation(DisplayOperationKind.SetCompositing, default, _compositingCount, 0));
        Append(ref _compositings, ref _compositingCount, compositing);
    }

    /// <summary>Saves the alpha constants, blend mode and device parameters the backend holds, as its state save does.</summary>
    private void SaveCompositing()
    {
        Append(ref _savedCompositings, ref _savedCompositingCount, _compositing);
        SaveDeviceParameters();
        SaveSoftMask();
    }

    /// <summary>Restores what <see cref="SaveCompositing"/> saved, as the backend's state restore does.</summary>
    private void RestoreCompositing()
    {
        _compositing = _savedCompositings[--_savedCompositingCount];
        RestoreDeviceParameters();
        RestoreSoftMask();
    }

    private void ResetCompositing()
    {
        _compositingCount = 0;
        _compositing = DisplayCompositing.Initial;
        _savedCompositingCount = 0;
        ResetSoftMaskTracking();
    }
}
