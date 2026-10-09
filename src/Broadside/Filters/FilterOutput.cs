using System.Buffers;

namespace Broadside.Filters;

/// <summary>
/// Writes a filter's output into an <see cref="IBufferWriter{T}"/> in chunks, so the decode loop pays a span store per byte and a
/// writer call per chunk. Call <see cref="Flush"/> before returning.
/// </summary>
internal ref struct FilterOutput
{
    private const int ChunkSize = 4096;
    private readonly IBufferWriter<byte> _writer;
    private Span<byte> _span;
    private int _position;

    /// <summary>Initializes a new instance of the <see cref="FilterOutput"/> struct.</summary>
    /// <param name="writer">The destination.</param>
    public FilterOutput(IBufferWriter<byte> writer)
    {
        _writer = writer;
        _span = default;
        _position = 0;
    }

    /// <summary>Writes one byte.</summary>
    /// <param name="value">The byte.</param>
    public void Write(byte value)
    {
        if (_position == _span.Length)
        {
            Grow(1);
        }

        _span[_position++] = value;
    }

    /// <summary>Writes bytes.</summary>
    /// <param name="bytes">The bytes.</param>
    public void Write(scoped ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (_position == _span.Length)
            {
                Grow(1);
            }

            int count = Math.Min(bytes.Length, _span.Length - _position);
            bytes[..count].CopyTo(_span[_position..]);
            _position += count;
            bytes = bytes[count..];
        }
    }

    /// <summary>Writes <paramref name="value"/> <paramref name="count"/> times.</summary>
    /// <param name="value">The byte.</param>
    /// <param name="count">How many times.</param>
    public void WriteRepeated(byte value, int count)
    {
        while (count > 0)
        {
            if (_position == _span.Length)
            {
                Grow(1);
            }

            int run = Math.Min(count, _span.Length - _position);
            _span.Slice(_position, run).Fill(value);
            _position += run;
            count -= run;
        }
    }

    /// <summary>Returns the next <paramref name="count"/> bytes of output to be filled in, in any order.</summary>
    /// <param name="count">How many bytes.</param>
    /// <returns>The bytes, now part of the output.</returns>
    public Span<byte> Reserve(int count)
    {
        if (_span.Length - _position < count)
        {
            Grow(count);
        }

        Span<byte> reserved = _span.Slice(_position, count);
        _position += count;
        return reserved;
    }

    /// <summary>Commits everything written to the underlying writer.</summary>
    public void Flush()
    {
        if (_position > 0)
        {
            _writer.Advance(_position);
        }

        _span = default;
        _position = 0;
    }

    private void Grow(int minimum)
    {
        Flush();
        _span = _writer.GetSpan(Math.Max(minimum, ChunkSize));
    }
}
