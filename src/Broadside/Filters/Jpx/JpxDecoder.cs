using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Images;
using Broadside.Parsing;

namespace Broadside.Filters.Jpx;

/// <summary>
/// Decodes a JPEG 2000 file or codestream into a <see cref="DecodedImage"/>: locates the codestream, reads its headers, decodes each
/// tile, applies the inverse component transform and DC level shift, and writes the samples in the §8.9.3 layout.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.9; ITU-T T.800 | ISO/IEC 15444-1 Annexes A to G and I. The output has one channel per component of the PDF
/// colour space when the dictionary has one (the first components; zeros with a diagnostic when the codestream has fewer), else one
/// per codestream component; its bits per component are the components' greatest precision (at most 16). Components of a smaller
/// precision are scaled to it so the full range maps to the full range; samples keep their precision otherwise (§8.9.5.2: the
/// Decode domain is 0 to 2^n - 1 with n the JPX precision). Signed components are offset by 2^(n-1) to be unsigned.
/// </para>
/// <para>
/// Not decoded yet (<c>JpxUnsupportedFeature</c>, Information, no image): sub-sampled components, the 9/7 irreversible filter and
/// ICT, progressions other than LRCP and RLCP, progression order changes, regions of interest, packed packet headers, and the
/// bypass, termination-on-each-pass and vertically-causal code-block styles. Multiple tiles and tile-parts are decoded.
/// </para>
/// </remarks>
internal static class JpxDecoder
{
    /// <summary>Reads the codestream's size, the output's channels and its precision.</summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> data, ImageFilterContext context, out ImageHeader header)
    {
        header = default;
        var reporter = new JpxReporter(context.Filter);
        if (JpxCodestreamReader.Locate(data, reporter) is not { } range
            || JpxCodestreamReader.ReadSize(data.Slice(range.Offset, range.Length), reporter) is not { } size)
        {
            return false;
        }

        (int channels, int bits) = Output(size, context);
        header = new ImageHeader(
            (int)Math.Min(size.Width - size.OriginX, int.MaxValue),
            (int)Math.Min(size.Height - size.OriginY, int.MaxValue),
            channels,
            bits);
        return true;
    }

    /// <summary>Decodes the image, or returns <see langword="null"/> after reporting why it cannot be.</summary>
    public static DecodedImage? Decode(ReadOnlySpan<byte> data, ImageFilterContext context)
    {
        var reporter = new JpxReporter(context.Filter);
        if (JpxCodestreamReader.Locate(data, reporter) is not { } range)
        {
            reporter.Report(DiagnosticCodes.JpxSignatureInvalid, DiagnosticSeverity.Error, "The JPXDecode data holds no JPEG 2000 codestream.");
            return null;
        }

        ReadOnlySpan<byte> codestream = data.Slice(range.Offset, range.Length);
        if (JpxCodestreamReader.Read(codestream, reporter) is not { } stream || !CheckSupported(stream, reporter))
        {
            return null;
        }

        JpxImageSize size = stream.Size;
        long width = size.Width - size.OriginX;
        long height = size.Height - size.OriginY;
        (int channels, int bits) = Output(size, context);
        if (width > int.MaxValue || height > int.MaxValue)
        {
            reporter.Report(DiagnosticCodes.JpxLimitExceeded, DiagnosticSeverity.Error, $"The JPEG 2000 image of {width} x {height} samples is too large.");
            return null;
        }

        if (channels > size.Components.Length)
        {
            reporter.Report(
                DiagnosticCodes.JpxChannelCountMismatch,
                DiagnosticSeverity.Warning,
                $"The JPEG 2000 codestream has {size.Components.Length} components where the colour space needs {channels}; the missing ones are zero.");
        }

        foreach (JpxTile tile in stream.Tiles)
        {
            (long tx0, long ty0, long tx1, long ty1) = size.TileBounds(tile.Index);
            long samples = (tx1 - tx0) * (ty1 - ty0) * size.Components.Length;
            if (samples > context.MaxPixels)
            {
                reporter.Report(
                    DiagnosticCodes.JpxLimitExceeded,
                    DiagnosticSeverity.Error,
                    $"A JPEG 2000 tile holds {samples} samples over its components, more than the limit of {context.MaxPixels}; the image is not decoded.");
                return null;
            }
        }

        DecodedImageBuilder? builder = null;
        try
        {
            if (!context.TryCreateImage((int)width, (int)height, Math.Clamp(channels, 1, ImageGeometry.MaxComponents), bits, out builder))
            {
                return null;
            }

            using var tiles = new JpxTileDecoder(reporter);
            foreach (JpxTile tile in stream.Tiles)
            {
                DecodeTile(stream, tile, codestream, tiles, builder, reporter);
            }

            DecodedImage image = builder.Build();
            builder = null;
            return image;
        }
        finally
        {
            builder?.Dispose();
        }
    }

    /// <summary>The number of output channels and their bits per component.</summary>
    private static (int Channels, int Bits) Output(JpxImageSize size, ImageFilterContext context)
    {
        int channels = context.IsMask ? 1 : context.ColorComponents > 0 ? context.ColorComponents : size.Components.Length;
        int bits = 1;
        for (int c = 0; c < Math.Min(channels, size.Components.Length); c++)
        {
            bits = Math.Max(bits, Math.Min(size.Components[c].Depth, 16));
        }

        return (channels, bits);
    }

    private static bool CheckSupported(JpxCodestream stream, JpxReporter reporter)
    {
        foreach (JpxComponentInfo component in stream.Size.Components)
        {
            if (component.Dx != 1 || component.Dy != 1)
            {
                reporter.ReportUnsupported("sub-sampled components (XRsiz or YRsiz above 1)");
            }
        }

        Check(stream.Main, reporter);
        foreach (JpxTile tile in stream.Tiles)
        {
            Check(tile.Markers, reporter);
        }

        return !reporter.Unsupported;

        static void Check(JpxMarkerSet markers, JpxReporter reporter)
        {
            if (markers.Cod is { } coding)
            {
                if (coding.Progression > 1)
                {
                    reporter.ReportUnsupported("a position- or component-first progression (RPCL, PCRL or CPRL)");
                }

                CheckComponent(coding.Component, reporter);
            }

            foreach (JpxComponentStyle? component in markers.Coc)
            {
                if (component is not null)
                {
                    CheckComponent(component, reporter);
                }
            }
        }

        static void CheckComponent(JpxComponentStyle component, JpxReporter reporter)
        {
            if (!component.Reversible)
            {
                reporter.ReportUnsupported("the 9/7 irreversible wavelet filter");
            }

            if ((component.BlockStyle & (JpxComponentStyle.Bypass | JpxComponentStyle.TerminateEachPass | JpxComponentStyle.VerticallyCausal)) != 0)
            {
                reporter.ReportUnsupported("the bypass, termination-on-each-pass or vertically causal code-block styles");
            }
        }
    }

    private static void DecodeTile(JpxCodestream stream, JpxTile tile, ReadOnlySpan<byte> codestream, JpxTileDecoder decoder, DecodedImageBuilder builder, JpxReporter reporter)
    {
        JpxImageSize size = stream.Size;
        (long tx0, long ty0, long tx1, long ty1) = size.TileBounds(tile.Index);
        int count = size.Components.Length;
        long area = (tx1 - tx0) * (ty1 - ty0);
        int length = 0;
        foreach ((int _, int partLength) in tile.Parts)
        {
            length += partLength;
        }

        byte[]? joined = null;
        int[][] buffers = new int[count][];
        try
        {
            ReadOnlySpan<byte> data;
            if (tile.Parts.Count == 1)
            {
                data = codestream.Slice(tile.Parts[0].Offset, tile.Parts[0].Length);
            }
            else
            {
                joined = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
                int written = 0;
                foreach ((int offset, int partLength) in tile.Parts)
                {
                    codestream.Slice(offset, partLength).CopyTo(joined.AsSpan(written));
                    written += partLength;
                }

                data = joined.AsSpan(0, length);
            }

            for (int c = 0; c < count; c++)
            {
                buffers[c] = ArrayPool<int>.Shared.Rent((int)Math.Max(1, area));
            }

            JpxTileParameters parameters = JpxTileParameters.Resolve(stream.Main, tile.Markers);
            if (decoder.Decode(size, parameters, tile.Index, data, buffers) is not { } components)
            {
                return;
            }

            ApplyComponentTransform(parameters, components, buffers, reporter);
            Write(size, components, buffers, builder);
        }
        finally
        {
            if (joined is not null)
            {
                ArrayPool<byte>.Shared.Return(joined);
            }

            foreach (int[]? buffer in buffers)
            {
                if (buffer is not null)
                {
                    ArrayPool<int>.Shared.Return(buffer);
                }
            }
        }
    }

    /// <summary>The inverse reversible component transformation (G.2.2, equations G-6 to G-8) of components 0 to 2.</summary>
    private static void ApplyComponentTransform(JpxTileParameters parameters, JpxTileComponent[] components, int[][] buffers, JpxReporter reporter)
    {
        if (parameters.Coding.Transform != 1 || components.Length < 3)
        {
            return;
        }

        if (components[1].Width != components[0].Width || components[2].Width != components[0].Width
            || components[1].Height != components[0].Height || components[2].Height != components[0].Height)
        {
            reporter.Report(
                DiagnosticCodes.JpxMarkerSegmentInvalid,
                DiagnosticSeverity.Warning,
                "The JPEG 2000 component transformation needs components 0 to 2 of one size; it is not applied.");
            return;
        }

        Span<int> y0 = buffers[0].AsSpan(0, components[0].Width * components[0].Height);
        Span<int> y1 = buffers[1].AsSpan(0, y0.Length);
        Span<int> y2 = buffers[2].AsSpan(0, y0.Length);
        for (int i = 0; i < y0.Length; i++)
        {
            int g = y0[i] - ((y1[i] + y2[i]) >> 2);
            y0[i] = y2[i] + g;
            y2[i] = y1[i] + g;
            y1[i] = g;
        }
    }

    /// <summary>The inverse DC level shift (G.1.2), clipping, and the samples written into the image at the tile's place.</summary>
    private static void Write(JpxImageSize size, JpxTileComponent[] components, int[][] buffers, DecodedImageBuilder builder)
    {
        int channels = Math.Min(builder.Components, components.Length);
        int bits = builder.BitsPerComponent;
        int storage = builder.StorageBits;
        int outMax = (1 << bits) - 1;
        for (int c = 0; c < channels; c++)
        {
            JpxTileComponent component = components[c];
            int depth = size.Components[c].Depth;
            long shift = 1L << (depth - 1);
            long max = (1L << depth) - 1;
            int left = (int)(component.X0 - size.OriginX);
            int top = (int)(component.Y0 - size.OriginY);
            int[] buffer = buffers[c];
            for (int y = 0; y < component.Height; y++)
            {
                Span<byte> row = builder.GetRow(top + y);
                for (int x = 0; x < component.Width; x++)
                {
                    long value = Math.Clamp(buffer[(y * component.Width) + x] + shift, 0, max);
                    if (depth != bits)
                    {
                        value = ((value * outMax) + (max / 2)) / max;
                    }

                    int index = ((left + x) * builder.Components) + c;
                    Store(row, index, storage, (int)value);
                }
            }
        }
    }

    private static void Store(Span<byte> row, int index, int storage, int value)
    {
        switch (storage)
        {
            case 8:
                row[index] = (byte)value;
                break;
            case 16:
                row[2 * index] = (byte)(value >> 8);
                row[(2 * index) + 1] = (byte)value;
                break;
            default:
                int bit = index * storage;
                row[bit >> 3] |= (byte)(value << (8 - storage - (bit & 7)));
                break;
        }
    }
}
