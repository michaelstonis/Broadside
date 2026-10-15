using static Broadside.Fonts.OpenType.LayoutData;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// Applies GSUB or GPOS lookups to a <see cref="GlyphBuffer"/>. GSUB: single (1), multiple (2), alternate (3), ligature (4),
/// contextual (5) and chained contextual (6) substitution in formats 1–3, extension (7, resolved by <see cref="LayoutTable"/>) and
/// reverse chaining contextual single substitution (8); GPOS: lookup types 1–9 (LayoutApplier.Positioning.cs). Subtables are read
/// in place; nothing allocates.
/// </summary>
/// <remarks>
/// OpenType specification, "GSUB — Glyph Substitution Table", lookup types 1–8, "GPOS — Glyph Positioning Table", lookup types
/// 1–9, and "Lookup Table" (LookupFlag: glyphs a lookup skips while matching). A lookup applies to a glyph whose feature mask it
/// shares; the glyphs of a context are matched whatever their mask, a ligature's components only when they share it. Nested
/// lookups go at most <see cref="MaxNesting"/> deep, and a budget of subtable applications bounds the work a damaged or hostile
/// table can cause.
/// </remarks>
internal ref partial struct LayoutApplier
{
    /// <summary>The deepest nesting of contextual lookups applied.</summary>
    public const int MaxNesting = 6;

    /// <summary>The longest input, backtrack or lookahead sequence matched.</summary>
    public const int MaxContext = 64;

    private readonly ReadOnlySpan<byte> _data;
    private readonly LayoutTable _table;
    private readonly GdefTable? _gdef;
    private readonly GlyphBuffer _buffer;
    private readonly bool _positioning;
    private readonly double _scale;
    private readonly bool _rightToLeft;
    private int _budget;

    /// <summary>Creates an applier of <paramref name="table"/>'s lookups.</summary>
    /// <param name="table">The GSUB or GPOS table.</param>
    /// <param name="gdef">The glyph definitions, or <see langword="null"/>.</param>
    /// <param name="buffer">The run.</param>
    /// <param name="budget">The most subtable applications and glyph steps the lookups may take.</param>
    /// <param name="scale">For GPOS: thousandths of the font size per font design unit.</param>
    /// <param name="rightToLeft">For GPOS: whether the run is right to left (cursive attachment and mark offsets).</param>
    public LayoutApplier(LayoutTable table, GdefTable? gdef, GlyphBuffer buffer, int budget, double scale = 1, bool rightToLeft = false)
    {
        _data = table.Data.Span;
        _table = table;
        _gdef = gdef;
        _buffer = buffer;
        _budget = budget;
        _positioning = table.IsPositioning;
        _scale = scale;
        _rightToLeft = rightToLeft;
    }

    private enum Match
    {
        Glyph,
        Class,
        Coverage,
    }

    /// <summary>Applies lookup <paramref name="lookupIndex"/> across the run to every glyph with a bit of <paramref name="mask"/>.</summary>
    public void Apply(int lookupIndex, uint mask)
    {
        Lookup lookup = _table.Lookups[lookupIndex];
        if (lookup.Type == 8 && !_positioning)
        {
            for (int index = _buffer.Length - 1; index >= 0 && _budget > 0; index--)
            {
                if ((_buffer[index].Mask & mask) != 0 && !Skips(lookup, index))
                {
                    ApplyAt(lookup, index, mask, 0, out _);
                }
            }

            return;
        }

        int position = 0;
        while (position < _buffer.Length && _budget > 0)
        {
            _budget--;
            if ((_buffer[position].Mask & mask) != 0 && !Skips(lookup, position) && ApplyAt(lookup, position, mask, 0, out int next))
            {
                position = next;
            }
            else
            {
                position++;
            }
        }
    }

    private bool ApplyAt(Lookup lookup, int index, uint mask, int depth, out int next)
    {
        for (int number = 0; number < lookup.SubtableCount; number++)
        {
            if (--_budget < 0)
            {
                break;
            }

            int subtable = _table.Subtable(lookup, number);
            if (subtable < 0)
            {
                continue;
            }

            bool applied = _positioning ? Position(lookup, subtable, index, depth, out next) : lookup.Type switch
            {
                1 => Single(subtable, index, out next),
                2 => Multiple(subtable, index, out next),
                3 => Alternate(subtable, index, out next),
                4 => Ligature(lookup, subtable, index, mask, out next),
                5 => Context(lookup, subtable, index, depth, out next),
                6 => ChainContext(lookup, subtable, index, depth, out next),
                8 => ReverseChain(lookup, subtable, index, out next),
                _ => Fail(out next),
            };
            if (applied)
            {
                return true;
            }
        }

        next = index + 1;
        return false;
    }

    private static bool Fail(out int next)
    {
        next = 0;
        return false;
    }

    private readonly bool Single(int subtable, int index, out int next)
    {
        next = index + 1;
        int glyph = _buffer[index].Glyph;
        int coverage = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), glyph);
        if (coverage < 0)
        {
            return false;
        }

        switch (U16(_data, subtable))
        {
            case 1:
                Set(index, (glyph + I16(_data, subtable + 4)) & 0xFFFF);
                return true;
            case 2 when coverage < U16(_data, subtable + 4):
                Set(index, U16(_data, subtable + 6 + (coverage * 2)));
                return true;
            default:
                return false;
        }
    }

    private readonly bool Multiple(int subtable, int index, out int next)
    {
        next = index + 1;
        int coverage = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), _buffer[index].Glyph);
        if (U16(_data, subtable) != 1 || coverage < 0 || coverage >= U16(_data, subtable + 4))
        {
            return false;
        }

        int sequence = Offset16(_data, subtable, subtable + 6 + (coverage * 2));
        int count = sequence < 0 ? 0 : Math.Min(U16(_data, sequence), (_data.Length - sequence - 2) / 2);
        if (sequence < 0 || !_buffer.Expand(index, count))
        {
            return false;
        }

        // A sequence of no glyphs deletes the glyph (fonts use it, though the specification asks for at least one).
        for (int offset = 0; offset < count; offset++)
        {
            Set(index + offset, U16(_data, sequence + 2 + (offset * 2)));
        }

        next = index + count;
        return true;
    }

    private readonly bool Alternate(int subtable, int index, out int next)
    {
        next = index + 1;
        int coverage = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), _buffer[index].Glyph);
        if (U16(_data, subtable) != 1 || coverage < 0 || coverage >= U16(_data, subtable + 4))
        {
            return false;
        }

        // The feature value selects the alternate; an enabled feature has value 1, the first alternate.
        int set = Offset16(_data, subtable, subtable + 6 + (coverage * 2));
        if (set < 0 || U16(_data, set) == 0)
        {
            return false;
        }

        Set(index, U16(_data, set + 2));
        return true;
    }

    private readonly bool Ligature(Lookup lookup, int subtable, int index, uint mask, out int next)
    {
        next = index + 1;
        int coverage = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), _buffer[index].Glyph);
        if (U16(_data, subtable) != 1 || coverage < 0 || coverage >= U16(_data, subtable + 4))
        {
            return false;
        }

        int set = Offset16(_data, subtable, subtable + 6 + (coverage * 2));
        int count = set < 0 ? 0 : U16(_data, set);
        Span<int> positions = stackalloc int[MaxContext];
        for (int ligature = 0; ligature < count; ligature++)
        {
            int table = Offset16(_data, set, set + 2 + (ligature * 2));
            int components = table < 0 ? 0 : U16(_data, table + 2);
            if (components == 0 || components > MaxContext)
            {
                continue;
            }

            positions[0] = index;
            int position = index;
            int matched = 1;
            for (; matched < components; matched++)
            {
                position = NextUnskipped(lookup, position);
                if (position < 0 || (_buffer[position].Mask & mask) == 0 || _buffer[position].Glyph != U16(_data, table + 4 + ((matched - 1) * 2)))
                {
                    break;
                }

                positions[matched] = position;
            }

            if (matched == components)
            {
                Ligate(positions[..components], U16(_data, table));
                return true;
            }
        }

        return false;
    }

    /// <summary>Replaces the glyphs at <paramref name="positions"/> by <paramref name="ligature"/>; skipped glyphs between them follow it, in the merged cluster.</summary>
    private readonly void Ligate(scoped ReadOnlySpan<int> positions, int ligature)
    {
        int first = positions[0];
        int last = positions[^1];
        int cluster = int.MaxValue;
        for (int index = first; index <= last; index++)
        {
            cluster = Math.Min(cluster, _buffer[index].Cluster);
        }

        ref ShapingGlyph head = ref _buffer[first];
        head.Glyph = ligature;
        head.Cluster = cluster;
        head.Class = _gdef?.GlyphClass(ligature) is int known and > 0 ? known : GdefTable.LigatureGlyph;
        head.Component = positions.Length;
        int write = first + 1;
        int component = 1;
        for (int index = first + 1; index <= last; index++)
        {
            if (component < positions.Length && positions[component] == index)
            {
                component++;
                continue;
            }

            ShapingGlyph kept = _buffer[index];
            kept.Cluster = cluster;
            kept.Component = component;
            _buffer[write++] = kept;
        }

        int removed = positions.Length - 1;
        for (int index = last + 1; index < _buffer.Length; index++)
        {
            _buffer[index - removed] = _buffer[index];
        }

        _buffer.Truncate(_buffer.Length - removed);
    }

    private bool Context(Lookup lookup, int subtable, int index, int depth, out int next)
    {
        next = index + 1;
        int glyph = _buffer[index].Glyph;
        int format = U16(_data, subtable);
        Span<int> positions = stackalloc int[MaxContext];
        if (format == 3)
        {
            int inputCount = U16(_data, subtable + 2);
            int records = subtable + 6 + (inputCount * 2);
            if (inputCount == 0 || inputCount > MaxContext
                || CoverageIndex(_data, Offset16(_data, subtable, subtable + 6), glyph) < 0
                || !MatchInput(lookup, index, inputCount, subtable + 8, Match.Coverage, subtable, positions))
            {
                return false;
            }

            next = ApplyRecords(records, U16(_data, subtable + 4), positions, inputCount, depth, index);
            return true;
        }

        int coverage = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), glyph);
        if (coverage < 0 || format is not (1 or 2))
        {
            return false;
        }

        int classDef = format == 2 ? Offset16(_data, subtable, subtable + 4) : -1;
        int setIndex = format == 2 ? ClassOf(_data, classDef, glyph) : coverage;
        int setsAt = format == 2 ? subtable + 6 : subtable + 4;
        if (setIndex >= U16(_data, setsAt))
        {
            return false;
        }

        int set = Offset16(_data, subtable, setsAt + 2 + (setIndex * 2));
        int rules = set < 0 ? 0 : U16(_data, set);
        Match kind = format == 2 ? Match.Class : Match.Glyph;
        for (int rule = 0; rule < rules; rule++)
        {
            int table = Offset16(_data, set, set + 2 + (rule * 2));
            int inputCount = table < 0 ? 0 : U16(_data, table);
            if (inputCount == 0 || inputCount > MaxContext || !MatchInput(lookup, index, inputCount, table + 4, kind, classDef, positions))
            {
                continue;
            }

            next = ApplyRecords(table + 4 + ((inputCount - 1) * 2), U16(_data, table + 2), positions, inputCount, depth, index);
            return true;
        }

        return false;
    }

    private bool ChainContext(Lookup lookup, int subtable, int index, int depth, out int next)
    {
        next = index + 1;
        int glyph = _buffer[index].Glyph;
        int format = U16(_data, subtable);
        Span<int> positions = stackalloc int[MaxContext];
        if (format == 3)
        {
            int backtrackCount = U16(_data, subtable + 2);
            int input = subtable + 4 + (backtrackCount * 2);
            int inputCount = U16(_data, input);
            int lookahead = input + 2 + (inputCount * 2);
            int lookaheadCount = U16(_data, lookahead);
            int records = lookahead + 2 + (lookaheadCount * 2);
            if (inputCount == 0 || inputCount > MaxContext || backtrackCount > MaxContext || lookaheadCount > MaxContext
                || CoverageIndex(_data, Offset16(_data, subtable, input + 2), glyph) < 0
                || !MatchInput(lookup, index, inputCount, input + 4, Match.Coverage, subtable, positions)
                || !MatchBacktrack(lookup, index, backtrackCount, subtable + 4, Match.Coverage, subtable)
                || !MatchLookahead(lookup, positions[inputCount - 1], lookaheadCount, lookahead + 2, Match.Coverage, subtable))
            {
                return false;
            }

            next = ApplyRecords(records + 2, U16(_data, records), positions, inputCount, depth, index);
            return true;
        }

        int coverage = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), glyph);
        if (coverage < 0 || format is not (1 or 2))
        {
            return false;
        }

        int backtrackClasses = format == 2 ? Offset16(_data, subtable, subtable + 4) : -1;
        int inputClasses = format == 2 ? Offset16(_data, subtable, subtable + 6) : -1;
        int lookaheadClasses = format == 2 ? Offset16(_data, subtable, subtable + 8) : -1;
        int setIndex = format == 2 ? ClassOf(_data, inputClasses, glyph) : coverage;
        int setsAt = format == 2 ? subtable + 10 : subtable + 4;
        if (setIndex >= U16(_data, setsAt))
        {
            return false;
        }

        int set = Offset16(_data, subtable, setsAt + 2 + (setIndex * 2));
        int rules = set < 0 ? 0 : U16(_data, set);
        Match kind = format == 2 ? Match.Class : Match.Glyph;
        for (int rule = 0; rule < rules; rule++)
        {
            int table = Offset16(_data, set, set + 2 + (rule * 2));
            if (table < 0)
            {
                continue;
            }

            int backtrackCount = U16(_data, table);
            int input = table + 2 + (backtrackCount * 2);
            int inputCount = U16(_data, input);
            int lookahead = input + 2 + (Math.Max(0, inputCount - 1) * 2);
            int lookaheadCount = U16(_data, lookahead);
            int records = lookahead + 2 + (lookaheadCount * 2);
            if (inputCount == 0 || inputCount > MaxContext || backtrackCount > MaxContext || lookaheadCount > MaxContext
                || !MatchInput(lookup, index, inputCount, input + 2, kind, inputClasses, positions)
                || !MatchBacktrack(lookup, index, backtrackCount, table + 2, kind, backtrackClasses)
                || !MatchLookahead(lookup, positions[inputCount - 1], lookaheadCount, lookahead + 2, kind, lookaheadClasses))
            {
                continue;
            }

            next = ApplyRecords(records + 2, U16(_data, records), positions, inputCount, depth, index);
            return true;
        }

        return false;
    }

    private readonly bool ReverseChain(Lookup lookup, int subtable, int index, out int next)
    {
        next = index - 1;
        int coverage = CoverageIndex(_data, Offset16(_data, subtable, subtable + 2), _buffer[index].Glyph);
        int backtrackCount = U16(_data, subtable + 4);
        int lookahead = subtable + 6 + (backtrackCount * 2);
        int lookaheadCount = U16(_data, lookahead);
        int substitutes = lookahead + 2 + (lookaheadCount * 2);
        if (U16(_data, subtable) != 1 || coverage < 0 || coverage >= U16(_data, substitutes)
            || backtrackCount > MaxContext || lookaheadCount > MaxContext
            || !MatchBacktrack(lookup, index, backtrackCount, subtable + 6, Match.Coverage, subtable)
            || !MatchLookahead(lookup, index, lookaheadCount, lookahead + 2, Match.Coverage, subtable))
        {
            return false;
        }

        Set(index, U16(_data, substitutes + 2 + (coverage * 2)));
        return true;
    }

    /// <summary>Matches the input sequence after its first glyph (at <paramref name="index"/>); <paramref name="items"/> is the second item.</summary>
    private readonly bool MatchInput(Lookup lookup, int index, int count, int items, Match kind, int classDefOrBase, scoped Span<int> positions)
    {
        positions[0] = index;
        int position = index;
        for (int item = 1; item < count; item++)
        {
            position = NextUnskipped(lookup, position);
            if (position < 0 || !Matches(kind, items + ((item - 1) * 2), classDefOrBase, _buffer[position].Glyph))
            {
                return false;
            }

            positions[item] = position;
        }

        return true;
    }

    /// <summary>Matches the backtrack sequence, nearest glyph first, before <paramref name="index"/>.</summary>
    private readonly bool MatchBacktrack(Lookup lookup, int index, int count, int items, Match kind, int classDefOrBase)
    {
        int position = index;
        for (int item = 0; item < count; item++)
        {
            position = PreviousUnskipped(lookup, position);
            if (position < 0 || !Matches(kind, items + (item * 2), classDefOrBase, _buffer[position].Glyph))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Matches the lookahead sequence after <paramref name="last"/>, the last input glyph.</summary>
    private readonly bool MatchLookahead(Lookup lookup, int last, int count, int items, Match kind, int classDefOrBase)
    {
        int position = last;
        for (int item = 0; item < count; item++)
        {
            position = NextUnskipped(lookup, position);
            if (position < 0 || !Matches(kind, items + (item * 2), classDefOrBase, _buffer[position].Glyph))
            {
                return false;
            }
        }

        return true;
    }

    private readonly bool Matches(Match kind, int item, int classDefOrBase, int glyph) => kind switch
    {
        Match.Glyph => U16(_data, item) == glyph,
        Match.Class => ClassOf(_data, classDefOrBase, glyph) == U16(_data, item),
        _ => CoverageIndex(_data, Offset16(_data, classDefOrBase, item), glyph) >= 0,
    };

    /// <summary>
    /// Applies the SubstLookupRecords of a matched context, in order, each at the input glyph it names, and returns where matching
    /// resumes: after the last input glyph, as the nested lookups moved it.
    /// </summary>
    private int ApplyRecords(int records, int count, scoped Span<int> positions, int inputCount, int depth, int index)
    {
        int end = positions[inputCount - 1];
        for (int record = 0; record < count && depth < MaxNesting && _budget > 0; record++)
        {
            int at = records + (record * 4);
            int sequenceIndex = U16(_data, at);
            int lookupIndex = U16(_data, at + 2);
            if (sequenceIndex >= inputCount || lookupIndex >= _table.Lookups.Length || _table.Lookups[lookupIndex].Type == 0 || (_table.Lookups[lookupIndex].Type == 8 && !_positioning)
                || positions[sequenceIndex] >= _buffer.Length)
            {
                continue;
            }

            int before = _buffer.Length;
            ApplyAt(_table.Lookups[lookupIndex], positions[sequenceIndex], uint.MaxValue, depth + 1, out _);
            int delta = _buffer.Length - before;
            if (delta != 0)
            {
                for (int later = sequenceIndex + 1; later < inputCount; later++)
                {
                    positions[later] = Math.Clamp(positions[later] + delta, positions[sequenceIndex], Math.Max(positions[sequenceIndex], _buffer.Length - 1));
                }

                end = Math.Clamp(end + delta, positions[sequenceIndex], Math.Max(positions[sequenceIndex], _buffer.Length - 1));
            }
        }

        return Math.Max(end + 1, index + 1);
    }

    private readonly void Set(int index, int glyph)
    {
        ref ShapingGlyph target = ref _buffer[index];
        target.Glyph = glyph;
        if (_gdef?.GlyphClass(glyph) is int known and > 0)
        {
            target.Class = known;
        }
    }

    private readonly int NextUnskipped(Lookup lookup, int index)
    {
        for (int position = index + 1; position < _buffer.Length; position++)
        {
            if (!Skips(lookup, position))
            {
                return position;
            }
        }

        return -1;
    }

    private readonly int PreviousUnskipped(Lookup lookup, int index)
    {
        for (int position = index - 1; position >= 0; position--)
        {
            if (!Skips(lookup, position))
            {
                return position;
            }
        }

        return -1;
    }

    /// <summary>Whether the lookup's flags skip the glyph at <paramref name="index"/> (OpenType "Lookup Table", LookupFlag).</summary>
    private readonly bool Skips(Lookup lookup, int index)
    {
        ref ShapingGlyph glyph = ref _buffer[index];
        int flag = lookup.Flag;
        switch (glyph.Class)
        {
            case GdefTable.BaseGlyph:
                return (flag & LookupFlags.IgnoreBaseGlyphs) != 0;
            case GdefTable.LigatureGlyph:
                return (flag & LookupFlags.IgnoreLigatures) != 0;
            case GdefTable.MarkGlyph:
                if ((flag & LookupFlags.IgnoreMarks) != 0)
                {
                    return true;
                }

                if ((flag & LookupFlags.UseMarkFilteringSet) != 0)
                {
                    return _gdef is null || !_gdef.InMarkGlyphSet(lookup.MarkFilteringSet, glyph.Glyph);
                }

                int attachment = (flag & LookupFlags.MarkAttachmentTypeMask) >> 8;
                return attachment != 0 && (_gdef is null || _gdef.MarkAttachClass(glyph.Glyph) != attachment);
            default:
                return false;
        }
    }
}
