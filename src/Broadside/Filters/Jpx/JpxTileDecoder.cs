using Broadside.Filters.Codecs;
using System.Buffers;
using System.Runtime.InteropServices;
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
/// ITU-T T.800 Annex B (B.3 to B.12), Annex D (through <see cref="JpxCodeBlockDecoder"/>), Annex E (reconstruction) and Annex F
/// (through <see cref="JpxWavelet"/>). The geometry is general: any precinct and code-block partition, any number of layers. All five
/// progressions (B.12.1) and progression volumes (POC, B.12.2) are read, packet headers from the bitstream or packed in PPM or PPT
/// (A.7.4, A.7.5), with codeword segments split as the termination and bypass code-block styles require (B.10.7.2, Table D.9).
/// </para>
/// <para>
/// A reversible component's buffer holds integers; an irreversible one's holds the bits of <see cref="float"/> values (the same
/// storage reinterpreted), dequantized (E.1.1.1) and 9/7-reconstructed. One instance per decode call; it owns pooled buffers until
/// <see cref="Dispose"/>.
/// </para>
/// </remarks>
internal sealed class JpxTileDecoder : IDisposable
{
    /// <summary>The most code-blocks and precincts one tile may have before it is refused.</summary>
    internal const int MaxStructures = 1 << 21;

    private readonly CodecReporter _reporter;
    private readonly JpxCodeBlockDecoder _blocks = new();
    private JpxChunk[] _chunks = ArrayPool<JpxChunk>.Shared.Rent(64);
    private int _chunkCount;
    private PendingPiece[] _pending = ArrayPool<PendingPiece>.Shared.Rent(64);
    private int _pendingCount;
    private JpxSegment[] _segments = ArrayPool<JpxSegment>.Shared.Rent(16);
    private byte[] _joined = ArrayPool<byte>.Shared.Rent(256);
    private int _structures;

    public JpxTileDecoder(CodecReporter reporter) => _reporter = reporter;

    /// <summary>
    /// Decodes the tile's packets (<paramref name="data"/>, its tile-parts joined; <paramref name="headers"/>, its packed packet headers
    /// or <see langword="null"/>) into <paramref name="buffers"/>, one rented buffer per component of <c>Width * Height</c> samples
    /// (relative to the tile-component's origin), and returns the tile-components.
    /// </summary>
    /// <returns>The tile-components; <see langword="null"/> when the tile's structure is over the limits.</returns>
    public JpxTileComponent[]? Decode(JpxImageSize size, JpxTileParameters parameters, int tile, ReadOnlySpan<byte> data, byte[]? headers, int[][] buffers)
    {
        _chunkCount = 0;
        _structures = 0;
        (long tx0, long ty0, long tx1, long ty1) = size.TileBounds(tile);
        var components = new JpxTileComponent[size.Components.Length];
        for (int c = 0; c < components.Length; c++)
        {
            if (Build(size.Components[c], parameters.Components[c], parameters.Quantization[c], parameters.RoiShifts[c], tx0, ty0, tx1, ty1) is not { } component)
            {
                return null;
            }

            components[c] = component;
        }

        var cursor = new PacketCursor(data, headers);
        ReadPackets(parameters, components, (tx0, ty0, tx1, ty1), ref cursor);
        for (int c = 0; c < components.Length; c++)
        {
            JpxTileComponent component = components[c];
            int[] buffer = buffers[c];
            buffer.AsSpan(0, component.Width * component.Height).Clear();
            DecodeBlocks(component, data, buffer);
            if (component.Style.Reversible)
            {
                JpxWavelet.Inverse53(component, buffer);
            }
            else
            {
                JpxWavelet.Inverse97(component, MemoryMarshal.Cast<int, float>(buffer.AsSpan()));
            }
        }

        return components;
    }

    /// <summary>Returns the pooled buffers.</summary>
    public void Dispose()
    {
        _blocks.Dispose();
        ArrayPool<JpxChunk>.Shared.Return(_chunks);
        ArrayPool<PendingPiece>.Shared.Return(_pending, clearArray: true);
        ArrayPool<JpxSegment>.Shared.Return(_segments);
        ArrayPool<byte>.Shared.Return(_joined);
        _chunks = [];
        _pending = [];
        _segments = [];
        _joined = [];
    }

    /// <summary>The base-2 logarithm of the sub-band gain (Table E.1): LL 0, HL and LH 1, HH 2.</summary>
    private static int GainLog2(int orientation) => orientation switch { 0 => 0, 3 => 2, _ => 1 };

    private JpxTileComponent? Build(JpxComponentInfo info, JpxComponentStyle style, JpxQuantization quantization, int roiShift, long tx0, long ty0, long tx1, long ty1)
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
                int exponent = quantization.Exponent(index, levels, level);
                float scale = 0;
                if (!style.Reversible)
                {
                    // E-3, E-4: delta_b = 2^(R_b - eps_b) (1 + mu_b / 2^11) with R_b = depth + log2(gain_b); halved for the doubled values.
                    double delta = Math.ScaleB(1 + (quantization.Mantissa(index) / 2048.0), info.Depth + GainLog2(orientation) - exponent);
                    scale = (float)(delta / 2);
                }

                bands[b] = new JpxBand
                {
                    Orientation = orientation,
                    X0 = bx0,
                    Y0 = by0,
                    Width = (int)Math.Max(0, bx1 - bx0),
                    Height = (int)Math.Max(0, by1 - by0),
                    BufferX = (orientation & 1) == 0 ? 0 : lower!.Width,
                    BufferY = (orientation >> 1) == 0 ? 0 : lower!.Height,
                    MagnitudeBits = quantization.GuardBits + exponent - 1,
                    Scale = scale,
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
                PrecinctExponentX = ppx,
                PrecinctExponentY = ppy,
                NextLayer = new int[wide * high],
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
            Dx = info.Dx,
            Dy = info.Dy,
            RoiShift = roiShift,
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
                    SegmentIndex = -1,
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

    /// <summary>
    /// Reads every packet of the tile until the data ends: one progression volume per POC entry (B.12.2), else one volume in the COD
    /// order over the whole tile. A packet a volume repeats is skipped (each precinct's layers are read once, in order).
    /// </summary>
    private void ReadPackets(JpxTileParameters parameters, JpxTileComponent[] components, (long X0, long Y0, long X1, long Y1) tile, ref PacketCursor cursor)
    {
        JpxCodingStyle coding = parameters.Coding;
        int maxLevels = 0;
        foreach (JpxTileComponent component in components)
        {
            maxLevels = Math.Max(maxLevels, component.Style.Levels);
        }

        if (parameters.Volumes.Count == 0)
        {
            ReadVolume(coding, components, tile, new JpxProgressionVolume(0, 0, coding.Layers, maxLevels + 1, components.Length, coding.Progression), ref cursor);
            return;
        }

        foreach (JpxProgressionVolume volume in parameters.Volumes)
        {
            JpxProgressionVolume bounded = volume with
            {
                LayerEnd = Math.Min(volume.LayerEnd, coding.Layers),
                ResolutionEnd = Math.Min(volume.ResolutionEnd, maxLevels + 1),
                ComponentEnd = Math.Min(volume.ComponentEnd, components.Length),
            };

            if (!ReadVolume(coding, components, tile, bounded, ref cursor))
            {
                return;
            }
        }
    }

    /// <summary>Reads the packets of one progression volume in its order (B.12.1.1 to B.12.1.5). Returns <see langword="false"/> when the data runs out.</summary>
    private bool ReadVolume(JpxCodingStyle coding, JpxTileComponent[] components, (long X0, long Y0, long X1, long Y1) tile, JpxProgressionVolume volume, ref PacketCursor cursor)
    {
        int layers = volume.LayerEnd;
        int rs = volume.ResolutionStart;
        int re = volume.ResolutionEnd;
        int cs = volume.ComponentStart;
        int ce = volume.ComponentEnd;
        switch (volume.Progression)
        {
            case 0: // LRCP
                for (int l = 0; l < layers; l++)
                {
                    for (int r = rs; r < re; r++)
                    {
                        for (int c = cs; c < ce; c++)
                        {
                            if (!ReadAllPrecincts(coding, components[c], r, l, ref cursor))
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            case 1: // RLCP
                for (int r = rs; r < re; r++)
                {
                    for (int l = 0; l < layers; l++)
                    {
                        for (int c = cs; c < ce; c++)
                        {
                            if (!ReadAllPrecincts(coding, components[c], r, l, ref cursor))
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            case 2: // RPCL
                for (int r = rs; r < re; r++)
                {
                    if (!ReadPositions(coding, components, tile, volume, r, r + 1, ref cursor))
                    {
                        return false;
                    }
                }

                return true;
            case 3: // PCRL
                return ReadPositions(coding, components, tile, volume, rs, re, ref cursor);
            default: // CPRL
                for (int c = cs; c < ce; c++)
                {
                    if (!ReadPositions(coding, components, tile, volume with { ComponentStart = c, ComponentEnd = c + 1 }, rs, re, ref cursor))
                    {
                        return false;
                    }
                }

                return true;
        }
    }

    private bool ReadAllPrecincts(JpxCodingStyle coding, JpxTileComponent component, int r, int layer, ref PacketCursor cursor)
    {
        if (r > component.Style.Levels)
        {
            return true;
        }

        JpxResolution resolution = component.Resolutions[r];
        int precincts = resolution.PrecinctsWide * resolution.PrecinctsHigh;
        for (int k = 0; k < precincts; k++)
        {
            if (!ReadOnce(coding, component.Style.BlockStyle, resolution, k, layer, ref cursor))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The position-driven loops of B.12.1.3 to B.12.1.5 over the tile's reference grid, stepping from one precinct origin to the next
    /// (the NOTE of B.12.1.3): for every position, the components and resolutions whose precinct starts there, then their layers.
    /// RPCL calls it per resolution; CPRL per component; PCRL once.
    /// </summary>
    private bool ReadPositions(JpxCodingStyle coding, JpxTileComponent[] components, (long X0, long Y0, long X1, long Y1) tile, JpxProgressionVolume volume, int rs, int re, ref PacketCursor cursor)
    {
        long stepX = long.MaxValue;
        long stepY = long.MaxValue;
        for (int c = volume.ComponentStart; c < volume.ComponentEnd; c++)
        {
            JpxTileComponent component = components[c];
            for (int r = rs; r < Math.Min(re, component.Style.Levels + 1); r++)
            {
                JpxResolution resolution = component.Resolutions[r];
                int level = component.Style.Levels - r;
                stepX = Math.Min(stepX, Shifted(component.Dx, resolution.PrecinctExponentX + level));
                stepY = Math.Min(stepY, Shifted(component.Dy, resolution.PrecinctExponentY + level));
            }
        }

        if (stepX == long.MaxValue || stepY == long.MaxValue)
        {
            return true;
        }

        for (long y = tile.Y0; y < tile.Y1; y += stepY - (y % stepY))
        {
            for (long x = tile.X0; x < tile.X1; x += stepX - (x % stepX))
            {
                // PCRL nests components then resolutions; RPCL (one resolution) and CPRL (one component) reduce to the same loops.
                for (int c = volume.ComponentStart; c < volume.ComponentEnd; c++)
                {
                    for (int r = rs; r < re; r++)
                    {
                        if (!ReadAtPosition(coding, components[c], r, x, y, tile, volume.LayerEnd, ref cursor))
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    /// <summary>XRsiz * 2^shift, saturated (B.12.1.3 NOTE: the products need more than 32 bits).</summary>
    private static long Shifted(int separation, int shift) => shift >= 62 - 8 ? long.MaxValue / 4 : (long)separation << shift;

    /// <summary>
    /// The packets of resolution <paramref name="r"/> of a component whose precinct starts at (<paramref name="x"/>, <paramref name="y"/>),
    /// if one does (B.12.1.3: divisible, or the tile's first row or column with a partial first precinct), precinct index by B-20.
    /// </summary>
    private bool ReadAtPosition(JpxCodingStyle coding, JpxTileComponent component, int r, long x, long y, (long X0, long Y0, long X1, long Y1) tile, int layers, ref PacketCursor cursor)
    {
        int levels = component.Style.Levels;
        if (r > levels)
        {
            return true;
        }

        JpxResolution resolution = component.Resolutions[r];
        if (resolution.Width == 0 || resolution.Height == 0 || resolution.PrecinctsWide == 0 || resolution.PrecinctsHigh == 0)
        {
            return true;
        }

        int level = levels - r;
        int ppx = resolution.PrecinctExponentX;
        int ppy = resolution.PrecinctExponentY;
        long rowStep = Shifted(component.Dy, ppy + level);
        long columnStep = Shifted(component.Dx, ppx + level);
        bool rowStart = y % rowStep == 0 || (y == tile.Y0 && (resolution.Y0 << level) % (1L << Math.Min(ppy + level, 62)) != 0);
        bool columnStart = x % columnStep == 0 || (x == tile.X0 && (resolution.X0 << level) % (1L << Math.Min(ppx + level, 62)) != 0);
        if (!rowStart || !columnStart)
        {
            return true;
        }

        long px = JpxMath.FloorShift(JpxMath.CeilDivide(x, Shifted(component.Dx, level)), ppx) - JpxMath.FloorShift(resolution.X0, ppx);
        long py = JpxMath.FloorShift(JpxMath.CeilDivide(y, Shifted(component.Dy, level)), ppy) - JpxMath.FloorShift(resolution.Y0, ppy);
        if (px < 0 || py < 0 || px >= resolution.PrecinctsWide || py >= resolution.PrecinctsHigh)
        {
            return true;
        }

        int k = (int)(px + (py * resolution.PrecinctsWide));
        for (int l = 0; l < layers; l++)
        {
            if (!ReadOnce(coding, component.Style.BlockStyle, resolution, k, l, ref cursor))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads packet (precinct <paramref name="k"/>, <paramref name="layer"/>) unless an earlier volume already did.</summary>
    private bool ReadOnce(JpxCodingStyle coding, int style, JpxResolution resolution, int k, int layer, ref PacketCursor cursor)
    {
        if (layer < resolution.NextLayer[k])
        {
            return true;
        }

        resolution.NextLayer[k] = layer + 1;
        return ReadPacket(coding, style, resolution, k, layer, ref cursor);
    }

    /// <summary>Reads one packet: header (B.10) and body. Returns <see langword="false"/> when the data runs out.</summary>
    private bool ReadPacket(JpxCodingStyle coding, int style, JpxResolution resolution, int precinct, int layer, ref PacketCursor cursor)
    {
        ReadOnlySpan<byte> data = cursor.Data;
        if (!cursor.Packed && cursor.DataPosition >= data.Length)
        {
            Truncated();
            return false;
        }

        if (coding.MayUseSop && cursor.DataPosition + 6 <= data.Length && data[cursor.DataPosition] == 0xFF && data[cursor.DataPosition + 1] == 0x91)
        {
            cursor.DataPosition += 6;
        }

        ReadOnlySpan<byte> headers = cursor.Packed ? cursor.Headers : data;
        int headerStart = cursor.Packed ? cursor.HeaderPosition : cursor.DataPosition;
        if (headerStart >= headers.Length)
        {
            Truncated();
            return false;
        }

        _pendingCount = 0;
        int bandCount = resolution.Bands.Length;
        var reader = new JpxPacketHeaderReader(headers, headerStart);
        bool nonEmpty = reader.ReadBit() == 1;
        if (nonEmpty)
        {
            for (int b = 0; b < bandCount && !reader.Overrun; b++)
            {
                JpxPrecinctBand precinctBand = resolution.PrecinctBands[(precinct * bandCount) + b];
                JpxBand band = resolution.Bands[b];
                JpxCodeBlock[] blocks = precinctBand.Blocks;
                for (int i = 0; i < blocks.Length && !reader.Overrun; i++)
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
                        block.ZeroPlanes = precinctBand.ZeroPlanes!.DecodeValue(ref reader, x, y, Math.Max(band.MagnitudeBits, 0) + 255);
                        block.Included = true;
                    }

                    int passes = ReadPassCount(ref reader);
                    while (reader.ReadBit() == 1 && !reader.Overrun)
                    {
                        block.LengthBits++;
                    }

                    ReadSegmentLengths(ref block, blocks, i, passes, style, ref reader);
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
            _pendingCount = 0;
            return false;
        }

        int position = reader.Position;
        if (coding.UsesEph)
        {
            if (position + 2 <= headers.Length && headers[position] == 0xFF && headers[position + 1] == 0x92)
            {
                position += 2;
            }
            else
            {
                _reporter.Report(DiagnosticCodes.JpxEphMissing, DiagnosticSeverity.Warning, "A JPEG 2000 packet header is not followed by the EPH marker COD announces; the data is read as if it were.");
            }
        }

        if (cursor.Packed)
        {
            cursor.HeaderPosition = position;
        }
        else
        {
            cursor.DataPosition = position;
        }

        bool complete = true;
        for (int p = 0; p < _pendingCount; p++)
        {
            ref PendingPiece piece = ref _pending[p];
            int start = cursor.DataPosition;
            int length = (int)Math.Min(piece.Length, (long)data.Length - start);
            if (length < piece.Length)
            {
                complete = false;
            }

            AddChunk(ref piece.Blocks[piece.Index], start, Math.Max(length, 0), piece.Passes, piece.StartsSegment);
            cursor.DataPosition = start + Math.Max(length, 0);
            piece.Blocks = null!;
        }

        _pendingCount = 0;
        if (!complete)
        {
            Truncated();
        }

        return complete;
    }

    /// <summary>
    /// Reads the lengths of a code-block's new passes, one per codeword segment they touch (B.10.7.2): a segment holds one pass with
    /// termination on each pass, 10 then alternately 2 and 1 with the bypass, else all passes (Table D.9).
    /// </summary>
    private void ReadSegmentLengths(ref JpxCodeBlock block, JpxCodeBlock[] blocks, int index, int passes, int style, ref JpxPacketHeaderReader reader)
    {
        int remaining = passes;
        while (remaining > 0 && !reader.Overrun)
        {
            bool starts = false;
            if (block.SegmentPasses >= block.SegmentMax)
            {
                block.SegmentIndex++;
                block.SegmentMax = SegmentCapacity(style, block.SegmentIndex, block.SegmentMax);
                block.SegmentPasses = 0;
                starts = true;
            }

            int count = Math.Min(block.SegmentMax - block.SegmentPasses, remaining);
            int bits = block.LengthBits + JpxMath.FloorLog2(count);
            int length = bits > 31 ? int.MaxValue : reader.ReadBits(bits);
            AddPending(blocks, index, length, count, starts);
            block.SegmentPasses += count;
            remaining -= count;
        }
    }

    /// <summary>The most passes codeword segment <paramref name="segment"/> may hold (Table D.9; OpenJPEG's opj_t2_init_seg).</summary>
    private static int SegmentCapacity(int style, int segment, int previous)
    {
        if ((style & JpxComponentStyle.TerminateEachPass) != 0)
        {
            return 1;
        }

        if ((style & JpxComponentStyle.Bypass) != 0)
        {
            return segment == 0 ? 10 : previous is 1 or 10 ? 2 : 1;
        }

        return 109;
    }

    private void AddPending(JpxCodeBlock[] blocks, int index, int length, int passes, bool starts)
    {
        if (_pendingCount == _pending.Length)
        {
            PendingPiece[] grown = ArrayPool<PendingPiece>.Shared.Rent(_pending.Length * 2);
            _pending.AsSpan(0, _pendingCount).CopyTo(grown);
            ArrayPool<PendingPiece>.Shared.Return(_pending, clearArray: true);
            _pending = grown;
        }

        _pending[_pendingCount++] = new PendingPiece { Blocks = blocks, Index = index, Length = length, Passes = passes, StartsSegment = starts };
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

    private void AddChunk(ref JpxCodeBlock block, int offset, int length, int passes, bool startsSegment)
    {
        if (_chunkCount == _chunks.Length)
        {
            JpxChunk[] grown = ArrayPool<JpxChunk>.Shared.Rent(_chunks.Length * 2);
            _chunks.AsSpan(0, _chunkCount).CopyTo(grown);
            ArrayPool<JpxChunk>.Shared.Return(_chunks);
            _chunks = grown;
        }

        int index = _chunkCount++;
        _chunks[index] = new JpxChunk(offset, length, passes, -1, startsSegment);
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

    /// <summary>Decodes every code-block of a tile-component into its buffer, with the reconstruction of E.1.1.</summary>
    private void DecodeBlocks(JpxTileComponent component, ReadOnlySpan<byte> data, int[] buffer)
    {
        int stride = component.Width;
        int roi = component.RoiShift;
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

                    int planes = band.MagnitudeBits + roi;
                    int top = planes - 1 - block.ZeroPlanes;
                    if (planes > 30 || roi >= 31)
                    {
                        _reporter.Report(
                            DiagnosticCodes.JpxPrecisionUnsupported,
                            DiagnosticSeverity.Information,
                            $"A JPEG 2000 sub-band has {planes} magnitude bit-planes (with the region-of-interest shift); more than 30 are not decoded and those code-blocks are left empty.");
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

                    ReadOnlySpan<byte> joined = Join(block, data, out int segments);
                    int width = block.X1 - block.X0;
                    int height = block.Y1 - block.Y0;
                    int origin = ((band.BufferY + block.Y0) * stride) + band.BufferX + block.X0;
                    bool symbols = _blocks.Decode(
                        joined,
                        _segments.AsSpan(0, segments),
                        new JpxBlockCoding(width, height, band.Orientation, component.Style.BlockStyle, top, roi, band.Scale),
                        buffer.AsSpan(origin),
                        stride);
                    if (!symbols)
                    {
                        _reporter.Report(
                            DiagnosticCodes.JpxSegmentationSymbolMismatch,
                            DiagnosticSeverity.Warning,
                            "A JPEG 2000 code-block's segmentation symbol is not 1010 (D.5); the data is decoded anyway.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The code-block's compressed data as one contiguous span (a slice of the tile data, or its chunks copied together), with its
    /// codeword segments in <see cref="_segments"/>.
    /// </summary>
    private ReadOnlySpan<byte> Join(in JpxCodeBlock block, ReadOnlySpan<byte> data, out int segments)
    {
        segments = 0;
        int written = 0;
        int index = block.FirstChunk;
        JpxChunk first = _chunks[index];
        bool single = first.Next < 0;
        if (!single && _joined.Length < block.DataLength)
        {
            ArrayPool<byte>.Shared.Return(_joined);
            _joined = ArrayPool<byte>.Shared.Rent(block.DataLength);
        }

        for (; index >= 0; index = _chunks[index].Next)
        {
            JpxChunk chunk = _chunks[index];
            if (chunk.StartsSegment || segments == 0)
            {
                if (segments == _segments.Length)
                {
                    JpxSegment[] grown = ArrayPool<JpxSegment>.Shared.Rent(_segments.Length * 2);
                    _segments.AsSpan().CopyTo(grown);
                    ArrayPool<JpxSegment>.Shared.Return(_segments);
                    _segments = grown;
                }

                _segments[segments++] = new JpxSegment(written, 0, 0);
            }

            ref JpxSegment segment = ref _segments[segments - 1];
            segment = segment with { Length = segment.Length + chunk.Length, Passes = segment.Passes + chunk.Passes };
            if (!single)
            {
                data.Slice(chunk.Offset, chunk.Length).CopyTo(_joined.AsSpan(written));
            }

            written += chunk.Length;
        }

        return single ? data.Slice(first.Offset, first.Length) : _joined.AsSpan(0, written);
    }

    private void Truncated() => _reporter.Report(
        DiagnosticCodes.JpxCodestreamTruncated,
        DiagnosticSeverity.Warning,
        "The JPEG 2000 codestream ends early; what is present is decoded and the rest of the image is left empty.");

    /// <summary>A codeword segment length read from a packet header, waiting for the packet's body.</summary>
    private struct PendingPiece
    {
        public JpxCodeBlock[] Blocks;
        public int Index;
        public int Length;
        public int Passes;
        public bool StartsSegment;
    }

    /// <summary>Where the packet headers and bodies of a tile are read from: one bitstream, or packed headers plus the bitstream.</summary>
    private ref struct PacketCursor
    {
        public PacketCursor(ReadOnlySpan<byte> data, byte[]? headers)
        {
            Data = data;
            Headers = headers;
            Packed = headers is not null;
        }

        public ReadOnlySpan<byte> Data { get; }

        public ReadOnlySpan<byte> Headers { get; }

        public bool Packed { get; }

        public int DataPosition { get; set; }

        public int HeaderPosition { get; set; }
    }
}
