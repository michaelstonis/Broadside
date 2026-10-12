using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;

namespace Broadside.Fonts.TrueType;

/// <summary>Glyph outlines: decoding "glyf" descriptions into points, composing components, and turning contours into a path.</summary>
internal sealed partial class TrueTypeFontProgram
{
    /// <inheritdoc/>
    /// <remarks>
    /// OpenType "glyf" table. Points are decoded into pooled buffers, composites are assembled with their transforms (offsets are not
    /// scaled unless SCALED_COMPONENT_OFFSET alone is set, as on Apple and Microsoft platforms) and point matching, and each contour
    /// becomes move, line and quadratic segments, with the implied on-curve point midway between two off-curve points. Nothing is
    /// allocated once the buffers are warm.
    /// </remarks>
    public override GlyphOutlineStatus GetOutline(int glyphId, GlyphOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        outline.Clear();
        if (!TryGetGlyphData(glyphId, report: true, out ReadOnlySpan<byte> glyph))
        {
            return GlyphOutlineStatus.Invalid;
        }

        if (glyph.IsEmpty)
        {
            return GlyphOutlineStatus.Empty;
        }

        Span<int> ancestors = stackalloc int[_maxDepth + 1];
        var loader = new GlyphLoader(this, ancestors);
        try
        {
            GlyphOutlineStatus status = loader.Load(glyphId, 0);
            if (status != GlyphOutlineStatus.Complete || loader.PointCount == 0)
            {
                return status == GlyphOutlineStatus.Invalid ? status : GlyphOutlineStatus.Empty;
            }

            // Place the outline as rasterizers do: its left edge at the left side bearing from the origin.
            double shift = 0;
            if (glyph.Length >= 10 && ReadHorizontalMetrics(glyphId) is { Present: true } metrics)
            {
                shift = metrics.LeftSideBearing - BinaryPrimitives.ReadInt16BigEndian(glyph[2..]);
            }

            loader.Emit(outline, shift);
            return outline.IsEmpty ? GlyphOutlineStatus.Empty : GlyphOutlineStatus.Complete;
        }
        finally
        {
            loader.Dispose();
        }
    }

    /// <summary>The points of one top-level glyph, components included, in pooled buffers.</summary>
    private ref struct GlyphLoader
    {
        private readonly TrueTypeFontProgram _program;
        private readonly Span<int> _ancestors;
        private double[] _x;
        private double[] _y;
        private bool[] _onCurve;
        private int[] _ends;
        private byte[] _flags;
        private int _contourCount;
        private int _components;

        public GlyphLoader(TrueTypeFontProgram program, Span<int> ancestors)
        {
            _program = program;
            _ancestors = ancestors;
            _x = ArrayPool<double>.Shared.Rent(64);
            _y = ArrayPool<double>.Shared.Rent(64);
            _onCurve = ArrayPool<bool>.Shared.Rent(64);
            _ends = ArrayPool<int>.Shared.Rent(8);
            _flags = ArrayPool<byte>.Shared.Rent(64);
        }

        public int PointCount { get; private set; }

        public readonly void Dispose()
        {
            ArrayPool<double>.Shared.Return(_x);
            ArrayPool<double>.Shared.Return(_y);
            ArrayPool<bool>.Shared.Return(_onCurve);
            ArrayPool<int>.Shared.Return(_ends);
            ArrayPool<byte>.Shared.Return(_flags);
        }

        /// <summary>Appends a glyph's points and contours; on failure, leaves the buffers as they were.</summary>
        public GlyphOutlineStatus Load(int glyphId, int depth)
        {
            _ancestors[depth] = glyphId;
            if (!_program.TryGetGlyphData(glyphId, report: true, out ReadOnlySpan<byte> glyph))
            {
                return GlyphOutlineStatus.Invalid;
            }

            if (glyph.IsEmpty)
            {
                return GlyphOutlineStatus.Empty;
            }

            if (glyph.Length < 10)
            {
                _program.ReportGlyph(glyphId, "it is shorter than a glyph header; it is dropped");
                return GlyphOutlineStatus.Invalid;
            }

            int contours = BinaryPrimitives.ReadInt16BigEndian(glyph);
            if (contours >= 0)
            {
                return LoadSimple(glyph, contours, glyphId);
            }

            if (contours < -1)
            {
                _program.ReportGlyph(glyphId, string.Create(CultureInfo.InvariantCulture, $"its numberOfContours is {contours}; it is read as a composite glyph"));
            }

            return LoadComposite(glyph, glyphId, depth);
        }

        /// <summary>Writes the contours as a path (implied on-curve midpoints between off-curve points), shifted horizontally.</summary>
        public readonly void Emit(GlyphOutline outline, double shift)
        {
            int start = 0;
            for (int contour = 0; contour < _contourCount; contour++)
            {
                int end = _ends[contour];
                if (end > start)
                {
                    EmitContour(outline, start, end, shift);
                }

                start = Math.Max(start, end + 1);
            }
        }

        private readonly void EmitContour(GlyphOutline outline, int first, int last, double shift)
        {
            // Start on an on-curve point: the first, else the last, else the midpoint between them.
            double startX;
            double startY;
            int from;
            int to = last;
            if (_onCurve[first])
            {
                (startX, startY) = (_x[first], _y[first]);
                from = first + 1;
            }
            else if (_onCurve[last])
            {
                (startX, startY) = (_x[last], _y[last]);
                from = first;
                to = last - 1;
            }
            else
            {
                (startX, startY) = ((_x[first] + _x[last]) / 2, (_y[first] + _y[last]) / 2);
                from = first;
            }

            outline.MoveTo(startX + shift, startY);
            bool pending = false;
            double controlX = 0;
            double controlY = 0;
            for (int index = from; index <= to; index++)
            {
                double x = _x[index];
                double y = _y[index];
                if (_onCurve[index])
                {
                    if (pending)
                    {
                        outline.QuadTo(controlX + shift, controlY, x + shift, y);
                        pending = false;
                    }
                    else
                    {
                        outline.LineTo(x + shift, y);
                    }
                }
                else
                {
                    if (pending)
                    {
                        outline.QuadTo(controlX + shift, controlY, ((controlX + x) / 2) + shift, (controlY + y) / 2);
                    }

                    (controlX, controlY) = (x, y);
                    pending = true;
                }
            }

            if (pending)
            {
                outline.QuadTo(controlX + shift, controlY, startX + shift, startY);
            }

            outline.Close();
        }

        /// <summary>A simple glyph: contour ends, instructions (skipped), flags, then x and y coordinates as deltas.</summary>
        private GlyphOutlineStatus LoadSimple(ReadOnlySpan<byte> glyph, int contours, int glyphId)
        {
            if (contours == 0)
            {
                return GlyphOutlineStatus.Empty;
            }

            int basePoint = PointCount;
            int baseContour = _contourCount;
            int position = 10;
            if (position + (2 * contours) + 2 > glyph.Length)
            {
                return Truncated(glyphId, basePoint, baseContour);
            }

            EnsureContours(baseContour + contours);
            int previous = -1;
            for (int contour = 0; contour < contours; contour++)
            {
                int end = BinaryPrimitives.ReadUInt16BigEndian(glyph[(position + (2 * contour))..]);
                if (end <= previous)
                {
                    _program.ReportGlyph(glyphId, "its contour end points are not in increasing order; it is dropped");
                    return GlyphOutlineStatus.Invalid;
                }

                _ends[baseContour + contour] = basePoint + end;
                previous = end;
            }

            int points = previous + 1;
            position += 2 * contours;
            if (basePoint + points > _program._context.MaxGlyphPoints)
            {
                _program.ReportGlyph(glyphId, string.Create(CultureInfo.InvariantCulture, $"it has more than {_program._context.MaxGlyphPoints} points with its components; it is dropped"));
                return GlyphOutlineStatus.Invalid;
            }

            int instructions = BinaryPrimitives.ReadUInt16BigEndian(glyph[position..]);
            position += 2 + instructions;
            if (position > glyph.Length)
            {
                return Truncated(glyphId, basePoint, baseContour);
            }

            // Flags, with REPEAT runs; a run past the point count is cut short.
            EnsurePoints(basePoint + points);
            Span<byte> flags = _flags.AsSpan(0, points);
            int count = 0;
            bool clamped = false;
            while (count < points)
            {
                if (position >= glyph.Length)
                {
                    return Truncated(glyphId, basePoint, baseContour);
                }

                byte flag = glyph[position++];
                flags[count++] = flag;
                if ((flag & 0x08) != 0)
                {
                    if (position >= glyph.Length)
                    {
                        return Truncated(glyphId, basePoint, baseContour);
                    }

                    int repeat = glyph[position++];
                    if (repeat > points - count)
                    {
                        clamped = true;
                        repeat = points - count;
                    }

                    flags.Slice(count, repeat).Fill(flag);
                    count += repeat;
                }
            }

            if (clamped)
            {
                _program.ReportGlyph(glyphId, "a repeated flag runs past its last point; the run is cut short");
            }

            int xLength = 0;
            int yLength = 0;
            foreach (byte flag in flags)
            {
                xLength += (flag & 0x02) != 0 ? 1 : (flag & 0x10) != 0 ? 0 : 2;
                yLength += (flag & 0x04) != 0 ? 1 : (flag & 0x20) != 0 ? 0 : 2;
            }

            if (position + xLength + yLength > glyph.Length)
            {
                return Truncated(glyphId, basePoint, baseContour);
            }

            // x: SHORT with SAME = positive byte, SHORT alone = negative byte, SAME alone = repeat (no byte), neither = int16 delta.
            int xAt = position;
            int yAt = position + xLength;
            int x = 0;
            int y = 0;
            for (int index = 0; index < points; index++)
            {
                byte flag = flags[index];
                switch (flag & 0x12)
                {
                    case 0x02:
                        x -= glyph[xAt++];
                        break;
                    case 0x12:
                        x += glyph[xAt++];
                        break;
                    case 0x00:
                        x += BinaryPrimitives.ReadInt16BigEndian(glyph[xAt..]);
                        xAt += 2;
                        break;
                }

                switch (flag & 0x24)
                {
                    case 0x04:
                        y -= glyph[yAt++];
                        break;
                    case 0x24:
                        y += glyph[yAt++];
                        break;
                    case 0x00:
                        y += BinaryPrimitives.ReadInt16BigEndian(glyph[yAt..]);
                        yAt += 2;
                        break;
                }

                _x[basePoint + index] = x;
                _y[basePoint + index] = y;
                _onCurve[basePoint + index] = (flag & 0x01) != 0;
            }

            PointCount = basePoint + points;
            _contourCount = baseContour + contours;
            return GlyphOutlineStatus.Complete;
        }

        /// <summary>A composite glyph: components loaded recursively, transformed, then offset or matched to a point.</summary>
        private GlyphOutlineStatus LoadComposite(ReadOnlySpan<byte> glyph, int glyphId, int depth)
        {
            int basePoint = PointCount;
            int position = 10;
            int flags;
            do
            {
                if (position + 4 > glyph.Length)
                {
                    _program.ReportGlyph(glyphId, "its component records are truncated; the components before the damage are kept");
                    break;
                }

                flags = BinaryPrimitives.ReadUInt16BigEndian(glyph[position..]);
                int child = BinaryPrimitives.ReadUInt16BigEndian(glyph[(position + 2)..]);
                position += 4;
                if (position + ComponentArgumentsLength(flags) > glyph.Length)
                {
                    _program.ReportGlyph(glyphId, "its component records are truncated; the components before the damage are kept");
                    break;
                }

                int argument1;
                int argument2;
                bool xy = (flags & CompositeFlags.ArgsAreXYValues) != 0;
                if ((flags & CompositeFlags.ArgsAreWords) != 0)
                {
                    argument1 = xy ? BinaryPrimitives.ReadInt16BigEndian(glyph[position..]) : BinaryPrimitives.ReadUInt16BigEndian(glyph[position..]);
                    argument2 = xy ? BinaryPrimitives.ReadInt16BigEndian(glyph[(position + 2)..]) : BinaryPrimitives.ReadUInt16BigEndian(glyph[(position + 2)..]);
                    position += 4;
                }
                else
                {
                    argument1 = xy ? (sbyte)glyph[position] : glyph[position];
                    argument2 = xy ? (sbyte)glyph[position + 1] : glyph[position + 1];
                    position += 2;
                }

                // x' = xscale·x + scale10·y; y' = scale01·x + yscale·y (F2Dot14 values).
                double xScale = 1;
                double scale01 = 0;
                double scale10 = 0;
                double yScale = 1;
                bool transformed = true;
                if ((flags & CompositeFlags.HaveScale) != 0)
                {
                    xScale = yScale = F2Dot14(glyph, position);
                    position += 2;
                }
                else if ((flags & CompositeFlags.HaveXAndYScale) != 0)
                {
                    xScale = F2Dot14(glyph, position);
                    yScale = F2Dot14(glyph, position + 2);
                    position += 4;
                }
                else if ((flags & CompositeFlags.HaveTwoByTwo) != 0)
                {
                    xScale = F2Dot14(glyph, position);
                    scale01 = F2Dot14(glyph, position + 2);
                    scale10 = F2Dot14(glyph, position + 4);
                    yScale = F2Dot14(glyph, position + 6);
                    position += 8;
                }
                else
                {
                    transformed = false;
                }

                if (++_components > MaxComponentsPerGlyph)
                {
                    _program.ReportGlyph(glyphId, string.Create(CultureInfo.InvariantCulture, $"it has more than {MaxComponentsPerGlyph} components with its nested components; the rest are skipped"));
                    break;
                }

                if (!CanLoadComponent(glyphId, child, depth))
                {
                    continue;
                }

                int childBase = PointCount;
                int childContours = _contourCount;
                GlyphOutlineStatus status = Load(child, depth + 1);
                if (status == GlyphOutlineStatus.Invalid)
                {
                    PointCount = childBase;
                    _contourCount = childContours;
                    continue;
                }

                if (transformed)
                {
                    for (int index = childBase; index < PointCount; index++)
                    {
                        double px = _x[index];
                        double py = _y[index];
                        _x[index] = (xScale * px) + (scale10 * py);
                        _y[index] = (scale01 * px) + (yScale * py);
                    }
                }

                double offsetX;
                double offsetY;
                if (xy)
                {
                    offsetX = argument1;
                    offsetY = argument2;
                    if (transformed && (flags & CompositeFlags.ScaledComponentOffset) != 0 && (flags & CompositeFlags.UnscaledComponentOffset) == 0)
                    {
                        offsetX *= Math.Sqrt((xScale * xScale) + (scale10 * scale10));
                        offsetY *= Math.Sqrt((yScale * yScale) + (scale01 * scale01));
                    }
                }
                else
                {
                    // Point matching: the component's point argument2 lands on the parent's point argument1.
                    int parentPoint = basePoint + argument1;
                    int childPoint = childBase + argument2;
                    if (parentPoint < childBase && childPoint < PointCount)
                    {
                        offsetX = _x[parentPoint] - _x[childPoint];
                        offsetY = _y[parentPoint] - _y[childPoint];
                    }
                    else
                    {
                        _program.ReportGlyph(glyphId, "a component's matching point numbers are out of range; the component is not moved");
                        offsetX = offsetY = 0;
                    }
                }

                if (offsetX != 0 || offsetY != 0)
                {
                    for (int index = childBase; index < PointCount; index++)
                    {
                        _x[index] += offsetX;
                        _y[index] += offsetY;
                    }
                }
            }
            while ((flags & CompositeFlags.MoreComponents) != 0);

            // WE_HAVE_INSTRUCTIONS: the instructions after the last component are not read.
            return GlyphOutlineStatus.Complete;
        }

        private readonly bool CanLoadComponent(int glyphId, int child, int depth)
        {
            if ((uint)child >= (uint)_program._glyphCount)
            {
                _program.ReportGlyph(glyphId, string.Create(CultureInfo.InvariantCulture, $"a component refers to glyph {child}, past the program's {_program._glyphCount} glyphs; it is skipped"));
                return false;
            }

            if (_ancestors[..(depth + 1)].Contains(child))
            {
                _program.ReportGlyph(glyphId, string.Create(CultureInfo.InvariantCulture, $"a component refers to glyph {child}, which contains this glyph; it is skipped"));
                return false;
            }

            if (depth + 1 > _program._maxDepth)
            {
                _program.ReportGlyph(glyphId, string.Create(CultureInfo.InvariantCulture, $"its components nest deeper than {_program._maxDepth} levels; the deeper ones are skipped"));
                return false;
            }

            return true;
        }

        private static double F2Dot14(ReadOnlySpan<byte> glyph, int position) => BinaryPrimitives.ReadInt16BigEndian(glyph[position..]) / 16384.0;

        private GlyphOutlineStatus Truncated(int glyphId, int basePoint, int baseContour)
        {
            PointCount = basePoint;
            _contourCount = baseContour;
            _program.ReportGlyph(glyphId, "its data ends before its last point; it is dropped");
            return GlyphOutlineStatus.Invalid;
        }

        private void EnsurePoints(int count)
        {
            if (count > _x.Length)
            {
                Grow(ref _x, count, PointCount);
                Grow(ref _y, count, PointCount);
                Grow(ref _onCurve, count, PointCount);
            }

            if (count > _flags.Length)
            {
                ArrayPool<byte>.Shared.Return(_flags);
                _flags = ArrayPool<byte>.Shared.Rent(count);
            }
        }

        private void EnsureContours(int count)
        {
            if (count > _ends.Length)
            {
                Grow(ref _ends, count, _contourCount);
            }
        }

        private static void Grow<T>(ref T[] array, int count, int used)
        {
            T[] larger = ArrayPool<T>.Shared.Rent(Math.Max(count, array.Length * 2));
            array.AsSpan(0, used).CopyTo(larger);
            ArrayPool<T>.Shared.Return(array);
            array = larger;
        }
    }
}
