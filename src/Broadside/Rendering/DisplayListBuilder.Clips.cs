using Broadside.Content;

namespace Broadside.Rendering;

/// <summary>Clipping paths (ISO 32000-2 §8.5.4) and form bounding boxes (§8.10.1).</summary>
internal sealed partial class DisplayListBuilder
{
    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §8.5.4: the interpreter reports a clip after the paint of its path object, so the record follows that paint and
    /// clips only what comes after it. Path clips (<c>W</c>, <c>W*</c>) and rectangles (a form's bounding box, §8.10.1) become
    /// <see cref="IRenderingBackend.IntersectClip"/> under the CTM the clip was set with. Text clips (§9.3.6) become one such call
    /// with the glyphs' outlines (<c>DisplayListBuilder.TextClips.cs</c>).
    /// </remarks>
    public override void IntersectClip(in ClipEvent clip, ContentContext context)
    {
        if (clip.Kind == ClipKind.Text)
        {
            IntersectTextClip(clip);
            return;
        }

        if (clip.Kind is not (ClipKind.Path or ClipKind.Rectangle))
        {
            return;
        }

        int pathIndex = InternPath(clip.Path);
        SetTransform(clip.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.IntersectClip, clip.Rule, pathIndex, 0));
    }
}
