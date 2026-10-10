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
        if (JpxCodestreamReader.Locate(data, reporter) is not { } range
            || JpxCodestreamReader.ReadSize(data.Slice(range.Offset, range.Length), reporter) is not { } size)
        {
            return false;
        }

        var quiet = new CodecReporter(context.Filter, silent: true);
        JpxOutputPlan plan = JpxOutputPlan.Create(size, JpxFileHeader.Read(data, quiet), context, quiet);
        header = new ImageHeader(
            (int)Math.Min(size.Width - size.OriginX, int.MaxValue),
            (int)Math.Min(size.Height - size.OriginY, int.MaxValue),
            plan.Colors.Length,
            plan.Bits)
        {
            HasAlpha = plan.Alpha is not null,
            ColorModel = plan.Model,
        };
        return true;
    }

    /// <summary>Decodes the image, or returns <see langword="null"/> after reporting why it cannot be.</summary>
    public static DecodedImage? Decode(ReadOnlySpan<byte> data, ImageFilterContext context)
    {
        var reporter = new CodecReporter(context.Filter);
        if (JpxCodestreamReader.Locate(data, reporter) is not { } range)
        {
            reporter.Report(DiagnosticCodes.JpxSignatureInvalid, DiagnosticSeverity.Error, "The JPXDecode data holds no JPEG 2000 codestream.");
            return null;
        }

        JpxFileHeader file = JpxFileHeader.Read(data, reporter);
        ReadOnlySpan<byte> codestream = data.Slice(range.Offset, range.Length);
        if (JpxCodestreamReader.Read(codestream, reporter) is not { } stream)
        {
            return null;
        }

        if (stream.HighThroughput)
        {
            reporter.Report(
                DiagnosticCodes.JpxHighThroughputUnsupported,
                DiagnosticSeverity.Error,
                "The JPEG 2000 codestream uses high-throughput block coding (HTJ2K, ISO/IEC 15444-15), which is outside the JPX baseline ISO 32000-2 §7.4.9 limits PDF images to; the image is not decoded.");
            return null;
        }

        JpxImageSize size = stream.Size;
        long width = size.Width - size.OriginX;
        long height = size.Height - size.OriginY;
        if (width > int.MaxValue || height > int.MaxValue)
        {
            reporter.Report(DiagnosticCodes.JpxLimitExceeded, DiagnosticSeverity.Error, $"The JPEG 2000 image of {width} x {height} samples is too large.");
            return null;
        }

        JpxOutputPlan plan = JpxOutputPlan.Create(size, file, context, reporter);
        if (!CheckLimits(stream, plan, context, reporter))
        {
            return null;
        }

        DecodedImageBuilder? builder = null;
        var planes = new JpxPlane?[size.Components.Length];
        try
        {
            if (!context.TryCreateImage((int)width, (int)height, Math.Clamp(plan.Colors.Length, 1, ImageGeometry.MaxComponents), plan.Bits, out builder))
            {
                return null;
            }

            DecodedImageBuilder? alpha = null;
            if (plan.Alpha is { } opacity && !builder.TryCreateAlpha(opacity.Depth, plan.Premultiplied, out alpha))
            {
                alpha = null;
            }

            for (int c = 0; c < planes.Length; c++)
            {
                if (plan.UsedComponents[c])
                {
                    planes[c] = new JpxPlane(size, c);
                }
            }

            using (var tiles = new JpxTileDecoder(reporter))
            {
                foreach (JpxTile tile in stream.Tiles)
                {
                    DecodeTile(stream, tile, codestream, tiles, planes, reporter);
                }
            }

            JpxComposer.Compose(size, plan, planes, builder, alpha, reporter);
            builder.ColorModel = plan.Model;
            if (plan.IccProfile is { } profile)
            {
                builder.IccProfile = profile;
            }

            DecodedImage image = builder.Build();
            builder = null;
            return image;
        }
        finally
        {
            builder?.Dispose();
            foreach (JpxPlane? plane in planes)
            {
                plane?.Dispose();
            }
        }
    }

    private static bool CheckLimits(JpxCodestream stream, JpxOutputPlan plan, ImageFilterContext context, CodecReporter reporter)
    {
        JpxImageSize size = stream.Size;
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
                return false;
            }
        }

        long planes = 0;
        for (int c = 0; c < size.Components.Length; c++)
        {
            if (plan.UsedComponents[c])
            {
                planes += JpxPlane.Area(size, c);
            }
        }

        if (planes > context.MaxPixels || planes > Array.MaxLength)
        {
            reporter.Report(
                DiagnosticCodes.JpxLimitExceeded,
                DiagnosticSeverity.Error,
                $"The JPEG 2000 components the image needs hold {planes} samples, more than the limit of {context.MaxPixels}; the image is not decoded.");
            return false;
        }

        return true;
    }

    private static void DecodeTile(JpxCodestream stream, JpxTile tile, ReadOnlySpan<byte> codestream, JpxTileDecoder decoder, JpxPlane?[] planes, CodecReporter reporter)
    {
        JpxImageSize size = stream.Size;
        (long tx0, long ty0, long tx1, long ty1) = size.TileBounds(tile.Index);
        int count = size.Components.Length;
        long area = (tx1 - tx0) * (ty1 - ty0);
        int length = 0;
        foreach ((int _, int _, int partLength) in tile.Parts)
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
                foreach ((int _, int offset, int partLength) in tile.Parts)
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
            byte[]? headers = tile.PackedHeaders();
            if (headers is null && stream.HasMainPacketHeaders)
            {
                headers = [];
            }

            if (decoder.Decode(size, parameters, tile.Index, data, headers, buffers) is not { } components)
            {
                return;
            }

            ApplyComponentTransform(parameters, components, buffers, reporter);
            for (int c = 0; c < count; c++)
            {
                if (!components[c].Style.Reversible)
                {
                    RoundToIntegers(buffers[c].AsSpan(0, components[c].Width * components[c].Height));
                }

                planes[c]?.Write(components[c], buffers[c]);
            }
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

    /// <summary>
    /// The inverse component transformation of components 0 to 2 (G.2.2, G.3.2): the RCT when they use the 5/3 filter, the ICT when
    /// they use the 9/7 one; skipped with a diagnostic when they differ in size or filter.
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
                DiagnosticCodes.JpxMctSkipped,
                DiagnosticSeverity.Warning,
                "The JPEG 2000 component transformation needs components 0 to 2 of one size and one wavelet filter; it is not applied.");
            return;
        }

        int count = components[0].Width * components[0].Height;
        if (reversible)
        {
            // G-6 to G-8.
            Span<int> y0 = buffers[0].AsSpan(0, count);
            Span<int> y1 = buffers[1].AsSpan(0, count);
            Span<int> y2 = buffers[2].AsSpan(0, count);
            for (int i = 0; i < y0.Length; i++)
            {
                int g = y0[i] - ((y1[i] + y2[i]) >> 2);
                y0[i] = y2[i] + g;
                y2[i] = y1[i] + g;
                y1[i] = g;
            }

            return;
        }

        // G-12 to G-14.
        Span<float> f0 = MemoryMarshal.Cast<int, float>(buffers[0].AsSpan(0, count));
        Span<float> f1 = MemoryMarshal.Cast<int, float>(buffers[1].AsSpan(0, count));
        Span<float> f2 = MemoryMarshal.Cast<int, float>(buffers[2].AsSpan(0, count));
        for (int i = 0; i < f0.Length; i++)
        {
            float y = f0[i];
            float cb = f1[i];
            float cr = f2[i];
            f0[i] = y + (1.402f * cr);
            f1[i] = y - (0.34413f * cb) - (0.71414f * cr);
            f2[i] = y + (1.772f * cb);
        }
    }

    /// <summary>Rounds an irreversible tile-component's reconstructed values to integers in place (G.1.2, before the DC level shift).</summary>
    private static void RoundToIntegers(Span<int> buffer)
    {
        Span<float> values = MemoryMarshal.Cast<int, float>(buffer);
        for (int i = 0; i < buffer.Length; i++)
        {
            float value = values[i];
            buffer[i] = value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : float.IsNaN(value) ? 0 : (int)MathF.Round(value);
        }
    }
}
