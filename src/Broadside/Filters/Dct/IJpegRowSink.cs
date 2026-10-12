using System.Buffers;
using Broadside.Images;

namespace Broadside.Filters.Dct;

/// <summary>Where a DCT decoder writes its output rows, top to bottom (a struct, so the decoder is specialized per sink).</summary>
internal interface IJpegRowSink
{
    /// <summary>Returns the row <paramref name="y"/> to fill: width x components bytes.</summary>
    /// <param name="y">The row.</param>
    /// <returns>The row.</returns>
    Span<byte> BeginRow(int y);

    /// <summary>Commits the row returned by <see cref="BeginRow"/>.</summary>
    void EndRow();
}

/// <summary>Writes rows into the <see cref="IBufferWriter{T}"/> of the plain filter path.</summary>
internal readonly struct BufferWriterRowSink(IBufferWriter<byte> writer, int stride) : IJpegRowSink
{
    public Span<byte> BeginRow(int y) => writer.GetSpan(stride)[..stride];

    public void EndRow() => writer.Advance(stride);
}

/// <summary>Writes rows into a <see cref="DecodedImageBuilder"/>.</summary>
internal readonly struct ImageRowSink(DecodedImageBuilder image) : IJpegRowSink
{
    public Span<byte> BeginRow(int y) => image.GetRow(y);

    public void EndRow()
    {
    }
}
