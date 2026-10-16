using System.Numerics;
using static Broadside.Fonts.OpenType.LayoutData;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// The GPOS part of the applier: single (1) and pair (2) adjustment, cursive attachment (3), mark-to-base (4), mark-to-ligature (5)
/// and mark-to-mark (6) attachment, contextual (7) and chained contextual (8) positioning (the matching GSUB uses for types 5 and
/// 6) and extension (9, resolved by <see cref="LayoutTable"/>). Adjustments land in the glyphs' <see cref="ShapingGlyph.XAdvance"/>,
/// <see cref="ShapingGlyph.XOffset"/> and <see cref="ShapingGlyph.YOffset"/>; attachments are recorded and resolved by
/// <see cref="Finish"/>.
/// </summary>
/// <remarks>
/// OpenType specification, "GPOS — Glyph Positioning Table" (ValueRecord, Anchor tables formats 1–3, MarkArray) for horizontal
/// text: YAdvance and Device or VariationIndex tables are not applied (there is no pixel size and no variation instance). Anchor
/// format 2 contour points and format 3 device tables fall back to the design coordinates, as the specification allows.
/// </remarks>
internal ref partial struct LayoutApplier
{
    /// <summary>A mark attached to a base, ligature or mark glyph (types 4–6).</summary>
    public const int MarkAttachment = 1;

    /// <summary>A glyph attached to its neighbour by a cursive connection (type 3).</summary>
    public const int CursiveAttachment = 2;

    /// <summary>The deepest chain of attachments resolved (a mark on a mark on a cursive glyph...).</summary>
    private const int MaxAttachmentDepth = 64;

    /// <summary>
    /// Ends positioning: marks take no advance (their GDEF class says they are marks), then every attached glyph's offset becomes
    /// relative to its own pen position (OpenType "GPOS", mark attachment: the mark's anchor lands on the base's).
    /// </summary>
    public static void Finish(GlyphBuffer buffer, bool rightToLeft)
    {
        Span<ShapingGlyph> glyphs = buffer.Glyphs;
        for (int index = 0; index < glyphs.Length; index++)
        {
            if (glyphs[index].Class == GdefTable.MarkGlyph)
            {
                glyphs[index].XAdvance = 0;
            }
        }

        for (int index = 0; index < glyphs.Length; index++)
        {
            Propagate(glyphs, index, rightToLeft, MaxAttachmentDepth);
        }
    }

    private static void Propagate(Span<ShapingGlyph> glyphs, int index, bool rightToLeft, int depth)
    {
        int chain = glyphs[index].AttachChain;
        if (chain == 0)
        {
            return;
        }

        glyphs[index].AttachChain = 0;
        int parent = index + chain;
        if ((uint)parent >= (uint)glyphs.Length || depth == 0)
        {
            return;
        }

        Propagate(glyphs, parent, rightToLeft, depth - 1);
        ref ShapingGlyph glyph = ref glyphs[index];
        glyph.YOffset += glyphs[parent].YOffset;
        if (glyph.AttachType == CursiveAttachment)
        {
            return;
        }

        glyph.XOffset += glyphs[parent].XOffset;
        if (parent > index)
        {
            return;
        }

        // The mark is drawn at its own pen position: take away the advances between it and its base, in the order they are drawn.
        if (!rightToLeft)
        {
            for (int between = parent; between < index; between++)
            {
                glyph.XOffset -= glyphs[between].XAdvance;
            }
        }
        else
        {
            for (int between = parent + 1; between <= index; between++)
            {
                glyph.XOffset += glyphs[between].XAdvance;
            }
        }
    }

    private bool Position(Lookup lookup, int subtable, int index, int depth, out int next)
    {
        next = index + 1;
        return lookup.Type switch
        {
            1 => SingleAdjustment(subtable, index),
            2 => PairAdjustment(lookup, subtable, index, out next),
            3 => CursiveAttach(lookup, subtable, index),
            4 => MarkToBase(subtable, index, ligature: false),
            5 => MarkToBase(subtable, index, ligature: true),
            6 => MarkToMark(lookup, subtable, index),
            7 => Context(lookup, subtable, index, depth, out next),
            8 => ChainContext(lookup, subtable, index, depth, out next),
            _ => false,
        };
    }

    private readonly bool SingleAdjustment(int subtable, int index)
    {
        int coverage = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), _buffer[index].Glyph);
        int format = U16(_data, subtable + 4);
        switch (U16(_data, subtable))
        {
            case 1 when coverage >= 0:
                Adjust(format, subtable + 6, index);
                return true;
            case 2 when coverage >= 0 && coverage < U16(_data, subtable + 6):
                Adjust(format, subtable + 8 + (coverage * ValueSize(format)), index);
                return true;
            default:
                return false;
        }
    }

    private readonly bool PairAdjustment(Lookup lookup, int subtable, int index, out int next)
    {
        next = index + 1;
        int first = _buffer[index].Glyph;
        int coverage = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), first);
        int second = coverage < 0 ? -1 : NextUnskipped(lookup, index);
        if (second < 0)
        {
            return false;
        }

        int format1 = U16(_data, subtable + 4);
        int format2 = U16(_data, subtable + 6);
        int size1 = ValueSize(format1);
        int size2 = ValueSize(format2);
        int glyph = _buffer[second].Glyph;
        long record;
        switch (U16(_data, subtable))
        {
            case 1:
                {
                    int set = coverage < U16(_data, subtable + 8) ? Offset16(_data, subtable, subtable + 10 + (coverage * 2)) : -1;
                    int stride = 2 + size1 + size2;
                    int low = 0;
                    int high = set < 0 ? -1 : Math.Min(U16(_data, set), (_data.Length - set - 2) / stride) - 1;
                    record = -1;
                    while (low <= high)
                    {
                        int middle = (low + high) >>> 1;
                        int at = set + 2 + (middle * stride);
                        int value = U16(_data, at);
                        if (value == glyph)
                        {
                            record = at + 2;
                            break;
                        }

                        (low, high) = value < glyph ? (middle + 1, high) : (low, middle - 1);
                    }

                    break;
                }

            case 2:
                {
                    int class1 = ClassOf(_data, Offset16(_data, subtable, subtable + 8), first);
                    int class2 = ClassOf(_data, Offset16(_data, subtable, subtable + 10), glyph);
                    int class2Count = U16(_data, subtable + 14);
                    record = class1 < U16(_data, subtable + 12) && class2 < class2Count
                        ? subtable + 16 + ((((long)class1 * class2Count) + class2) * (size1 + size2))
                        : -1;
                    break;
                }

            default:
                return false;
        }

        if (record < 0 || record + size1 + size2 > _data.Length)
        {
            return false;
        }

        Adjust(format1, (int)record, index);
        Adjust(format2, (int)record + size1, second);
        next = size2 > 0 ? second + 1 : second;
        return true;
    }

    private readonly bool CursiveAttach(Lookup lookup, int subtable, int index)
    {
        int coverageTable = Offset16(_data, subtable, subtable + 2);
        int count = U16(_data, subtable + 4);
        int coverage = CoverageIndex(_data, coverageTable, _buffer[index].Glyph);
        if (U16(_data, subtable) != 1 || coverage < 0 || coverage >= count
            || !Anchor(Offset16(_data, subtable, subtable + 6 + (coverage * 4)), out double entryX, out double entryY))
        {
            return false;
        }

        int previous = PreviousUnskipped(lookup, index);
        int previousCoverage = previous < 0 ? -1 : CoverageIndex(_data, coverageTable, _buffer[previous].Glyph);
        if (previousCoverage < 0 || previousCoverage >= count
            || !Anchor(Offset16(_data, subtable, subtable + 8 + (previousCoverage * 4)), out double exitX, out double exitY))
        {
            return false;
        }

        // The exit point of the glyph before meets the entry point of this one (in logical order; HarfBuzz's convention).
        ref ShapingGlyph before = ref _buffer[previous];
        ref ShapingGlyph after = ref _buffer[index];
        if (!_rightToLeft)
        {
            before.XAdvance = exitX + before.XOffset;
            double shift = entryX + after.XOffset;
            after.XAdvance -= shift;
            after.XOffset -= shift;
        }
        else
        {
            double shift = exitX + before.XOffset;
            before.XAdvance -= shift;
            before.XOffset -= shift;
            after.XAdvance = entryX + after.XOffset;
        }

        // The cross-stream (vertical) offset: the RightToLeft flag says the chain ends on the baseline at its last glyph.
        bool lastOnBaseline = (lookup.Flag & LookupFlags.RightToLeft) != 0;
        int child = lastOnBaseline ? previous : index;
        int parent = lastOnBaseline ? index : previous;
        double offset = lastOnBaseline ? entryY - exitY : exitY - entryY;
        ReverseCursiveChain(child, parent);
        ref ShapingGlyph attached = ref _buffer[child];
        attached.AttachType = CursiveAttachment;
        attached.AttachChain = parent - child;
        attached.YOffset = offset;
        ref ShapingGlyph other = ref _buffer[parent];
        if (other.AttachType == CursiveAttachment && other.AttachChain == child - parent)
        {
            // The parent was attached to this child: separate them.
            other.AttachChain = 0;
            other.YOffset = 0;
        }

        return true;
    }

    /// <summary>Turns the cursive chain hanging from <paramref name="index"/> around, so that it hangs from <paramref name="parent"/>, its new parent.</summary>
    private readonly void ReverseCursiveChain(int index, int parent)
    {
        int chain = _buffer[index].AttachChain;
        int type = _buffer[index].AttachType;
        if (chain == 0 || type != CursiveAttachment)
        {
            return;
        }

        _buffer[index].AttachChain = 0;
        double offset = _buffer[index].YOffset;
        for (int steps = 0; steps < _buffer.Length; steps++)
        {
            int next = index + chain;
            if (next == parent || (uint)next >= (uint)_buffer.Length)
            {
                return;
            }

            ref ShapingGlyph glyph = ref _buffer[next];
            int nextChain = glyph.AttachChain;
            int nextType = glyph.AttachType;
            double nextOffset = glyph.YOffset;
            glyph.YOffset = -offset;
            glyph.AttachChain = -chain;
            glyph.AttachType = type;
            if (nextChain == 0 || nextType != CursiveAttachment)
            {
                return;
            }

            (index, chain, type, offset) = (next, nextChain, nextType, nextOffset);
        }
    }

    /// <summary>Mark-to-base (4) or mark-to-ligature (5): attaches the mark at <paramref name="index"/> to the glyph before it that is not a mark.</summary>
    private bool MarkToBase(int subtable, int index, bool ligature)
    {
        int mark = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), _buffer[index].Glyph);
        if (U16(_data, subtable) != 1 || mark < 0)
        {
            return false;
        }

        int target = index - 1;
        while (target >= 0 && _buffer[target].Class == GdefTable.MarkGlyph && --_budget > 0)
        {
            target--;
        }

        int targetCoverage = target < 0 || _buffer[target].Class == GdefTable.MarkGlyph
            ? -1
            : CoverageIndex(_data, Offset16(_data, subtable, subtable + 4), _buffer[target].Glyph);
        int classCount = U16(_data, subtable + 6);
        int array = Offset16(_data, subtable, subtable + 10);
        if (targetCoverage < 0 || array < 0 || targetCoverage >= U16(_data, array)
            || !MarkRecord(Offset16(_data, subtable, subtable + 8), mark, classCount, out int markClass, out int markAnchor))
        {
            return false;
        }

        long anchor;
        if (!ligature)
        {
            anchor = array + 2 + ((((long)targetCoverage * classCount) + markClass) * 2);
        }
        else
        {
            // The mark sits on the component it followed when the ligature formed; a mark after the ligature on its last component.
            int attach = Offset16(_data, array, array + 2 + (targetCoverage * 2));
            int components = attach < 0 ? 0 : U16(_data, attach);
            int component = _buffer[index].Component;
            component = component > 0 && _buffer[target].Component > 0 ? Math.Min(component, components) - 1 : components - 1;
            anchor = component < 0 ? -1 : attach + 2 + ((((long)component * classCount) + markClass) * 2);
            array = attach;
        }

        return anchor >= 0 && anchor < _data.Length && Attach(index, target, markAnchor, Offset16(_data, array, (int)anchor));
    }

    /// <summary>Mark-to-mark (6): attaches the mark at <paramref name="index"/> to the mark before it.</summary>
    private readonly bool MarkToMark(Lookup lookup, int subtable, int index)
    {
        int mark = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), _buffer[index].Glyph);
        if (U16(_data, subtable) != 1 || mark < 0)
        {
            return false;
        }

        // The mark before is found with the lookup's mark filtering but none of its Ignore flags.
        int previous = PreviousUnskipped(lookup with { Flag = lookup.Flag & ~(LookupFlags.IgnoreBaseGlyphs | LookupFlags.IgnoreLigatures | LookupFlags.IgnoreMarks) }, index);
        int previousCoverage = previous < 0 || _buffer[previous].Class != GdefTable.MarkGlyph
            ? -1
            : CoverageIndex(_data, Offset16(_data, subtable, subtable + 4), _buffer[previous].Glyph);
        int classCount = U16(_data, subtable + 6);
        int array = Offset16(_data, subtable, subtable + 10);
        if (previousCoverage < 0 || array < 0 || previousCoverage >= U16(_data, array)
            || !MarkRecord(Offset16(_data, subtable, subtable + 8), mark, classCount, out int markClass, out int markAnchor))
        {
            return false;
        }

        long anchor = array + 2 + ((((long)previousCoverage * classCount) + markClass) * 2);
        return anchor < _data.Length && Attach(index, previous, markAnchor, Offset16(_data, array, (int)anchor));
    }

    /// <summary>The class and anchor (absolute offset) of mark <paramref name="mark"/> of the MarkArray at <paramref name="array"/>.</summary>
    private readonly bool MarkRecord(int array, int mark, int classCount, out int markClass, out int anchor)
    {
        markClass = 0;
        anchor = -1;
        if (array < 0 || mark >= U16(_data, array))
        {
            return false;
        }

        int record = array + 2 + (mark * 4);
        markClass = U16(_data, record);
        anchor = Offset16(_data, array, record + 2);
        return markClass < classCount && anchor >= 0;
    }

    /// <summary>Places the mark at <paramref name="index"/> so its anchor lands on the anchor of the glyph at <paramref name="target"/>.</summary>
    private readonly bool Attach(int index, int target, int markAnchor, int targetAnchor)
    {
        if (!Anchor(markAnchor, out double markX, out double markY) || !Anchor(targetAnchor, out double targetX, out double targetY))
        {
            return false;
        }

        ref ShapingGlyph glyph = ref _buffer[index];
        glyph.XOffset = targetX - markX;
        glyph.YOffset = targetY - markY;
        glyph.AttachType = MarkAttachment;
        glyph.AttachChain = target - index;
        return true;
    }

    /// <summary>The design coordinates of the Anchor table at <paramref name="offset"/> (formats 1–3), in thousandths of the font size.</summary>
    private readonly bool Anchor(int offset, out double x, out double y)
    {
        bool valid = offset >= 0 && U16(_data, offset) is >= 1 and <= 3 && offset + 6 <= _data.Length;
        x = valid ? I16(_data, offset + 2) * _scale : 0;
        y = valid ? I16(_data, offset + 4) * _scale : 0;
        return valid;
    }

    /// <summary>Adds the ValueRecord at <paramref name="record"/> (fields as <paramref name="format"/> says) to the glyph at <paramref name="index"/>.</summary>
    private readonly void Adjust(int format, int record, int index)
    {
        ref ShapingGlyph glyph = ref _buffer[index];
        int at = record;
        if ((format & 0x0001) != 0)
        {
            glyph.XOffset += I16(_data, at) * _scale;
            at += 2;
        }

        if ((format & 0x0002) != 0)
        {
            glyph.YOffset += I16(_data, at) * _scale;
            at += 2;
        }

        if ((format & 0x0004) != 0)
        {
            glyph.XAdvance += I16(_data, at) * _scale;
        }
    }

    /// <summary>The size in bytes of a ValueRecord of <paramref name="format"/>: two bytes per field present.</summary>
    private static int ValueSize(int format) => BitOperations.PopCount((uint)(format & 0xFF)) * 2;
}
