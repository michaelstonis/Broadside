using System.Buffers;
using System.Globalization;
using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>
/// Writes the operators of a generated appearance stream: operands separated by single spaces, one operator per line.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.2 (content stream syntax) and §7.3 (operand syntax; reals without exponents, §7.3.3). Internal and minimal
/// (issue #167): the content stream rewriting work (issue #147) owns the general content writer and may absorb this one.
/// </remarks>
internal sealed class AppearanceContentWriter
{
    private readonly ArrayBufferWriter<byte> _output = new();

    /// <summary>Gets the content written so far.</summary>
    public ReadOnlyMemory<byte> Content => _output.WrittenMemory;

    /// <summary>Writes a number, rounded to four decimal places.</summary>
    public AppearanceContentWriter Number(double value)
    {
        double rounded = Math.Round(value, 4);
        if (rounded == 0 || !double.IsFinite(rounded))
        {
            rounded = 0;
        }

        Raw(rounded.ToString("0.####", CultureInfo.InvariantCulture));
        return Space();
    }

    /// <summary>Writes a name operand.</summary>
    public AppearanceContentWriter Name(CosName name)
    {
        CosWriter.Write(name, _output);
        return Space();
    }

    /// <summary>Writes a string operand, literal or hexadecimal.</summary>
    public AppearanceContentWriter String(byte[] bytes, bool hexadecimal)
    {
        CosWriter.Write(new CosString(bytes, hexadecimal), _output);
        return Space();
    }

    /// <summary>Writes an operator and ends the line.</summary>
    public AppearanceContentWriter Operator(string name)
    {
        Raw(name);
        _output.Write("\n"u8);
        return this;
    }

    /// <summary>Writes operators as they are, then ends the line.</summary>
    public AppearanceContentWriter Verbatim(ReadOnlySpan<byte> operators)
    {
        if (!operators.IsEmpty)
        {
            _output.Write(operators);
            _output.Write("\n"u8);
        }

        return this;
    }

    /// <summary>Writes the four numbers and <c>re</c> of a rectangle.</summary>
    public AppearanceContentWriter Rectangle(double x, double y, double width, double height) =>
        Number(x).Number(y).Number(width).Number(height).Operator("re");

    private AppearanceContentWriter Space()
    {
        _output.Write(" "u8);
        return this;
    }

    private void Raw(string text)
    {
        Span<byte> span = _output.GetSpan(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            span[index] = (byte)text[index];
        }

        _output.Advance(text.Length);
    }
}
