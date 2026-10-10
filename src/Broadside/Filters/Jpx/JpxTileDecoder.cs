using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Filters.Jpx;

/// <summary>
/// Decodes one tile into one coefficient buffer per component, wavelet-reconstructed: builds the tile's geometry (resolutions,
/// sub-bands, precincts, code-blocks), reads its packets in progression order (tier-2), decodes every code-block (tier-1), and runs
/// the inverse wavelet transform.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.800 Annex B (B.3 to B.12), Annex D (through <see cref="JpxCodeBlockDecoder"/>), Annex E (reversible reconstruction) and
/// Annex F (through <see cref="JpxWavelet"/>). The geometry is general: any precinct and code-block partition, any number of layers.
/// Progressions LRCP and RLCP (B.12.1.1, B.12.1.2) are read here; the position-driven orders, the 9/7 path and the code-block styles
/// that split segments plug into <see cref="ReadPackets"/>, <see cref="DecodeBlocks"/> and <see cref="JpxCodeBlockDecoder"/>.
/// </para>
/// <para>One instance per decode call; it owns pooled buffers until <see cref="Dispose"/>.</para>
/// </remarks>
internal sealed class JpxTileDecoder : IDisposable
{
    /// <summary>The most code-blocks and precincts one tile may have before it is refused.</summary>
    internal const int MaxStructures = 1 << 21;

    private readonly JpxReporter _reporter;
    private readonly JpxCodeBlockDecoder _blocks = new();
    private JpxChunk[] _chunks = ArrayPool<JpxChunk>.Shared.Rent(64);
    private int _chunkCount;
    private byte[] _joined = ArrayPool<byte>.Shared.Rent(256);
    private int _structures;

    public JpxTileDecoder(JpxReporter reporter) => _reporter = reporter;

    /// <summary>
    /// Decodes <paramref name="data"/> (the tile's packets, its tile-parts joined) into <paramref name="buffers"/>, one rented buffer per
    /// component of <c>Width * Height</c> samples (relative to the tile-component's origin), and returns the tile-components.
    /// </summary>
    /// <returns>The tile-components; <see langword="null"/> when the tile's structure is over the limits.</returns>
    public JpxTileComponent[]? Decode(JpxImageSize size, JpxTileParameters parameters, int tile, ReadOnlySpan<byte> data, int[][] buffers)
    {
        _chunkCount = 0;
        _structures = 0;
        (long tx0, long ty0, long tx1, long ty1) = size.TileBounds(tile);
        var components = new JpxTileComponent[size.Components.Length];
        for (int c = 0; c < components.Length; c++)
        {
            if (Build(size.Components[c], parameters.Components[c], parameters.Quantization[c], tx0, ty0, tx1, ty1) is not { } component)
            {
                return null;
            }

            components[c] = component;
        }

        ReadPackets(parameters.Coding, components, data);
        for (int c = 0; c < components.Length; c++)
        {
            JpxTileComponent component = components[c];
            int[] buffer = buffers[c];
            buffer.AsSpan(0, component.Width * component.Height).Clear();
            DecodeBlocks(component, data, buffer);
            JpxWavelet.Inverse53(component, buffer);
        }

        return components;
    }

    /// <summary>Returns the pooled buffers.</summary>
    public void Dispose()
    {
        _blocks.Dispose();
        ArrayPool<JpxChunk>.Shared.Return(_chunks);
        ArrayPool<byte>.Shared.Return(_joined);
        _chunks = [];
        _joined = [];
    }

    private JpxTileComponent? Build(JpxComponentInfo info, JpxComponentStyle style, JpxQuantization quantization, long tx0, long ty0, long tx1, long ty1)
    {
        long cx0 = JpxMath.CeilDivide(tx0, info.Dx);
        long cy0 = JpxMath.CeilDivide(ty0, info.Dy);
        long cx1 = JpxMath.CeilDivide(tx1, info.Dx);
        long cy1 = JpxMath.CeilDivide(ty1, info.Dy);
        int levels = style.Levels;
        var resolutions = new JpxResolution[levels + 1];
        for (int r = 0; r <= levels; r++)
        {
            int shift = levels - r;
            long rx0 = JpxMath.CeilShift(cx0, shift);
            long ry0 = JpxMath.CeilShift(cy0, shift);
            long rx1 = JpxMath.CeilShift(cx1, shift);
            long ry1 = JpxMath.CeilShift(cy1, shift);
            (int ppx, int ppy) = style.PrecinctExponents(r);
            int wide = rx1 > rx0 ? (int)(JpxMath.CeilShift(rx1, ppx) - JpxMath.FloorShift(rx0, ppx)) : 0;
            int high = ry1 > ry0 ? (int)(JpxMath.CeilShift(ry1, ppy) - JpxMath.FloorShift(ry0, ppy)) : 0;
            int bandPpx = r > 0 ? ppx - 1 : ppx;
            int bandPpy = r > 0 ? ppy - 1 : ppy;
            JpxResolution? lower = r > 0 ? resolutions[r - 1] : null;
            var bands = new JpxBand[r == 0 ? 1 : 3];
            for (int b = 0; b < bands.Length; b++)
            {
                int orientation = r == 0 ? 0 : b + 1;
                int level = r == 0 ? levels : levels - r + 1;
                long xOffset = (orientation & 1) == 0 ? 0 : 1L << (level - 1);
                long yOffset = (orientation >> 1) == 0 ? 0 : 1L << (level - 1);
                long bx0 = JpxMath.CeilShift(cx0 - xOffset, level);
                long by0 = JpxMath.CeilShift(cy0 - yOffset, level);
                long bx1 = JpxMath.CeilShift(cx1 - xOffset, level);
                long by1 = JpxMath.CeilShift(cy1 - yOffset, level);
                int index = r == 0 ? 0 : 1 + (3 * (r - 1)) + b;
                bands[b] = new JpxBand
                {
                    Orientation = orientation,
                    X0 = bx0,
                    Y0 = by0,
                    Width = (int)Math.Max(0, bx1 - bx0),
                    Height = (int)Math.Max(0, by1 - by0),
                    BufferX = (orientation & 1) == 0 ? 0 : lower!.Width,
                    BufferY = (orientation >> 1) == 0 ? 0 : lower!.Height,
                    MagnitudeBits = quantization.GuardBits + quantization.Exponent(index, levels, level) - 1,
                    BlockWidthExponent = Math.Min(style.BlockWidthExponent, bandPpx),
                    BlockHeightExponent = Math.Min(style.BlockHeightExponent, bandPpy),
                };
            }

            if (!Reserve((long)wide * high))
            {
                return null;
            }

            var precinctBands = new JpxPrecinctBand[wide * high * bands.Length];
            long firstX = JpxMath.FloorShift(rx0, ppx);
            long firstY = JpxMath.FloorShift(ry0, ppy);
            for (int k = 0; k < wide * high; k++)
            {
                for (int b = 0; b < bands.Length; b++)
                {
                    if (CreatePrecinctBand(bands[b], firstX + (k % wide), firstY + (k / wide), bandPpx, bandPpy) is not { } precinctBand)
                    {
                        return null;
                    }

                    precinctBands[(k * bands.Length) + b] = precinctBand;
                }
            }

            resolutions[r] = new JpxResolution
            {
                X0 = rx0,
                Y0 = ry0,
                Width = (int)(rx1 - rx0),
                Height = (int)(ry1 - ry0),
                PrecinctsWide = wide,
                PrecinctsHigh = high,
                Bands = bands,
                PrecinctBands = precinctBands,
            };
        }

        return new JpxTileComponent
        {
            X0 = cx0,
            Y0 = cy0,
            Width = (int)(cx1 - cx0),
            Height = (int)(cy1 - cy0),
            Style = style,
            Resolutions = resolutions,
        };
    }

    /// <summary>The code-blocks of precinct (<paramref name="px"/>, <paramref name="py"/>) in <paramref name="band"/> (B.6, B.7).</summary>
    private JpxPrecinctBand? CreatePrecinctBand(JpxBand band, long px, long py, int ppx, int ppy)
    {
        long x0 = Math.Max(px << ppx, band.X0);
        long y0 = Math.Max(py << ppy, band.Y0);
        long x1 = Math.Min((px + 1) << ppx, band.X0 + band.Width);
        long y1 = Math.Min((py + 1) << ppy, band.Y0 + band.Height);
        if (x1 <= x0 || y1 <= y0)
        {
            return new JpxPrecinctBand(0, 0, []);
        }

        int xcb = band.BlockWidthExponent;
        int ycb = band.BlockHeightExponent;
        long firstX = JpxMath.FloorShift(x0, xcb);
        long firstY = JpxMath.FloorShift(y0, ycb);
        int wide = (int)(JpxMath.CeilShift(x1, xcb) - firstX);
        int high = (int)(JpxMath.CeilShift(y1, ycb) - firstY);
        if (!Reserve((long)wide * high))
        {
            return null;
        }

        var blocks = new JpxCodeBlock[wide * high];
        for (int j = 0; j < high; j++)
        {
            for (int i = 0; i < wide; i++)
            {
                long bx = firstX + i;
                long by = firstY + j;
                blocks[(j * wide) + i] = new JpxCodeBlock
                {
                    X0 = (int)(Math.Max(bx << xcb, x0) - band.X0),
                    Y0 = (int)(Math.Max(by << ycb, y0) - band.Y0),
                    X1 = (int)(Math.Min((bx + 1) << xcb, x1) - band.X0),
                    Y1 = (int)(Math.Min((by + 1) << ycb, y1) - band.Y0),
                    LengthBits = 3,
                    FirstChunk = -1,
                    LastChunk = -1,
                };
            }
        }

        return new JpxPrecinctBand(wide, high, blocks);
    }

    private bool Reserve(long count)
    {
        _structures += (int)Math.Min(count, MaxStructures + 1L);
        if (_structures <= MaxStructures)
        {
            return true;
        }

        _reporter.Report(
            DiagnosticCodes.JpxLimitExceeded,
            DiagnosticSeverity.Error,
            $"A JPEG 2000 tile has more than {MaxStructures} precincts and code-blocks; it is not decoded.");
        return false;
    }

    /// <summary>Reads every packet of the tile in its progression order (B.12) until the data ends.</summary>
    private void ReadPackets(JpxCodingStyle coding, JpxTileComponent[] components, ReadOnlySpan<byte> data)
    {
        int maxLevels = 0;
        foreach (JpxTileComponent component in components)
        {
            maxLevels = Math.Max(maxLevels, component.Style.Levels);
        }

        int position = 0;
        bool resolutionFirst = coding.Progression == 1;
        int outer = resolutionFirst ? maxLevels + 1 : coding.Layers;
        int inner = resolutionFirst ? coding.Layers : maxLevels + 1;
        for (int a = 0; a < outer; a++)
        {
            for (int b = 0; b < inner; b++)
            {
                int layer = resolutionFirst ? b : a;
                int r = resolutionFirst ? a : b;
                foreach (JpxTileComponent component in components)
                {
                    if (r > component.Style.Levels)
                    {
                        continue;
                    }

                    JpxResolution resolution = component.Resolutions[r];
                    int precincts = resolution.PrecinctsWide * resolution.PrecinctsHigh;
                    for (int k = 0; k < precincts; k++)
                    {
                        if (!ReadPacket(coding, resolution, k, layer, data, ref position))
                        {
                            return;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Reads one packet: header (B.10) and body. Returns <see langword="false"/> when the data runs out.</summary>
    private bool ReadPacket(JpxCodingStyle coding, JpxResolution resolution, int precinct, int layer, ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length)
        {
            Truncated();
            return false;
        }

        if (coding.MayUseSop && position + 6 <= data.Length && data[position] == 0xFF && data[position + 1] == 0x91)
        {
            position += 6;
        }

        int bandCount = resolution.Bands.Length;
        var reader = new JpxPacketHeaderReader(data, position);
        bool nonEmpty = reader.ReadBit() == 1;
        if (nonEmpty)
        {
            for (int b = 0; b < bandCount; b++)
            {
                JpxPrecinctBand precinctBand = resolution.PrecinctBands[(precinct * bandCount) + b];
                JpxBand band = resolution.Bands[b];
                JpxCodeBlock[] blocks = precinctBand.Blocks;
                for (int i = 0; i < blocks.Length; i++)
                {
                    ref JpxCodeBlock block = ref blocks[i];
                    int x = i % precinctBand.BlocksWide;
                    int y = i / precinctBand.BlocksWide;
                    bool included = block.Included
                        ? reader.ReadBit() == 1
                        : precinctBand.Inclusion!.Decode(ref reader, x, y, layer + 1);
                    if (!included)
                    {
                        continue;
                    }

                    if (!block.Included)
                    {
                        block.ZeroPlanes = precinctBand.ZeroPlanes!.DecodeValue(ref reader, x, y, Math.Max(band.MagnitudeBits, 0) + 1);
                        block.Included = true;
                    }

                    int passes = ReadPassCount(ref reader);
                    while (reader.ReadBit() == 1 && !reader.Overrun)
                    {
                        block.LengthBits++;
                    }

                    int bits = block.LengthBits + JpxMath.FloorLog2(passes);
                    block.PendingPasses = passes;
                    block.PendingLength = bits > 31 ? int.MaxValue : reader.ReadBits(bits);
                    if (reader.Overrun)
                    {
                        break;
                    }
                }

                if (reader.Overrun)
                {
                    break;
                }
            }
        }

        reader.Align();
        if (reader.Overrun)
        {
            _reporter.Report(
                DiagnosticCodes.JpxPacketHeaderTruncated,
                DiagnosticSeverity.Warning,
                "A JPEG 2000 packet header runs past the end of the tile's data; decoding of the tile stops there.");
            ClearPending(resolution, precinct);
            return false;
        }

        position = reader.Position;
        if (coding.UsesEph)
        {
            if (position + 2 <= data.Length && data[position] == 0xFF && data[position + 1] == 0x92)
            {
                position += 2;
            }
            else
            {
                _reporter.Report(DiagnosticCodes.JpxEphMissing, DiagnosticSeverity.Warning, "A JPEG 2000 packet header is not followed by the EPH marker COD announces; the data is read as if it were.");
            }
        }

        if (!nonEmpty)
        {
            return true;
        }

        bool complete = true;
        for (int b = 0; b < bandCount; b++)
        {
            JpxCodeBlock[] blocks = resolution.PrecinctBands[(precinct * bandCount) + b].Blocks;
            for (int i = 0; i < blocks.Length; i++)
            {
                ref JpxCodeBlock block = ref blocks[i];
                if (block.PendingPasses == 0)
                {
                    continue;
                }

                int length = (int)Math.Min(block.PendingLength, (long)data.Length - position);
                if (length < block.PendingLength)
                {
                    complete = false;
                }

                AddChunk(ref block, position, length, block.PendingPasses);
                position += length;
                block.PendingPasses = 0;
                block.PendingLength = 0;
            }
        }

        if (!complete)
        {
            Truncated();
        }

        return complete;
    }

    private static void ClearPending(JpxResolution resolution, int precinct)
    {
        int bandCount = resolution.Bands.Length;
        for (int b = 0; b < bandCount; b++)
        {
            foreach (ref JpxCodeBlock block in resolution.PrecinctBands[(precinct * bandCount) + b].Blocks.AsSpan())
            {
                block.PendingPasses = 0;
                block.PendingLength = 0;
            }
        }
    }

    /// <summary>The number of new coding passes, Table B.4.</summary>
    private static int ReadPassCount(ref JpxPacketHeaderReader reader)
    {
        if (reader.ReadBit() == 0)
        {
            return 1;
        }

        if (reader.ReadBit() == 0)
        {
            return 2;
        }

        int value = reader.ReadBits(2);
        if (value != 3)
        {
            return 3 + value;
        }

        value = reader.ReadBits(5);
        return value != 31 ? 6 + value : 37 + reader.ReadBits(7);
    }

    private void AddChunk(ref JpxCodeBlock block, int offset, int length, int passes)
    {
        if (_chunkCount == _chunks.Length)
        {
            JpxChunk[] grown = ArrayPool<JpxChunk>.Shared.Rent(_chunks.Length * 2);
            _chunks.AsSpan(0, _chunkCount).CopyTo(grown);
            ArrayPool<JpxChunk>.Shared.Return(_chunks);
            _chunks = grown;
        }

        int index = _chunkCount++;
        _chunks[index] = new JpxChunk(offset, length, passes, -1);
        if (block.LastChunk >= 0)
        {
            _chunks[block.LastChunk].Next = index;
        }
        else
        {
            block.FirstChunk = index;
        }

        block.LastChunk = index;
        block.Passes += passes;
        block.DataLength += length;
    }

    /// <summary>Decodes every code-block of a tile-component into its buffer, with the reversible reconstruction of E.1.1.2.</summary>
    private void DecodeBlocks(JpxTileComponent component, ReadOnlySpan<byte> data, int[] buffer)
    {
        int stride = component.Width;
        foreach (JpxResolution resolution in component.Resolutions)
        {
            int bandCount = resolution.Bands.Length;
            for (int p = 0; p < resolution.PrecinctBands.Length; p++)
            {
                JpxBand band = resolution.Bands[p % bandCount];
                foreach (ref JpxCodeBlock block in resolution.PrecinctBands[p].Blocks.AsSpan())
                {
                    if (block.Passes == 0)
                    {
                        continue;
                    }

                    int top = band.MagnitudeBits - 1 - block.ZeroPlanes;
                    if (band.MagnitudeBits > 30)
                    {
                        _reporter.Report(
                            DiagnosticCodes.JpxPrecisionUnsupported,
                            DiagnosticSeverity.Information,
                            $"A JPEG 2000 sub-band has {band.MagnitudeBits} magnitude bit-planes; more than 30 are not decoded and those code-blocks are left empty.");
                        continue;
                    }

                    if (top < 0)
                    {
                        _reporter.Report(
                            DiagnosticCodes.JpxCodeBlockInvalid,
                            DiagnosticSeverity.Warning,
                            "A JPEG 2000 code-block claims more missing bit-planes than its sub-band has; it is left empty.");
                        continue;
                    }

                    ReadOnlySpan<byte> segment = Join(block, data);
                    int width = block.X1 - block.X0;
                    int height = block.Y1 - block.Y0;
                    int origin = ((band.BufferY + block.Y0) * stride) + band.BufferX + block.X0;
                    _blocks.Decode(segment, width, height, band.Orientation, component.Style.BlockStyle, top, block.Passes, buffer.AsSpan(origin), stride);
                }
            }
        }
    }

    /// <summary>The code-block's compressed data as one contiguous span: a slice of the tile data, or its chunks copied together.</summary>
    private ReadOnlySpan<byte> Join(in JpxCodeBlock block, ReadOnlySpan<byte> data)
    {
        JpxChunk first = _chunks[block.FirstChunk];
        if (first.Next < 0)
        {
            return data.Slice(first.Offset, first.Length);
        }

        if (_joined.Length < block.DataLength)
        {
            ArrayPool<byte>.Shared.Return(_joined);
            _joined = ArrayPool<byte>.Shared.Rent(block.DataLength);
        }

        int written = 0;
        for (int index = block.FirstChunk; index >= 0; index = _chunks[index].Next)
        {
            JpxChunk chunk = _chunks[index];
            data.Slice(chunk.Offset, chunk.Length).CopyTo(_joined.AsSpan(written));
            written += chunk.Length;
        }

        return _joined.AsSpan(0, written);
    }

    private void Truncated() => _reporter.Report(
        DiagnosticCodes.JpxCodestreamTruncated,
        DiagnosticSeverity.Warning,
        "The JPEG 2000 codestream ends early; what is present is decoded and the rest of the image is left empty.");
}
