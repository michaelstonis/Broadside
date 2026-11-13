using System.Globalization;
using System.Text;

namespace Broadside.Tests.Document;

/// <summary>
/// A seekable, read-only stream over a PDF file that is never stored anywhere: a list of literal pieces (header, dictionaries,
/// cross-reference table) and runs of filler bytes (stream data), each byte produced when it is read. A file of any length costs
/// the memory of its literal pieces only, so tests of multi-gigabyte files need neither the disk nor the memory (issue #237).
/// </summary>
internal sealed class SyntheticPdfStream : Stream
{
    private readonly long[] _starts;
    private readonly byte[]?[] _literals;
    private readonly long[] _lengths;
    private readonly byte _filler;
    private long _position;

    private SyntheticPdfStream(List<(byte[]? Literal, long Length)> pieces, byte filler)
    {
        _starts = new long[pieces.Count];
        _literals = new byte[]?[pieces.Count];
        _lengths = new long[pieces.Count];
        long position = 0;
        for (int index = 0; index < pieces.Count; index++)
        {
            _starts[index] = position;
            _literals[index] = pieces[index].Literal;
            _lengths[index] = pieces[index].Length;
            position += pieces[index].Length;
        }

        Length = position;
        _filler = filler;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    /// <summary>
    /// A file of <paramref name="pages"/> Letter pages, each with a content stream of <paramref name="contentLength"/> bytes: a
    /// comment naming the page, then <paramref name="filler"/> bytes. Objects are written as Broadside's writer writes them, and the
    /// classic cross-reference table lists every one (§7.5.4; ten-digit offsets reach past 4 GiB).
    /// </summary>
    /// <param name="pages">The number of pages.</param>
    /// <param name="contentLength">The length of each content stream's data.</param>
    /// <param name="filler">The byte that fills each content stream after its comment.</param>
    /// <returns>The stream, at position 0.</returns>
    public static SyntheticPdfStream WithPages(int pages, long contentLength, byte filler = (byte)' ')
    {
        var pieces = new List<(byte[]? Literal, long Length)>();
        long position = 0;
        int size = 3 + (2 * pages);
        long[] offsets = new long[size];
        void Literal(string text)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(text);
            pieces.Add((bytes, bytes.Length));
            position += bytes.Length;
        }

        Literal("%PDF-1.7\n%âãÏÓ\n");
        offsets[1] = position;
        Literal("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = position;
        var kids = new StringBuilder();
        for (int page = 0; page < pages; page++)
        {
            kids.Append(CultureInfo.InvariantCulture, $"{3 + (2 * page)} 0 R ");
        }

        Literal($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pages} >>\nendobj\n");
        for (int page = 0; page < pages; page++)
        {
            int number = 3 + (2 * page);
            offsets[number] = position;
            Literal($"{number} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents {number + 1} 0 R >>\nendobj\n");
            offsets[number + 1] = position;
            string comment = $"% page {page}\n";
            Literal($"{number + 1} 0 obj\n<< /Length {contentLength} >>\nstream\n{comment}");
            pieces.Add((null, contentLength - comment.Length));
            position += contentLength - comment.Length;
            Literal("\nendstream\nendobj\n");
        }

        long xref = position;
        var table = new StringBuilder();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {size}\n0000000000 65535 f\r\n");
        for (int number = 1; number < size; number++)
        {
            table.Append(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n\r\n");
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Literal(table.ToString());
        return new SyntheticPdfStream(pieces, filler);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length && _position < Length)
        {
            int piece = Array.BinarySearch(_starts, _position);
            piece = piece >= 0 ? piece : ~piece - 1;
            long within = _position - _starts[piece];
            int length = (int)Math.Min(buffer.Length - total, _lengths[piece] - within);
            Span<byte> target = buffer.Slice(total, length);
            if (_literals[piece] is { } literal)
            {
                literal.AsSpan((int)within, length).CopyTo(target);
            }
            else
            {
                target.Fill(_filler);
            }

            total += length;
            _position += length;
        }

        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => Length + offset,
        };
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// A write-only stream that keeps nothing but what a test asks about: how many bytes were written, the largest single write, the
/// last bytes and, optionally, every byte.
/// </summary>
internal sealed class MeasuringStream(bool keep = false, int tailLength = 4096) : Stream
{
    private readonly MemoryStream? _kept = keep ? new MemoryStream() : null;
    private readonly byte[] _tail = new byte[tailLength];
    private long _length;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _length;

    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    /// <summary>Gets the length of the largest single write.</summary>
    public int LargestWrite { get; private set; }

    /// <summary>Gets every byte written, when the stream was created to keep them.</summary>
    public byte[] Kept => _kept?.ToArray() ?? throw new InvalidOperationException("The stream keeps no bytes.");

    /// <summary>Gets the last bytes written, up to the tail length.</summary>
    public ReadOnlySpan<byte> Tail => _tail.AsSpan((int)Math.Max(0, _tail.Length - _length));

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        LargestWrite = Math.Max(LargestWrite, buffer.Length);
        _kept?.Write(buffer);
        if (buffer.Length >= _tail.Length)
        {
            buffer[^_tail.Length..].CopyTo(_tail);
        }
        else
        {
            _tail.AsSpan(buffer.Length).CopyTo(_tail);
            buffer.CopyTo(_tail.AsSpan(_tail.Length - buffer.Length));
        }

        _length += buffer.Length;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _kept?.Dispose();
        base.Dispose(disposing);
    }
}
