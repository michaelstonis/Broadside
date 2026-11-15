using System.Buffers;
using System.Runtime.InteropServices;
using Broadside.Diagnostics;
using Broadside.Filters.Codecs;
using Broadside.Images;
using Broadside.Parsing;

namespace Broadside.Filters.Jpx;

/// <summary>
/// Decodes a JPEG 2000 file or codestream into a <see cref="DecodedImage"/>: locates the codestream and the JP2 colour boxes, reads the
/// codestream's headers, decodes each tile, applies the inverse component transform and DC level shift, and composes the channels
/// the PDF image needs (component mapping, palette, channel definitions, colour conversion, opacity) in the §8.9.3 layout.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.9 and Table 87; ITU-T T.800 | ISO/IEC 15444-1 Annexes A to I. Tiles are decoded into one plane per component at
/// the component's own resolution (unsigned, after the DC level shift and clipping, at most 16 bits); the channels are then composed
/// per row, a sub-sampled component replicated over the image grid (each image sample takes the component sample at or before it,
/// G.4). <see cref="JpxOutputPlan"/> decides the channels.
/// </para>
/// <para>
/// The output's bits per component are the colour channels' greatest precision (at most 16); channels of a smaller precision are
/// scaled to it so the full range maps to the full range; samples keep their precision otherwise (§8.9.5.2: the Decode domain is 0 to
/// 2^n - 1 with n the JPX precision). Signed components are offset by 2^(n-1) to be unsigned.
/// </para>
/// </remarks>
internal static class JpxDecoder
{
    /// <summary>Reads the codestream's size, the output's channels and its precision.</summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> data, ImageFilterContext context, out ImageHeader header)
    {
        header = default;
        var reporter = new CodecReporter(context.Filter);
        if (JpxCodestreamReader.Locate(data, reporter, out byte[]? assembled) is not { } range)
        {
            return false;
        }

        ReadOnlySpan<byte> source = assembled is null ? data : assembled;
        if (JpxCodestreamReader.ReadSize(source.Slice(range.Offset, range.Length), reporter) is not { } size)
        {
            return false;
        }

        var quiet = new CodecReporter(context.Filter, silent: true);
        JpxOutputPlan plan = JpxOutputPlan.Create(size, JpxFileHeader.Read(data, quiet), context, quiet);
        int maxReduction = OnReducedGrid(size, JpxCodestreamReader.ReadMainHeaderLevels(source.Slice(range.Offset, range.Length), size.Components.Length));
        header = new ImageHeader(new ImageGeometry(
            (int)Math.Min(size.Width - size.OriginX, int.MaxValue),
            (int)Math.Min(size.Height - size.OriginY, int.MaxValue),
            plan.Colors.Length,
            plan.Bits))
        {
            HasAlpha = plan.Alpha is not null,
            ColorModel = plan.Model,
            IccProfile = plan.IccProfile,
            MaxReduction = maxReduction,
        };
        return true;
    }

    /// <summary>
    /// The most resolution levels a reduced decode may drop (issue #362; ITU-T T.800 B.5): the fewest decomposition levels of any
    /// tile-component, and no more than keep the image origin on the reduced grid, so a reduction by 2^d is exactly ⌈W / 2^d⌉ x
    /// ⌈H / 2^d⌉ samples. The header offers the main header's levels (<see cref="JpxCodestreamReader.ReadMainHeaderLevels"/>); a
    /// tile-part header that lowers them makes the decode deliver less than asked, which the samples' reduction says. The decode
    /// never drops more than the header offers (issue #424): tile-part headers that all raise the levels do not lift that limit.
    /// </summary>
    private static int MaxReduction(JpxCodestream stream, ReadOnlySpan<byte> codestream)
    {
        int levels = JpxCodestreamReader.ReadMainHeaderLevels(codestream, stream.Size.Components.Length);
        foreach (JpxTile tile in stream.Tiles)
        {
            foreach (JpxComponentStyle style in JpxTileParameters.Resolve(stream.Main, tile.Markers).Components)
            {
                levels = Math.Min(levels, style.Levels);
            }
        }

        if (stream.Tiles.Count == 0)
        {
            foreach (JpxComponentStyle style in JpxTileParameters.Resolve(stream.Main, stream.Main).Components)
            {
                levels = Math.Min(levels, style.Levels);
            }
        }

        return OnReducedGrid(stream.Size, levels);
    }

    /// <summary>Lowers <paramref name="levels"/> until the image origin divides by 2^levels; at most <see cref="ImageGeometry.MaxReduction"/>.</summary>
    private static int OnReducedGrid(JpxImageSize size, int levels)
    {
        levels = Math.Clamp(levels, 0, ImageGeometry.MaxReduction);
        while (levels > 0 && ((size.OriginX & ((1L << levels) - 1)) != 0 || (size.OriginY & ((1L << levels) - 1)) != 0))
        {
            levels--;
        }

        return levels;
    }

    /// <summary>The image size at a reduction of 2^<paramref name="reduction"/>: the reference grid divided, rounded up (B-15).</summary>
    private static JpxImageSize Reduce(JpxImageSize size, int reduction) => reduction == 0 ? size : new JpxImageSize
    {
        Width = JpxMath.CeilShift(size.Width, reduction),
        Height = JpxMath.CeilShift(size.Height, reduction),
        OriginX = JpxMath.CeilShift(size.OriginX, reduction),
        OriginY = JpxMath.CeilShift(size.OriginY, reduction),
        TileWidth = size.TileWidth,
        TileHeight = size.TileHeight,
        TileOriginX = size.TileOriginX,
        TileOriginY = size.TileOriginY,
        Components = size.Components,
    };

    /// <summary>Decodes the image, or returns <see langword="null"/> after reporting why it cannot be.</summary>
    public static DecodedImage? Decode(ReadOnlySpan<byte> data, ImageFilterContext context)
    {
        using var run = new JpxDecodeRun(context);
        run.Continue(data, long.MaxValue);
        return run.TakeResult();
    }

    /// <summary>
    /// A JPEG 2000 decode that runs in slices (issue #365): <see cref="Continue"/> works until the image is done or, after at least one
    /// unit of work, the deadline has passed, between two code-blocks, two wavelet levels or two tiles; the next call resumes there.
    /// <see cref="Decode"/> runs one to the end, so the samples are the same however a decode is sliced.
    /// </summary>
    /// <remarks>ITU-T T.800 Annexes B, D, E and F; ISO 32000-2 §7.4.9. One decode at a time; it owns pooled buffers until disposed.</remarks>
    internal sealed class JpxDecodeRun : IDisposable
    {
        private readonly ImageFilterContext _context;
        private readonly CodecReporter _reporter;
        private Stage _stage;
        private byte[]? _assembled;
        private (int Offset, int Length) _range;
        private JpxCodestream? _stream;
        private JpxImageSize? _output;
        private JpxOutputPlan? _plan;
        private int _reduction;
        private DecodedImageBuilder? _builder;
        private DecodedImageBuilder? _alpha;
        private JpxPlane?[] _planes = [];
        private JpxTileDecoder? _tiles;
        private int _tile;
        private DecodedImage? _result;

        // The tile in progress: its joined tile-parts, its buffers, its parameters and components.
        private byte[]? _joined;
        private int _joinedLength;
        private int[][]? _buffers;
        private JpxTileParameters? _parameters;
        private JpxTileComponent[]? _components;

        public JpxDecodeRun(ImageFilterContext context)
        {
            _context = context;
            _reporter = new CodecReporter(context.Filter);
        }

        private enum Stage
        {
            Setup,
            TileStart,
            TileBlocks,
            Compose,
            Done,
        }

        /// <summary>Gets the reporter, which carries a sliced decode's deadline and slice token.</summary>
        public CodecReporter Reporter => _reporter;

        /// <summary>Returns the image once the run is done (the caller owns it), or <see langword="null"/>.</summary>
        public DecodedImage? TakeResult()
        {
            DecodedImage? result = _result;
            _result = null;
            return result;
        }

        /// <summary>Decodes from where the last call stopped; true when the run is done.</summary>
        /// <param name="data">The JPXDecode data, the same on every call.</param>
        /// <param name="deadline">The <see cref="System.Diagnostics.Stopwatch"/> timestamp to pause at.</param>
        public bool Continue(ReadOnlySpan<byte> data, long deadline)
        {
            _reporter.Deadline = deadline;
            while (true)
            {
                switch (_stage)
                {
                    case Stage.Setup:
                        if (!Setup(data))
                        {
                            Finish();
                            return true;
                        }

                        _stage = Stage.TileStart;
                        break;
                    case Stage.TileStart:
                        if (_tile >= _stream!.Tiles.Count)
                        {
                            _stage = Stage.Compose;
                            break;
                        }

                        _reporter.ThrowIfCancellationRequested();
                        if (StartTile(Codestream(data)))
                        {
                            _stage = Stage.TileBlocks;
                        }
                        else
                        {
                            EndTile();
                            _tile++;
                        }

                        break;
                    case Stage.TileBlocks:
                        if (!_tiles!.Continue(TileData(data), _buffers!, deadline))
                        {
                            return false;
                        }

                        FinishTile();
                        EndTile();
                        _tile++;
                        _stage = Stage.TileStart;
                        if (_reporter.ShouldPause)
                        {
                            return false;
                        }

                        break;
                    case Stage.Compose:
                        _reporter.ThrowIfCancellationRequested();
                        Compose();
                        Finish();
                        return true;
                    default:
                        return true;
                }
            }
        }

        private ReadOnlySpan<byte> Codestream(ReadOnlySpan<byte> data) => (_assembled is null ? data : _assembled).Slice(_range.Offset, _range.Length);

        private ReadOnlySpan<byte> TileData(ReadOnlySpan<byte> data)
        {
            if (_joined is not null)
            {
                return _joined.AsSpan(0, _joinedLength);
            }

            (int _, int offset, int length) = _stream!.Tiles[_tile].Parts[0];
            return Codestream(data).Slice(offset, length);
        }

        private bool Setup(ReadOnlySpan<byte> data)
        {
            if (JpxCodestreamReader.Locate(data, _reporter, out _assembled) is not { } range)
            {
                _reporter.Report(JpxDiagnostics.JpxSignatureInvalid, DiagnosticSeverity.Error, "The JPXDecode data holds no JPEG 2000 codestream.");
                return false;
            }

            _range = range;
            JpxFileHeader file = JpxFileHeader.Read(data, _reporter);
            ReadOnlySpan<byte> codestream = Codestream(data);
            if (JpxCodestreamReader.Read(codestream, _reporter) is not { } stream)
            {
                return false;
            }

            if (stream.HighThroughput)
            {
                _reporter.Report(
                    JpxDiagnostics.JpxHighThroughputUnsupported,
                    DiagnosticSeverity.Error,
                    "The JPEG 2000 codestream uses high-throughput block coding (HTJ2K, ISO/IEC 15444-15), which is outside the JPX baseline ISO 32000-2 §7.4.9 limits PDF images to; the image is not decoded.");
                return false;
            }

            _stream = stream;
            JpxImageSize size = stream.Size;
            _reduction = _context.Reduction > 0 ? Math.Min(_context.Reduction, MaxReduction(stream, codestream)) : 0;
            _output = Reduce(size, _reduction);
            long width = _output.Width - _output.OriginX;
            long height = _output.Height - _output.OriginY;
            if (width > int.MaxValue || height > int.MaxValue)
            {
                _reporter.Report(JpxDiagnostics.JpxLimitExceeded, DiagnosticSeverity.Error, $"The JPEG 2000 image of {width} x {height} samples is too large.");
                return false;
            }

            JpxOutputPlan plan = JpxOutputPlan.Create(size, file, _context, _reporter);
            _plan = plan;
            if (!CheckLimits(stream, _output, plan, _context, _reporter))
            {
                return false;
            }

            _planes = new JpxPlane?[size.Components.Length];
            if (!_context.TryCreateImage(new ImageGeometry((int)width, (int)height, Math.Clamp(plan.Colors.Length, 1, ImageGeometry.MaxComponents), plan.Bits), out _builder))
            {
                return false;
            }

            if (plan.Alpha is { } opacity && !_builder.TryCreateAlpha(opacity.Depth, plan.Premultiplied, out _alpha))
            {
                _alpha = null;
            }

            for (int c = 0; c < _planes.Length; c++)
            {
                if (plan.UsedComponents[c])
                {
                    _planes[c] = new JpxPlane(_output, c);
                }
            }

            _tiles = new JpxTileDecoder(_reporter);
            return true;
        }

        /// <summary>Joins the tile's parts, rents its buffers and reads its packets; false when the tile is skipped.</summary>
        private bool StartTile(ReadOnlySpan<byte> codestream)
        {
            JpxCodestream stream = _stream!;
            JpxTile tile = stream.Tiles[_tile];
            JpxImageSize size = stream.Size;
            (long tx0, long ty0, long tx1, long ty1) = size.TileBounds(tile.Index);
            int count = size.Components.Length;
            long area = (tx1 - tx0) * (ty1 - ty0);
            int length = 0;
            foreach ((int _, int _, int partLength) in tile.Parts)
            {
                length += partLength;
            }

            if (tile.Parts.Count != 1)
            {
                _joined = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
                _joinedLength = length;
                int written = 0;
                foreach ((int _, int offset, int partLength) in tile.Parts)
                {
                    codestream.Slice(offset, partLength).CopyTo(_joined.AsSpan(written));
                    written += partLength;
                }
            }

            _buffers = new int[count][];
            for (int c = 0; c < count; c++)
            {
                _buffers[c] = ArrayPool<int>.Shared.Rent((int)Math.Max(1, area));
            }

            _parameters = JpxTileParameters.Resolve(stream.Main, tile.Markers);
            byte[]? headers = tile.PackedHeaders();
            if (headers is null && stream.HasMainPacketHeaders)
            {
                headers = [];
            }

            ReadOnlySpan<byte> data = _joined is not null ? _joined.AsSpan(0, _joinedLength) : codestream.Slice(tile.Parts[0].Offset, tile.Parts[0].Length);
            _components = _tiles!.Begin(size, _parameters, tile.Index, data, headers, _reduction);
            return _components is not null;
        }

        /// <summary>The tile's component transform, then its samples into the planes.</summary>
        private void FinishTile()
        {
            JpxTileComponent[] components = _components!;
            JpxTileComponent[] reconstructed = _tiles!.Components!;
            ApplyComponentTransform(_parameters!, reconstructed, _buffers!, _reporter);
            for (int c = 0; c < reconstructed.Length; c++)
            {
                _planes[c]?.Write(reconstructed[c], _buffers![c], !reconstructed[c].Style.Reversible, _reporter);
            }

            _ = components;
        }

        /// <summary>Returns the tile's pooled buffers.</summary>
        private void EndTile()
        {
            if (_joined is not null)
            {
                ArrayPool<byte>.Shared.Return(_joined);
                _joined = null;
            }

            if (_buffers is not null)
            {
                foreach (int[]? buffer in _buffers)
                {
                    if (buffer is not null)
                    {
                        ArrayPool<int>.Shared.Return(buffer);
                    }
                }

                _buffers = null;
            }

            _components = null;
            _parameters = null;
        }

        private void Compose()
        {
            JpxOutputPlan plan = _plan!;
            DecodedImageBuilder builder = _builder!;
            JpxComposer.Compose(_output!, plan, _planes, builder, _alpha, _reporter);
            builder.Reduction = _reduction;
            builder.ColorModel = plan.Model;
            if (plan.IccProfile is { } profile)
            {
                builder.IccProfile = profile;
            }

            _result = builder.Build();
            _builder = null;
        }

        private void Finish()
        {
            _stage = Stage.Done;
            Release();
        }

        private void Release()
        {
            EndTile();
            _tiles?.Dispose();
            _tiles = null;
            _builder?.Dispose();
            _builder = null;
            foreach (JpxPlane? plane in _planes)
            {
                plane?.Dispose();
            }

            _planes = [];
        }

        public void Dispose()
        {
            _stage = Stage.Done;
            Release();
            _result?.Dispose();
            _result = null;
        }
    }

    /// <summary>A JPEG 2000 decode in slices for the image path (issue #365), over the data it keeps.</summary>
    internal sealed class SlicedDecode(ReadOnlyMemory<byte> encoded, ImageFilterContext context) : ImageDecodeJob
    {
        private readonly JpxDecodeRun _run = new(context);
        private bool _done;

        public override bool Continue(long deadline, CancellationToken cancellationToken)
        {
            if (_done)
            {
                return true;
            }

            _run.Reporter.SliceToken = cancellationToken;
            try
            {
                if (!_run.Continue(encoded.Span, deadline))
                {
                    return false;
                }
            }
            catch
            {
                _done = true;
                _run.Dispose();
                throw;
            }

            _done = true;
            Result = _run.TakeResult();
            _run.Dispose();
            return true;
        }

        public override void Dispose()
        {
            _done = true;
            _run.Dispose();
        }
    }

    private static bool CheckLimits(JpxCodestream stream, JpxImageSize output, JpxOutputPlan plan, ImageFilterContext context, CodecReporter reporter)
    {
        JpxImageSize size = stream.Size;
        foreach (JpxTile tile in stream.Tiles)
        {
            (long tx0, long ty0, long tx1, long ty1) = size.TileBounds(tile.Index);
            long samples = (tx1 - tx0) * (ty1 - ty0) * size.Components.Length;
            if (samples > context.MaxPixels)
            {
                reporter.Report(
                    JpxDiagnostics.JpxLimitExceeded,
                    DiagnosticSeverity.Error,
                    $"A JPEG 2000 tile holds {samples} samples over its components, more than the limit of {context.MaxPixels}; the image is not decoded.");
                return false;
            }
        }

        long planes = 0;
        for (int c = 0; c < size.Components.Length; c++)
        {
            if (plan.UsedComponents[c])
            {
                planes += JpxPlane.Area(output, c);
            }
        }

        if (planes > context.MaxPixels || planes > Array.MaxLength)
        {
            reporter.Report(
                JpxDiagnostics.JpxLimitExceeded,
                DiagnosticSeverity.Error,
                $"The JPEG 2000 components the image needs hold {planes} samples, more than the limit of {context.MaxPixels}; the image is not decoded.");
            return false;
        }

        return true;
    }

    /// <summary>How many samples a whole-component loop handles between cancellation checks, less one (a mask: 64 Ki samples).</summary>
    private const int CancellationStride = (1 << 16) - 1;

    /// <summary>
    /// The inverse component transformation of components 0 to 2 (G.2.2, G.3.2): the RCT when they use the 5/3 filter, the ICT when
    /// they use the 9/7 one (<see cref="JpxLifting"/>, 64 Ki samples between cancellation checks); skipped with a diagnostic when they
    /// differ in size or filter.
    /// </summary>
    private static void ApplyComponentTransform(JpxTileParameters parameters, JpxTileComponent[] components, int[][] buffers, CodecReporter reporter)
    {
        if (parameters.Coding.Transform != 1 || components.Length < 3)
        {
            return;
        }

        bool reversible = components[0].Style.Reversible;
        if (components[1].Width != components[0].Width || components[2].Width != components[0].Width
            || components[1].Height != components[0].Height || components[2].Height != components[0].Height
            || components[1].Style.Reversible != reversible || components[2].Style.Reversible != reversible)
        {
            reporter.Report(
                JpxDiagnostics.JpxMctSkipped,
                DiagnosticSeverity.Warning,
                "The JPEG 2000 component transformation needs components 0 to 2 of one size and one wavelet filter; it is not applied.");
            return;
        }

        int count = components[0].Width * components[0].Height;
        for (int start = 0; start < count; start += CancellationStride + 1)
        {
            reporter.ThrowIfCancellationRequested();
            int length = Math.Min(CancellationStride + 1, count - start);
            Span<int> y0 = buffers[0].AsSpan(start, length);
            Span<int> y1 = buffers[1].AsSpan(start, length);
            Span<int> y2 = buffers[2].AsSpan(start, length);
            if (reversible)
            {
                JpxLifting.InverseRct(y0, y1, y2);
            }
            else
            {
                JpxLifting.InverseIct(MemoryMarshal.Cast<int, float>(y0), MemoryMarshal.Cast<int, float>(y1), MemoryMarshal.Cast<int, float>(y2));
            }
        }
    }
}
