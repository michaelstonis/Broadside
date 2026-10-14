using System.Buffers.Binary;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Text clips (ISO 32000-2 §9.3.6, Table 104 modes 4 to 7): the outlines of the glyphs a text object showed in a clipping mode,
/// combined into one path that intersects the clip under the non-zero winding rule at the end of the text object.
/// </summary>
internal sealed partial class DisplayListBuilder
{
    // The combined outline of the text clip being built, reused across text objects and builds.
    private PathVerb[] _textClipVerbs = new PathVerb[64];
    private int _textClipVerbCount;
    private PathPoint[] _textClipPoints = new PathPoint[128];
    private int _textClipPointCount;

    /// <summary>
    /// Records a text clip: each glyph's outline, selected as <see cref="ShowGlyph"/> selects it, placed by its glyph transform as one
    /// subpath group of a single path, intersected with the clip under the non-zero rule (§9.3.6, §8.5.3.3.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.3.6: the interpreter reports the clip at <c>ET</c>, after the glyphs were painted, so the record follows their
    /// runs. The path is built in the user space of the CTM at <c>ET</c> when every glyph was shown under it (the CTM cannot change
    /// inside a text object, §8.2 Figure 9), else in default user space with each glyph's own CTM folded in. When no glyph has an
    /// outline (only spaces, or glyphs no program draws), no clipping occurs: nothing is recorded. A contour of a single point (a
    /// TrueType anchor) is left out, as it paints nothing when the glyph is filled. Type 3 glyphs never reach a text clip
    /// (§9.3.6: in modes 4 to 7 nothing is added to the clipping path for them).
    /// </para>
    /// <para>Allocates nothing per glyph once the outline buffer and the build's tables have grown.</para>
    /// </remarks>
    private void IntersectTextClip(in ClipEvent clip)
    {
        ReadOnlySpan<TextClipGlyph> glyphs = clip.Glyphs;
        bool sameSpace = true;
        foreach (ref readonly TextClipGlyph glyph in glyphs)
        {
            if (glyph.Ctm != clip.Ctm)
            {
                sameSpace = false;
                break;
            }
        }

        _textClipVerbCount = 0;
        _textClipPointCount = 0;
        Span<byte> code = stackalloc byte[4];
        foreach (ref readonly TextClipGlyph glyph in glyphs)
        {
            if (glyph.Font is PdfType3Font)
            {
                continue;
            }

            int length = Math.Clamp(glyph.CodeLength, 1, 4);
            BinaryPrimitives.WriteUInt32BigEndian(code, glyph.CharacterCode);
            FontProgram? program = SelectGlyph(glyph.Font, glyph.CharacterCode, code[(4 - length)..], out int glyphId, out double scale, out int[]? components);
            if (program is null)
            {
                continue;
            }

            Matrix toSpace = sameSpace ? glyph.TextMatrix : glyph.TextMatrix * glyph.Ctm;
            Matrix glyphSpace = program.FontMatrix * new Matrix(scale, 0, 0, 1, 0, 0);
            if (components is null)
            {
                if (_substitute is null || glyphId != 0)
                {
                    AppendTextClipOutline(InternOutline(program, glyphId), glyphSpace * toSpace);
                }

                continue;
            }

            double offset = 0;
            foreach (int component in components)
            {
                Matrix placed = offset == 0 ? toSpace : Matrix.CreateTranslation(offset, 0) * toSpace;
                AppendTextClipOutline(InternOutline(program, component), glyphSpace * placed);
                offset += _substitute!.GetWidth(component) * scale / 1000;
            }
        }

        if (_textClipVerbCount == 0)
        {
            return;
        }

        int pathIndex = InternPath(new PathView(_textClipVerbs.AsSpan(0, _textClipVerbCount), _textClipPoints.AsSpan(0, _textClipPointCount)));
        SetTransform(sameSpace ? clip.Ctm : Matrix.Identity);
        AddOperation(new DisplayOperation(DisplayOperationKind.IntersectClip, FillRule.NonZero, pathIndex, 0));
    }

    /// <summary>Appends the subpaths of an interned glyph outline, mapped by <paramref name="transform"/>, leaving out single-point contours.</summary>
    private void AppendTextClipOutline(int pathIndex, Matrix transform)
    {
        DisplayPath path = _paths[pathIndex];
        ReadOnlySpan<PathVerb> verbs = _verbs.AsSpan(path.VerbStart, path.VerbCount);
        ReadOnlySpan<PathPoint> points = _points.AsSpan(path.PointStart, path.PointCount);
        EnsureCapacity(ref _textClipVerbs, _textClipVerbCount + verbs.Length);
        EnsureCapacity(ref _textClipPoints, _textClipPointCount + points.Length);
        int verb = 0;
        int point = 0;
        while (verb < verbs.Length)
        {
            // One subpath: its moveto and the segments up to the next moveto.
            int verbEnd = verb + 1;
            int pointEnd = point + PointCount(verbs[verb]);
            while (verbEnd < verbs.Length && verbs[verbEnd] != PathVerb.MoveTo)
            {
                pointEnd += PointCount(verbs[verbEnd]);
                verbEnd++;
            }

            if (pointEnd > points.Length)
            {
                return;
            }

            ReadOnlySpan<PathPoint> subpath = points[point..pointEnd];
            bool degenerate = true;
            foreach (PathPoint p in subpath)
            {
                if (p != subpath[0])
                {
                    degenerate = false;
                    break;
                }
            }

            if (!degenerate && verbs[verb] == PathVerb.MoveTo)
            {
                verbs[verb..verbEnd].CopyTo(_textClipVerbs.AsSpan(_textClipVerbCount));
                _textClipVerbCount += verbEnd - verb;
                foreach (PathPoint p in subpath)
                {
                    _textClipPoints[_textClipPointCount++] = transform.Transform(p);
                }
            }

            verb = verbEnd;
            point = pointEnd;
        }

        static int PointCount(PathVerb verb) => verb switch
        {
            PathVerb.MoveTo or PathVerb.LineTo => 1,
            PathVerb.QuadTo => 2,
            PathVerb.CubicTo => 3,
            _ => 0,
        };
    }
}
