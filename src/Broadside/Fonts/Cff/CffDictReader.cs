using System.Buffers.Binary;
using System.Globalization;
using Broadside.Fonts.CharStrings;

namespace Broadside.Fonts.Cff;

/// <summary>
/// Reads a CFF DICT one operator at a time: the operands before each operator are collected into an inline buffer, so reading
/// allocates nothing. Damage (reserved bytes, a number cut short, too many operands) is repaired and recorded in <see cref="Problem"/>.
/// </summary>
/// <remarks>
/// Adobe Technical Note #5176 §4, Tables 3-6 (p.9-12): operators 0-21 (12 escapes to a two-byte operator <c>12 x</c>), integer
/// operands 28 (int16), 29 (int32) and 32-254, real operands 30 (packed decimal nibbles), at most 48 operands per operator.
/// </remarks>
internal ref struct CffDictReader
{
    private readonly ReadOnlySpan<byte> _dict;
    private CharStringStack _operands;
    private int _position;

    /// <summary>Initializes a new instance of the <see cref="CffDictReader"/> struct over a DICT's bytes.</summary>
    public CffDictReader(ReadOnlySpan<byte> dict)
    {
        _dict = dict;
    }

    /// <summary>Gets the operator just read: 0-21, or <c>1200 + x</c> for the escaped operator <c>12 x</c>.</summary>
    public int Operator { get; private set; }

    /// <summary>Gets the number of operands of the operator just read.</summary>
    public int OperandCount { get; private set; }

    /// <summary>Gets a description of the first damage met, or <see langword="null"/>.</summary>
    public string? Problem { get; private set; }

    /// <summary>Gets an operand of the operator just read (0 when out of range).</summary>
    public readonly double this[int index] => (uint)index < (uint)OperandCount ? _operands[index] : 0;

    /// <summary>Reads the next operator and its operands.</summary>
    /// <returns><see langword="false"/> at the end of the DICT (operands with no operator after them are dropped).</returns>
    public bool Next()
    {
        OperandCount = 0;
        while (_position < _dict.Length)
        {
            byte b0 = _dict[_position++];
            if (b0 <= 21)
            {
                if (b0 == 12)
                {
                    if (_position >= _dict.Length)
                    {
                        Problem ??= "ends inside an escaped operator";
                        return false;
                    }

                    Operator = 1200 + _dict[_position++];
                }
                else
                {
                    Operator = b0;
                }

                return true;
            }

            double value;
            switch (b0)
            {
                case 28:
                    if (_position + 2 > _dict.Length)
                    {
                        return Truncated();
                    }

                    value = BinaryPrimitives.ReadInt16BigEndian(_dict[_position..]);
                    _position += 2;
                    break;
                case 29:
                    if (_position + 4 > _dict.Length)
                    {
                        return Truncated();
                    }

                    value = BinaryPrimitives.ReadInt32BigEndian(_dict[_position..]);
                    _position += 4;
                    break;
                case 30:
                    value = ReadReal();
                    break;
                case >= 32 and <= 246:
                    value = b0 - 139;
                    break;
                case >= 247 and <= 254:
                    if (_position >= _dict.Length)
                    {
                        return Truncated();
                    }

                    int b1 = _dict[_position++];
                    value = b0 <= 250 ? ((b0 - 247) * 256) + b1 + 108 : (-(b0 - 251) * 256) - b1 - 108;
                    break;
                default:
                    Problem ??= string.Create(CultureInfo.InvariantCulture, $"has the reserved byte {b0}, which is skipped");
                    continue;
            }

            if (OperandCount == CharStringLimits.MaxArguments)
            {
                Problem ??= "gives an operator more than 48 operands; the extra ones are dropped";
                continue;
            }

            _operands[OperandCount++] = value;
        }

        return false;
    }

    private bool Truncated()
    {
        Problem ??= "ends inside a number";
        _position = _dict.Length;
        return false;
    }

    /// <summary>A real operand: nibbles 0-9 digits, a '.', b 'E', c 'E-', e '-', f end (5176 Table 5).</summary>
    private double ReadReal()
    {
        Span<char> text = stackalloc char[64];
        int length = 0;
        bool overflow = false;
        while (_position < _dict.Length)
        {
            byte b = _dict[_position++];
            for (int shift = 4; shift >= 0; shift -= 4)
            {
                int nibble = (b >> shift) & 0xF;
                if (nibble == 0xF)
                {
                    return Parse(text[..length], overflow);
                }

                ReadOnlySpan<char> piece = nibble switch
                {
                    <= 9 => [(char)('0' + nibble)],
                    0xA => ".",
                    0xB => "E",
                    0xC => "E-",
                    0xE => "-",
                    _ => default,
                };
                if (nibble == 0xD)
                {
                    Problem ??= "has a real number with the reserved nibble d";
                }

                if (length + piece.Length > text.Length)
                {
                    overflow = true;
                    continue;
                }

                piece.CopyTo(text[length..]);
                length += piece.Length;
            }
        }

        Problem ??= "ends inside a real number";
        return Parse(text[..length], overflow);
    }

    private double Parse(scoped ReadOnlySpan<char> text, bool overflow)
    {
        if (!overflow && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
        {
            return value;
        }

        Problem ??= "has a real number that is not a number; 0 is used";
        return 0;
    }
}
