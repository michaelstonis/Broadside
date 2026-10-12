using System.Buffers;
using System.Globalization;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// The content stream writer: a reusable byte buffer that content stream syntax is written into, either copied as it is (the
/// rewrite's untouched bytes and edits, <see cref="ContentEditor"/>) or built operand by operand (generated appearance streams):
/// operands followed by single spaces, each operator ending its line.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.2 (content stream syntax: operands, then the operator) and §7.3 (operand syntax; reals are written without
/// exponents, §7.3.3). Clearing keeps the buffer, so a writer reused for the next stream allocates nothing once it is large enough.
/// </remarks>
internal sealed class ContentWriter : IBufferWriter<byte>
{
    private byte[] _buffer = [];
    private int _length;

    /// <summary>Gets the content written since the last <see cref="Clear"/>.</summary>
    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);

    /// <summary>Gets the number of bytes written since the last <see cref="Clear"/>.</summary>
    public int Length => _length;

    /// <summary>Gets the last byte written, or -1 when nothing has been.</summary>
    public int Last => _length == 0 ? -1 : _buffer[_length - 1];

    /// <summary>Forgets what was written, keeping the buffer.</summary>
    public void Clear() => _length = 0;

    /// <summary>Makes room for <paramref name="length"/> bytes in all, so writing up to that many grows nothing.</summary>
    public void Reserve(int length)
    {
        if (length > _buffer.Length)
        {
            Array.Resize(ref _buffer, (int)Math.Min(Array.MaxLength, Math.Max(length, Math.Max(256L, _buffer.Length * 2L))));
        }
    }

    /// <summary>Writes one byte as it is.</summary>
    public void Write(byte value)
    {
        Reserve(_length + 1);
        _buffer[_length++] = value;
    }

    /// <summary>Writes bytes as they are.</summary>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        Reserve(_length + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
    }

    /// <summary>Writes a number operand, rounded to four decimal places, without an exponent (§7.3.3).</summary>
    public ContentWriter Number(double value)
    {
        double rounded = Math.Round(value, 4);
        if (rounded == 0 || !double.IsFinite(rounded))
        {
            rounded = 0;
        }

        int written;
        for (int size = 32; !rounded.TryFormat(GetSpan(size), out written, "0.####", CultureInfo.InvariantCulture); size *= 2)
        {
        }

        Advance(written);
        return Space();
    }

    /// <summary>Writes a name operand.</summary>
    public ContentWriter Name(CosName name)
    {
        CosWriter.Write(name, this);
        return Space();
    }

    /// <summary>Writes a string operand, literal or hexadecimal.</summary>
    public ContentWriter String(byte[] bytes, bool hexadecimal)
    {
        CosWriter.Write(new CosString(bytes, hexadecimal), this);
        return Space();
    }

    /// <summary>Writes an operator and ends the line.</summary>
    public ContentWriter Operator(string name)
    {
        Span<byte> span = GetSpan(name.Length + 1);
        for (int index = 0; index < name.Length; index++)
        {
            span[index] = (byte)name[index];
        }

        span[name.Length] = (byte)'\n';
        Advance(name.Length + 1);
        return this;
    }

    /// <summary>Writes operators as they are, then ends the line; nothing for none.</summary>
    public ContentWriter Verbatim(ReadOnlySpan<byte> operators)
    {
        if (!operators.IsEmpty)
        {
            Write(operators);
            Write((byte)'\n');
        }

        return this;
    }

    /// <summary>Writes the four numbers and <c>re</c> of a rectangle (§8.5.2.1).</summary>
    public ContentWriter Rectangle(double x, double y, double width, double height) =>
        Number(x).Number(y).Number(width).Number(height).Operator("re");

    /// <summary>Returns a copy of what was written.</summary>
    public byte[] ToArray() => Written.ToArray();

    /// <inheritdoc/>
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _buffer.Length - _length);
        _length += count;
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Reserve(_length + Math.Max(sizeHint, 1));
        return _buffer.AsMemory(_length);
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Reserve(_length + Math.Max(sizeHint, 1));
        return _buffer.AsSpan(_length);
    }

    private ContentWriter Space()
    {
        Write((byte)' ');
        return this;
    }
}
