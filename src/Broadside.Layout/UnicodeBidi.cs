using System.Buffers;
using static Broadside.Layout.Flow.BidiClass;

namespace Broadside.Layout.Flow;

/// <summary>
/// The Unicode Bidirectional Algorithm: UAX #9 revision 50, Unicode 16.0.0 (<see cref="BidiData.UnicodeVersion"/>), the default
/// algorithm without higher-level protocols. Resolves the embedding level of every UTF-16 unit of a text (rules P1 to I2) and
/// reorders a line by them (L1 and L2); L4 (mirroring) is the shaper's, which mirrors the characters of right-to-left runs.
/// </summary>
/// <remarks>
/// <para>
/// UAX #9 §3.3: P1 splits the text into paragraphs after each paragraph separator (Bidi_Class B); P2 and P3 find each
/// paragraph's level from its first strong character outside isolates unless a direction is given; X1 to X8 apply explicit
/// embeddings, overrides and isolates with the directional status stack (depth 125, overflow counted); X9 removes embedding
/// controls and boundary neutrals; X10 forms the isolating run sequences, each resolved by W1 to W7, N0 (paired brackets, BD16
/// with its 63-entry stack, canonical equivalents of U+2329/U+232A and U+3008/U+3009), N1, N2, I1 and I2. A character removed
/// by X9 takes the level of the character before it (the paragraph level at the start), as §5.2 suggests.
/// </para>
/// <para>
/// A surrogate pair takes the Bidi_Class of its code point in both units; a lone surrogate is L. Every buffer is pooled or on
/// the stack: nothing is allocated per character.
/// </para>
/// </remarks>
internal static class UnicodeBidi
{
    /// <summary>The deepest explicit embedding level (BD2).</summary>
    public const int MaxDepth = 125;

    /// <summary>The bit of a resolved level byte that holds the paragraph level (P2, P3) of its paragraph.</summary>
    public const byte ParagraphBit = 0x80;

    /// <summary>The mask of the embedding level in a resolved level byte.</summary>
    public const byte LevelMask = 0x7F;

    private const int BracketStackSize = 63;

    /// <summary>
    /// Resolves <paramref name="text"/>: every unit's embedding level in its low seven bits and its paragraph's level (0 or 1) in
    /// <see cref="ParagraphBit"/>. <paramref name="paragraphLevel"/> is 0 or 1 to set every paragraph's level, -1 for P2 and P3.
    /// </summary>
    public static void Resolve(ReadOnlySpan<char> text, int paragraphLevel, Span<byte> levels)
    {
        if (text.IsEmpty)
        {
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(text.Length);
        try
        {
            Span<byte> classes = rented.AsSpan(0, text.Length);
            Classify(text, classes);
            int start = 0;
            for (int index = 0; index < text.Length; index++)
            {
                if (classes[index] == (byte)B || index == text.Length - 1)
                {
                    // P1: a paragraph ends after its paragraph separator.
                    int end = index + 1;
                    int level = ResolveParagraph(classes[start..end], text[start..end], paragraphLevel, levels[start..end]);
                    byte bit = level == 1 ? ParagraphBit : (byte)0;
                    for (int unit = start; unit < end; unit++)
                    {
                        levels[unit] |= bit;
                    }

                    start = end;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>The Bidi_Class of each UTF-16 unit: a surrogate pair's code point in both units.</summary>
    public static void Classify(ReadOnlySpan<char> text, Span<byte> classes)
    {
        for (int index = 0; index < text.Length; index++)
        {
            char unit = text[index];
            if (char.IsHighSurrogate(unit) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                byte value = (byte)BidiData.Lookup(char.ConvertToUtf32(unit, text[index + 1]));
                classes[index] = value;
                classes[++index] = value;
            }
            else
            {
                classes[index] = (byte)BidiData.Lookup(unit);
            }
        }
    }

    /// <summary>
    /// Resolves one paragraph (P2 to I2) whose classes are <paramref name="classes"/>; <paramref name="text"/> gives the paired
    /// brackets of N0 (empty: none). Returns the paragraph level; <paramref name="levels"/> receives the embedding levels.
    /// </summary>
    public static int ResolveParagraph(ReadOnlySpan<byte> classes, ReadOnlySpan<char> text, int paragraphLevel, Span<byte> levels)
    {
        int length = classes.Length;
        int[] ints = ArrayPool<int>.Shared.Rent(length * 6);
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length * 2);
        try
        {
            var work = new Work(classes, text, levels, ints.AsSpan(0, length * 6), bytes.AsSpan(0, length * 2));
            work.MatchIsolates();
            int level = paragraphLevel is 0 or 1 ? paragraphLevel : work.FirstStrong(0, length, 0);
            work.Paragraph = (byte)level;
            work.Explicit();
            work.ResolveSequences();
            return level;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(ints);
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>
    /// Rule L1 on the line <paramref name="levels"/> (resolved levels, without <see cref="ParagraphBit"/>) whose classes are
    /// <paramref name="classes"/>: segment and paragraph separators, and the whitespace and isolate controls before them or at
    /// the line end (with the characters X9 removed among them), take the paragraph level.
    /// </summary>
    public static void ResetWhitespace(ReadOnlySpan<byte> classes, int paragraphLevel, Span<byte> levels)
    {
        ResetBefore(classes, classes.Length, paragraphLevel, levels);
        for (int index = 0; index < classes.Length; index++)
        {
            if ((BidiClass)classes[index] is S or B)
            {
                levels[index] = (byte)paragraphLevel;
                ResetBefore(classes, index, paragraphLevel, levels);
            }
        }
    }

    private static void ResetBefore(ReadOnlySpan<byte> classes, int end, int paragraphLevel, Span<byte> levels)
    {
        for (int index = end - 1; index >= 0 && IsWhitespaceForL1((BidiClass)classes[index]); index--)
        {
            levels[index] = (byte)paragraphLevel;
        }
    }

    /// <summary>
    /// Rule L2: the visual order of the units whose levels are <paramref name="levels"/>, as indexes into them from left to
    /// right. Reverses, from the highest level down to the lowest odd level, every run at that level or higher.
    /// </summary>
    public static void Reorder(ReadOnlySpan<byte> levels, Span<int> order)
    {
        for (int index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }

        (int lowestOdd, int highest) = LevelRange(levels);
        for (int level = highest; level >= lowestOdd; level--)
        {
            for (int index = 0; index < order.Length; index++)
            {
                if (levels[order[index]] < level)
                {
                    continue;
                }

                int end = index;
                while (end + 1 < order.Length && levels[order[end + 1]] >= level)
                {
                    end++;
                }

                order[index..(end + 1)].Reverse();
                index = end;
            }
        }
    }

    /// <summary>The lowest odd level and the highest level among <paramref name="levels"/> (lowest odd above highest: none).</summary>
    public static (int LowestOdd, int Highest) LevelRange(ReadOnlySpan<byte> levels)
    {
        int lowestOdd = MaxDepth + 2;
        int highest = 0;
        foreach (byte level in levels)
        {
            highest = Math.Max(highest, level);
            if ((level & 1) != 0)
            {
                lowestOdd = Math.Min(lowestOdd, level);
            }
        }

        return (lowestOdd, highest);
    }

    /// <summary>Whether X9 removes a character of this class: embedding and override controls, PDF and boundary neutrals.</summary>
    public static bool IsRemovedByX9(BidiClass value) => value is RLE or LRE or RLO or LRO or PDF or BN;

    private static bool IsWhitespaceForL1(BidiClass value) => value is WS or FSI or LRI or RLI or PDI || IsRemovedByX9(value);

    private static bool IsIsolateInitiator(BidiClass value) => value is LRI or RLI or FSI;

    private static bool IsNeutralOrIsolate(BidiClass value) => value is B or S or WS or ON or LRI or RLI or FSI or PDI;

    /// <summary>The canonical equivalent of a bracket for BD16: U+2329 and U+232A are U+3008 and U+3009.</summary>
    private static int Canonical(int bracket) => bracket switch
    {
        0x2329 => 0x3008,
        0x232A => 0x3009,
        _ => bracket,
    };

    /// <summary>The state of one paragraph's resolution, over pooled buffers.</summary>
    private ref struct Work
    {
        private readonly ReadOnlySpan<byte> _initial;
        private readonly ReadOnlySpan<char> _text;
        private readonly Span<byte> _levels;

        /// <summary>The matching PDI of each isolate initiator and the initiator of each matched PDI (BD9), -1 for none.</summary>
        private readonly Span<int> _match;

        /// <summary>The level run of each unit not removed by X9.</summary>
        private readonly Span<int> _runOf;

        /// <summary>The first and last unit of each level run.</summary>
        private readonly Span<int> _runStart;
        private readonly Span<int> _runEnd;

        /// <summary>The units of the isolating run sequence being resolved, then the open and close positions of its bracket pairs.</summary>
        private readonly Span<int> _sequence;
        private readonly Span<int> _pairs;

        /// <summary>The current character types (W1 onwards) and the explicit embedding levels of X1 to X8.</summary>
        private readonly Span<byte> _types;
        private readonly Span<byte> _embedding;

        public Work(ReadOnlySpan<byte> classes, ReadOnlySpan<char> text, Span<byte> levels, Span<int> ints, Span<byte> bytes)
        {
            int length = classes.Length;
            _initial = classes;
            _text = text;
            _levels = levels;
            _match = ints[..length];
            _runOf = ints.Slice(length, length);
            _runStart = ints.Slice(length * 2, length);
            _runEnd = ints.Slice(length * 3, length);
            _sequence = ints.Slice(length * 4, length);
            _pairs = ints.Slice(length * 5, length);
            _types = bytes[..length];
            _embedding = bytes.Slice(length, length);
            classes.CopyTo(_types);
        }

        public byte Paragraph { get; set; }

        /// <summary>BD9: pairs isolate initiators with their PDIs (isolates nest; embeddings do not affect them).</summary>
        public readonly void MatchIsolates()
        {
            _match.Fill(-1);
            Span<int> open = _sequence;
            int depth = 0;
            for (int index = 0; index < _initial.Length; index++)
            {
                var value = (BidiClass)_initial[index];
                if (IsIsolateInitiator(value))
                {
                    open[depth++] = index;
                }
                else if (value == PDI && depth > 0)
                {
                    int initiator = open[--depth];
                    _match[initiator] = index;
                    _match[index] = initiator;
                }
            }
        }

        /// <summary>
        /// P2 and P3 over <paramref name="start"/> to <paramref name="end"/>: 1 when the first strong character skipping isolates is
        /// R or AL, 0 when it is L, <paramref name="fallback"/> when there is none.
        /// </summary>
        public readonly int FirstStrong(int start, int end, int fallback)
        {
            for (int index = start; index < end; index++)
            {
                var value = (BidiClass)_initial[index];
                if (value == L)
                {
                    return 0;
                }

                if (value is R or AL)
                {
                    return 1;
                }

                if (IsIsolateInitiator(value))
                {
                    if (_match[index] < 0)
                    {
                        break;
                    }

                    index = _match[index];
                }
            }

            return fallback;
        }

        /// <summary>X1 to X9: the explicit levels and the overridden types; removed characters become BN.</summary>
        public readonly void Explicit()
        {
            Span<byte> stackLevels = stackalloc byte[MaxDepth + 2];
            Span<byte> stackOverrides = stackalloc byte[MaxDepth + 2];
            Span<bool> stackIsolates = stackalloc bool[MaxDepth + 2];
            int depth = 1;
            stackLevels[0] = Paragraph;
            stackOverrides[0] = (byte)ON;
            stackIsolates[0] = false;
            int overflowIsolates = 0;
            int overflowEmbeddings = 0;
            int validIsolates = 0;
            for (int index = 0; index < _initial.Length; index++)
            {
                var value = (BidiClass)_initial[index];
                byte current = stackLevels[depth - 1];
                switch (value)
                {
                    case RLE or LRE or RLO or LRO:
                        {
                            // X2 to X5.
                            int next = value is RLE or RLO ? (current + 1) | 1 : (current + 2) & ~1;
                            if (next <= MaxDepth && overflowIsolates == 0 && overflowEmbeddings == 0)
                            {
                                stackLevels[depth] = (byte)next;
                                stackOverrides[depth] = (byte)(value == RLO ? R : value == LRO ? L : ON);
                                stackIsolates[depth] = false;
                                depth++;
                            }
                            else if (overflowIsolates == 0)
                            {
                                overflowEmbeddings++;
                            }

                            _embedding[index] = current;
                            _types[index] = (byte)BN;
                            break;
                        }

                    case RLI or LRI or FSI:
                        {
                            // X5a to X5c.
                            _embedding[index] = current;
                            if (stackOverrides[depth - 1] != (byte)ON)
                            {
                                _types[index] = stackOverrides[depth - 1];
                            }

                            bool rightToLeft = value == RLI
                                || (value == FSI && FirstStrong(index + 1, _match[index] >= 0 ? _match[index] : _initial.Length, 0) == 1);
                            int next = rightToLeft ? (current + 1) | 1 : (current + 2) & ~1;
                            if (next <= MaxDepth && overflowIsolates == 0 && overflowEmbeddings == 0)
                            {
                                validIsolates++;
                                stackLevels[depth] = (byte)next;
                                stackOverrides[depth] = (byte)ON;
                                stackIsolates[depth] = true;
                                depth++;
                            }
                            else
                            {
                                overflowIsolates++;
                            }

                            break;
                        }

                    case PDI:
                        {
                            // X6a.
                            if (overflowIsolates > 0)
                            {
                                overflowIsolates--;
                            }
                            else if (validIsolates > 0)
                            {
                                overflowEmbeddings = 0;
                                while (!stackIsolates[depth - 1])
                                {
                                    depth--;
                                }

                                depth--;
                                validIsolates--;
                            }

                            _embedding[index] = stackLevels[depth - 1];
                            if (stackOverrides[depth - 1] != (byte)ON)
                            {
                                _types[index] = stackOverrides[depth - 1];
                            }

                            break;
                        }

                    case PDF:
                        {
                            // X7 (nothing while an overflow isolate is open).
                            if (overflowIsolates == 0 && overflowEmbeddings > 0)
                            {
                                overflowEmbeddings--;
                            }
                            else if (overflowIsolates == 0 && !stackIsolates[depth - 1] && depth >= 2)
                            {
                                depth--;
                            }

                            _embedding[index] = current;
                            _types[index] = (byte)BN;
                            break;
                        }

                    case B:
                        // X8.
                        _embedding[index] = Paragraph;
                        break;

                    case BN:
                        _embedding[index] = current;
                        break;

                    default:
                        // X6.
                        _embedding[index] = current;
                        if (stackOverrides[depth - 1] != (byte)ON)
                        {
                            _types[index] = stackOverrides[depth - 1];
                        }

                        break;
                }
            }
        }

        /// <summary>X10: forms each isolating run sequence from the level runs and resolves it; then levels the removed characters.</summary>
        public readonly void ResolveSequences()
        {
            // The level runs over the characters X9 keeps.
            int runs = 0;
            int previous = -1;
            for (int index = 0; index < _initial.Length; index++)
            {
                if (IsRemovedByX9((BidiClass)_initial[index]))
                {
                    _runOf[index] = -1;
                    continue;
                }

                if (previous < 0 || _embedding[index] != _embedding[previous])
                {
                    _runStart[runs++] = index;
                }

                _runEnd[runs - 1] = index;
                _runOf[index] = runs - 1;
                previous = index;
            }

            for (int run = 0; run < runs; run++)
            {
                int first = _runStart[run];
                if ((BidiClass)_initial[first] == PDI && _match[first] >= 0 && _runEnd[_runOf[_match[first]]] == _match[first])
                {
                    // Continues the sequence of its isolate initiator.
                    continue;
                }

                int length = 0;
                int current = run;
                while (true)
                {
                    for (int index = _runStart[current]; index <= _runEnd[current]; index++)
                    {
                        if (_runOf[index] == current)
                        {
                            _sequence[length++] = index;
                        }
                    }

                    int last = _runEnd[current];
                    if (!IsIsolateInitiator((BidiClass)_initial[last]) || _match[last] < 0)
                    {
                        break;
                    }

                    current = _runOf[_match[last]];
                }

                ResolveSequence(_sequence[..length]);
            }

            for (int index = 0; index < _initial.Length; index++)
            {
                if (IsRemovedByX9((BidiClass)_initial[index]))
                {
                    _levels[index] = index > 0 ? _levels[index - 1] : Paragraph;
                }
            }
        }

        private readonly void ResolveSequence(ReadOnlySpan<int> sequence)
        {
            int first = sequence[0];
            int last = sequence[^1];
            byte level = _embedding[first];

            int before = first - 1;
            while (before >= 0 && IsRemovedByX9((BidiClass)_initial[before]))
            {
                before--;
            }

            int beforeLevel = before >= 0 ? _embedding[before] : Paragraph;
            int afterLevel;
            if (IsIsolateInitiator((BidiClass)_initial[last]))
            {
                afterLevel = Paragraph;
            }
            else
            {
                int after = last + 1;
                while (after < _initial.Length && IsRemovedByX9((BidiClass)_initial[after]))
                {
                    after++;
                }

                afterLevel = after < _initial.Length ? _embedding[after] : Paragraph;
            }

            var sos = (Math.Max(beforeLevel, level) & 1) != 0 ? R : L;
            var eos = (Math.Max(afterLevel, level) & 1) != 0 ? R : L;
            ResolveWeak(sequence, sos);
            ResolveBrackets(sequence, sos, level);
            ResolveNeutral(sequence, sos, eos, level);

            // I1 and I2.
            foreach (int index in sequence)
            {
                var value = (BidiClass)_types[index];
                int resolved = level;
                if ((level & 1) == 0)
                {
                    resolved += value == R ? 1 : value is AN or EN ? 2 : 0;
                }
                else if (value is L or EN or AN)
                {
                    resolved++;
                }

                _levels[index] = (byte)resolved;
            }
        }

        /// <summary>W1 to W7.</summary>
        private readonly void ResolveWeak(ReadOnlySpan<int> sequence, BidiClass sos)
        {
            // W1: a nonspacing mark takes the type before it (ON after an isolate initiator or PDI, sos at the start).
            BidiClass preceding = sos;
            foreach (int index in sequence)
            {
                var value = (BidiClass)_types[index];
                if (value == NSM)
                {
                    _types[index] = (byte)preceding;
                }
                else
                {
                    preceding = value is LRI or RLI or FSI or PDI ? ON : value;
                }
            }

            // W2 and W3: European numbers after Arabic letters are Arabic numbers; Arabic letters are R.
            BidiClass strong = sos;
            foreach (int index in sequence)
            {
                var value = (BidiClass)_types[index];
                if (value is L or R or AL)
                {
                    strong = value;
                }
                else if (value == EN && strong == AL)
                {
                    _types[index] = (byte)AN;
                }
            }

            foreach (int index in sequence)
            {
                if (_types[index] == (byte)AL)
                {
                    _types[index] = (byte)R;
                }
            }

            // W4: a single separator between two numbers of the same type joins them.
            for (int position = 1; position < sequence.Length - 1; position++)
            {
                var value = (BidiClass)_types[sequence[position]];
                if (value is not (ES or CS))
                {
                    continue;
                }

                var before = (BidiClass)_types[sequence[position - 1]];
                var after = (BidiClass)_types[sequence[position + 1]];
                if (before == EN && after == EN)
                {
                    _types[sequence[position]] = (byte)EN;
                }
                else if (value == CS && before == AN && after == AN)
                {
                    _types[sequence[position]] = (byte)AN;
                }
            }

            // W5: terminators next to European numbers are European numbers.
            for (int position = 0; position < sequence.Length; position++)
            {
                if (_types[sequence[position]] != (byte)ET)
                {
                    continue;
                }

                int end = position;
                while (end + 1 < sequence.Length && _types[sequence[end + 1]] == (byte)ET)
                {
                    end++;
                }

                bool number = (position > 0 && _types[sequence[position - 1]] == (byte)EN)
                    || (end + 1 < sequence.Length && _types[sequence[end + 1]] == (byte)EN);
                if (number)
                {
                    for (int terminator = position; terminator <= end; terminator++)
                    {
                        _types[sequence[terminator]] = (byte)EN;
                    }
                }

                position = end;
            }

            // W6: the remaining separators and terminators are neutral; W7: European numbers after L are L.
            strong = sos;
            foreach (int index in sequence)
            {
                var value = (BidiClass)_types[index];
                if (value is ES or ET or CS)
                {
                    _types[index] = (byte)ON;
                }
                else if (value is L or R)
                {
                    strong = value;
                }
                else if (value == EN && strong == L)
                {
                    _types[index] = (byte)L;
                }
            }
        }

        /// <summary>N0: paired brackets take the embedding direction, or the direction of the context they enclose and follow.</summary>
        private readonly void ResolveBrackets(ReadOnlySpan<int> sequence, BidiClass sos, int level)
        {
            if (_text.IsEmpty)
            {
                return;
            }

            // BD16: the bracket pairs, by a stack of 63 openings; a full stack stops the search.
            Span<int> stackPositions = stackalloc int[BracketStackSize];
            Span<int> stackBrackets = stackalloc int[BracketStackSize];
            int depth = 0;
            int pairs = 0;
            Span<int> pairOpen = _pairs;
            int half = _pairs.Length / 2;
            for (int position = 0; position < sequence.Length; position++)
            {
                int index = sequence[position];
                if (_types[index] != (byte)ON)
                {
                    continue;
                }

                int kind = BidiData.Bracket(_text[index], out int paired);
                if (kind == BidiData.OpenBracket)
                {
                    if (depth == BracketStackSize)
                    {
                        break;
                    }

                    stackPositions[depth] = position;
                    stackBrackets[depth] = Canonical(paired);
                    depth++;
                }
                else if (kind == BidiData.CloseBracket)
                {
                    int bracket = Canonical(_text[index]);
                    for (int entry = depth - 1; entry >= 0; entry--)
                    {
                        if (stackBrackets[entry] == bracket)
                        {
                            // Openings in the first half of the buffer, closings in the second: a pair takes two units.
                            pairOpen[pairs] = stackPositions[entry];
                            pairOpen[half + pairs] = position;
                            pairs++;
                            depth = entry;
                            break;
                        }
                    }
                }
            }

            if (pairs == 0)
            {
                return;
            }

            Span<int> opens = pairOpen[..pairs];
            Span<int> closes = pairOpen.Slice(half, pairs);
            opens.Sort(closes);
            var embedding = (level & 1) != 0 ? R : L;
            for (int pair = 0; pair < pairs; pair++)
            {
                int open = opens[pair];
                int close = closes[pair];
                BidiClass inside = ON;
                for (int position = open + 1; position < close; position++)
                {
                    BidiClass strong = Strong((BidiClass)_types[sequence[position]]);
                    if (strong == embedding)
                    {
                        inside = embedding;
                        break;
                    }

                    if (strong != ON)
                    {
                        inside = strong;
                    }
                }

                if (inside == ON)
                {
                    continue;
                }

                BidiClass resolved = embedding;
                if (inside != embedding)
                {
                    // N0 c: the opposite direction inside; it holds when the context before the opening bracket has it too.
                    BidiClass context = sos;
                    for (int position = open - 1; position >= 0; position--)
                    {
                        BidiClass strong = Strong((BidiClass)_types[sequence[position]]);
                        if (strong != ON)
                        {
                            context = strong;
                            break;
                        }
                    }

                    resolved = context == inside ? inside : embedding;
                }

                SetBracket(sequence, open, resolved);
                SetBracket(sequence, close, resolved);
            }
        }

        /// <summary>Sets a bracket's type, and that of the nonspacing marks that originally followed it.</summary>
        private readonly void SetBracket(ReadOnlySpan<int> sequence, int position, BidiClass value)
        {
            _types[sequence[position]] = (byte)value;
            for (int next = position + 1; next < sequence.Length && _initial[sequence[next]] == (byte)NSM; next++)
            {
                _types[sequence[next]] = (byte)value;
            }
        }

        /// <summary>The strong direction a type counts as in N0, N1 and N2 (EN and AN as R); ON when none.</summary>
        private static BidiClass Strong(BidiClass value) => value switch
        {
            L => L,
            R or EN or AN => R,
            _ => ON,
        };

        /// <summary>N1 and N2: runs of neutrals take the direction around them when both sides agree, else the embedding direction.</summary>
        private readonly void ResolveNeutral(ReadOnlySpan<int> sequence, BidiClass sos, BidiClass eos, int level)
        {
            var embedding = (level & 1) != 0 ? R : L;
            for (int position = 0; position < sequence.Length; position++)
            {
                if (!IsNeutralOrIsolate((BidiClass)_types[sequence[position]]))
                {
                    continue;
                }

                int end = position;
                while (end + 1 < sequence.Length && IsNeutralOrIsolate((BidiClass)_types[sequence[end + 1]]))
                {
                    end++;
                }

                BidiClass before = position == 0 ? sos : Strong((BidiClass)_types[sequence[position - 1]]);
                BidiClass after = end + 1 == sequence.Length ? eos : Strong((BidiClass)_types[sequence[end + 1]]);
                BidiClass resolved = before == after ? before : embedding;
                for (int neutral = position; neutral <= end; neutral++)
                {
                    _types[sequence[neutral]] = (byte)resolved;
                }

                position = end;
            }
        }
    }
}
