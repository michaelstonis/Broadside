using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>What a transparency group's content does that decides how the rasterizer can composite it.</summary>
/// <param name="Blends">Whether any paint or nested group in it uses a blend mode other than Normal.</param>
/// <param name="Transparency">Whether anything in it is not opaque: an alpha constant below 1, a nested group, or a masked image.</param>
internal readonly record struct GroupContent(bool Blends, bool Transparency);

/// <summary>
/// Replays a group's content without painting to find out whether it blends with its backdrop (a blend mode other than Normal,
/// ISO 32000-2 §11.3.5) or is transparent at all. A group whose content only uses Normal has the same result whether it is isolated or
/// not (§11.4.5: only blending sees the backdrop); one whose content is opaque gives nearly the same result in any blending space.
/// </summary>
/// <remarks>Reused for every group; allocates nothing.</remarks>
internal sealed class GroupProbe : IRenderingBackend
{
    private bool _blends;
    private bool _transparency;

    /// <summary>Inspects the content of <paramref name="group"/>, nested groups included.</summary>
    public GroupContent Inspect(in DisplayGroup group)
    {
        _blends = false;
        _transparency = false;
        group.Replay(this);
        return new GroupContent(_blends, _transparency);
    }

    /// <summary>Inspects a whole list, such as a tiling pattern's cell.</summary>
    public GroupContent Inspect(DisplayList list)
    {
        _blends = false;
        _transparency = false;
        list.Replay(this);
        return new GroupContent(_blends, _transparency);
    }

    public void SaveState()
    {
    }

    public void RestoreState()
    {
    }

    public void SetTransform(in Matrix transform)
    {
    }

    public void FillPath(PathView path, FillRule rule, in DisplayColor color)
    {
    }

    public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
    {
    }

    public void IntersectClip(PathView path, FillRule rule)
    {
    }

    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) =>
        _transparency |= image.SoftMask is not null || image.Mask is not null || !image.ColorKey.IsEmpty;

    public void DrawGlyphs(in GlyphRun run)
    {
    }

    public void DrawShading(in DisplayShading shading)
    {
    }

    public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
    {
    }

    public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
    {
    }

    public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
    {
    }

    public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
    {
        _blends |= blendMode != BlendMode.Normal;
        _transparency |= strokeAlpha < 1 || fillAlpha < 1;
    }

    /// <summary>Overprint and halftones do not make a cell blend (§11.7.4.5 applies only under the overprint preview).</summary>
    public void SetDeviceParameters(in DisplayDeviceParameters parameters)
    {
    }

    public void DrawGroup(in DisplayGroup group)
    {
        _blends |= group.BlendMode != BlendMode.Normal;
        _transparency = true;
        group.Replay(this);
    }

    public void BeginAnnotation(in DisplayAnnotation annotation)
    {
    }

    public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern) => InspectCell(pattern.Cell);

    public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern) => InspectCell(pattern.Cell);

    public void EndAnnotation()
    {
    }

    /// <summary>A tiling pattern paints its cell as a group of its own (§11.6.7): what it does counts as transparency, its blend modes too.</summary>
    private void InspectCell(DisplayList cell)
    {
        _transparency = true;
        cell.Replay(this);
    }
}
