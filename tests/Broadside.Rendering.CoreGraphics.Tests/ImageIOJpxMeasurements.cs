using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Issue #482's measurements: ImageIO against the managed JPEG 2000 decoder over the JPX images of the files named in
/// <c>BROADSIDE_IMAGEIO_FILES</c> (paths under <c>BROADSIDE_CORPUS_DIR</c>, or absolute), at full size and each resolution level, in
/// each way ImageIO can serve a reduction. Explicit: run with <c>--explicit only</c> and read the output.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ImageIOJpxMeasurements(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public async Task Fidelity_and_time_against_the_managed_decoder_over_the_JPX_images()
    {
        string root = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR") ?? string.Empty;
        string list = Environment.GetEnvironmentVariable("BROADSIDE_IMAGEIO_FILES") ?? string.Empty;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in await File.ReadAllLinesAsync(list, TestContext.Current.CancellationToken))
        {
            PdfPage[] pages;
            using PdfDocument document = await PdfDocument.OpenAsync(Path.Combine(root, file), new PdfOptions().WithDisplayListCacheBudget(0), TestContext.Current.CancellationToken);
            pages = [.. document.Pages.Take(10)];
            for (int p = 0; p < pages.Length; p++)
            {
                var recorder = new ImagesRecorder();
                pages[p].CreateDisplayList().Replay(recorder);
                foreach (DisplayImage image in recorder.Images)
                {
                    if (!image.TryGetPlatformData(PlatformImageFeatures.None, out PlatformImageRequest request, out ReadOnlyMemory<byte> data) || request.Filter.Value != "JPXDecode")
                    {
                        continue;
                    }

                    string hash = Convert.ToHexString(SHA256.HashData(data.Span))[..12];
                    if (!seen.Add(hash))
                    {
                        continue;
                    }

                    Measure(file, p, hash, image, request, data.ToArray());
                }
            }
        }
    }

    [Fact(Explicit = true)]
    public void Fidelity_over_the_OpenJPEG_vectors()
    {
        foreach (JpxVector vector in JpxSamples.Vectors)
        {
            if (vector.Components is not (1 or 3) || vector.Depth != 8)
            {
                Log($"{vector.Name}: {vector.Width}x{vector.Height}x{vector.Components} of {vector.Depth} bits, not offered");
                continue;
            }

            byte[] data = vector.Data;
            var filter = new JpxDecodeFilter();
            var request = new PlatformImageRequest { Filter = new CosName("JPXDecode"), Width = vector.Width, Height = vector.Height, Components = vector.Components, BitsPerComponent = 8 };
            filter.TryReadHeader(data, new ImageFilterContext(new FilterContext()) { Geometry = new ImageGeometry(vector.Width, vector.Height, vector.Components, 8) }, out ImageHeader header);
            for (int r = 0; r <= Math.Min(3, header.MaxReduction); r++)
            {
                using DecodedImage? managed = filter.DecodeImage(data, new ImageFilterContext(new FilterContext()) { Geometry = new ImageGeometry(vector.Width, vector.Height, vector.Components, 8), Reduction = r });
                using DecodedImage? platform = ImageIODecoder.Decode(data, request with { Reduction = r }, r);
                string result = platform is null || managed is null
                    ? "failed"
                    : platform.Reduction != managed.Reduction || platform.Geometry.Width != managed.Geometry.Width || platform.Geometry.Height != managed.Geometry.Height
                        ? $"delivered r{platform.Reduction} {platform.Geometry.Width}x{platform.Geometry.Height} against r{managed.Reduction} {managed.Geometry.Width}x{managed.Geometry.Height}"
                        : ImageIOTesting.Compare(platform, managed).ToString();
                Log($"{vector.Name} {vector.Width}x{vector.Height}x{vector.Components} {(vector.IsLossless ? "lossless" : "lossy")} r{r}: {result}");
            }
        }
    }

    [Fact(Explicit = true)]
    public void ImageIO_and_the_JP2_boxes_the_shared_rule_refuses()
    {
        // The managed decoder's reading of each box (no ColorSpace in the dictionary, so the boxes decide) against ImageIO's.
        byte[] medium = JpxSamples.Vector("Medium").Data;
        byte[] gray = JpxSamples.Vector("Rpcl").Data;
        var cases = new List<(string Name, byte[] Data)>
        {
            ("sRGB (accepted)", JpxEditing.Jp2(medium, JpxEditing.Color(16))),
            ("sYCC", JpxEditing.Jp2(medium, JpxEditing.Color(18))),
            ("CIELab", JpxEditing.Jp2(medium, JpxEditing.Color(14))),
            ("ICC profile", JpxEditing.Jp2(medium, JpxEditing.Color(JpxEditing.IccHeader("RGB ")))),
            ("channel definition, reversed colors", JpxEditing.Jp2(medium, JpxEditing.Color(16), JpxEditing.Definitions((0, 0, 3), (1, 0, 2), (2, 0, 1)))),
            ("palette and mapping", JpxEditing.Jp2(gray, JpxEditing.Color(16), JpxEditing.Palette([.. Enumerable.Range(0, 256).Select(i => new[] { i, 255 - i, i / 2 })]), JpxEditing.Mapping((0, 1, 0), (0, 1, 1), (0, 1, 2)))),
            ("fragmented codestream", JpxEditing.Fragmented(medium, [100], [0, 1])),
            ("fragmented codestream, pieces out of order", JpxEditing.Fragmented(medium, [100], [1, 0])),
        };
        foreach ((string name, byte[] data) in cases)
        {
            var filter = new JpxDecodeFilter();
            Assert.True(filter.TryReadHeader(data, new ImageFilterContext(new FilterContext()), out ImageHeader header));
            ImageGeometry geometry = header.Geometry;
            var request = new PlatformImageRequest { Filter = new CosName("JPXDecode"), Width = geometry.Width, Height = geometry.Height, Components = geometry.Components, BitsPerComponent = 8 };
            using DecodedImage? managed = filter.DecodeImage(data, new ImageFilterContext(new FilterContext()));
            using DecodedImage? platform = ImageIODecoder.Decode(data, request, 0);
            string result = platform is null || managed is null
                ? $"ImageIO {(platform is null ? "failed" : "decoded")}, managed {(managed is null ? "failed" : "decoded")}"
                : platform.Geometry != managed.Geometry
                    ? $"geometry {platform.Geometry} against {managed.Geometry}"
                    : ImageIOTesting.Compare(platform, managed).ToString();
            Log($"{name}: {result}");
        }
    }

    [Fact(Explicit = true)]
    public async Task Which_images_of_the_files_the_core_offers()
    {
        string root = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR") ?? string.Empty;
        string list = Environment.GetEnvironmentVariable("BROADSIDE_IMAGEIO_FILES") ?? string.Empty;
        foreach (string file in await File.ReadAllLinesAsync(list, TestContext.Current.CancellationToken))
        {
            using PdfDocument document = await PdfDocument.OpenAsync(Path.Combine(root, file), new PdfOptions().WithDisplayListCacheBudget(0), TestContext.Current.CancellationToken);
            for (int p = 0; p < Math.Min(document.Pages.Count, 10); p++)
            {
                var recorder = new ImagesRecorder();
                document.Pages[p].CreateDisplayList().Replay(recorder);
                foreach (DisplayImage image in recorder.Images)
                {
                    bool offered = image.TryGetPlatformData(PlatformImageFeatures.None, out PlatformImageRequest request, out _);
                    Log($"{Path.GetFileName(file)} p{p} {image.Geometry.Width}x{image.Geometry.Height}x{image.Geometry.Components}x{image.Geometry.BitsPerComponent} maxred {image.MaxReduction} offered {offered} {(offered ? request.Filter.Value : string.Empty)}");
                }
            }
        }
    }

    private void Measure(string file, int page, string hash, DisplayImage image, PlatformImageRequest request, byte[] data)
    {
        string info;
        try
        {
            info = Describe(data);
        }
#pragma warning disable CA1031 // a measurement: a codestream that is cut short is described as far as it goes
        catch (Exception exception)
#pragma warning restore CA1031
        {
            info = "describe failed: " + exception.GetType().Name;
        }

        Log($"== {Path.GetFileName(file)} p{page} {hash} {request.Width}x{request.Height}x{request.Components} maxred {image.MaxReduction} {info}");
        for (int r = 0; r <= image.MaxReduction; r++)
        {
            PlatformImageRequest at = request with { Reduction = r };
            (Raw? managed, double managedMs) = ManagedAt(file, page, hash, r);
            foreach (string way in r == 0 ? (string[])["full"] : ["subsample", "full then average"])
            {
                (DecodedImage? platform, double ms) = Time(() => way == "subsample"
                    ? ImageIODecoder.Decode(data, at, r)
                    : ImageIOTesting.Average(ImageIODecoder.Decode(data, request, 0), r));
                using (platform)
                {
                    string fidelity = platform is null || managed is null
                        ? "failed"
                        : platform.Reduction != managed.Reduction || platform.Geometry.Width != managed.Width || platform.Geometry.Height != managed.Height
                            ? $"delivered r{platform.Reduction} {platform.Geometry.Width}x{platform.Geometry.Height} against r{managed.Reduction} {managed.Width}x{managed.Height}"
                            : Compare(platform, managed).ToString();
                    Log(string.Create(CultureInfo.InvariantCulture, $"   r{r} {way}: managed {managedMs:0.0} ms, imageio {ms:0.0} ms, {fidelity}"));
                }
            }
        }
    }

    // A fresh document each time, so the managed decode is cold; only the decode is timed.
    private static (Raw? Samples, double Ms) ManagedAt(string file, int page, string hash, int reduction)
    {
        string root = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR") ?? string.Empty;
        double best = double.MaxValue;
        Raw? kept = null;
        for (int i = 0; i < 3; i++)
        {
            using PdfDocument document = PdfDocument.Open(Path.Combine(root, file), new PdfOptions().WithDisplayListCacheBudget(0));
            var recorder = new ImagesRecorder();
            document.Pages[page].CreateDisplayList().Replay(recorder);
            foreach (DisplayImage image in recorder.Images)
            {
                if (!image.TryGetPlatformData(PlatformImageFeatures.None, out _, out ReadOnlyMemory<byte> data) || Convert.ToHexString(SHA256.HashData(data.Span))[..12] != hash)
                {
                    continue;
                }

                long start = Stopwatch.GetTimestamp();
                DisplayImageSamples? samples = image.GetSamplesAt(reduction);
                best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                kept = samples is { } s ? Copy(s) : null;
                break;
            }
        }

        return (kept, best);
    }

    // The samples of an image the document owns, copied so they outlive it.
    private static Raw Copy(DisplayImageSamples samples)
    {
        int row = samples.Width * samples.Components * (samples.BitsPerComponent == 16 ? 2 : 1);
        byte[] bytes = new byte[row * samples.Height];
        for (int y = 0; y < samples.Height; y++)
        {
            samples.GetRow(y)[..row].CopyTo(bytes.AsSpan(y * row));
        }

        return new Raw(samples.Width, samples.Height, samples.Components, samples.BitsPerComponent, samples.Reduction, bytes);
    }

    private sealed record Raw(int Width, int Height, int Components, int BitsPerComponent, int Reduction, byte[] Data)
    {
        public ReadOnlySpan<byte> GetRow(int y) => Data.AsSpan(y * Width * Components * (BitsPerComponent == 16 ? 2 : 1));
    }

    private static Difference Compare(DecodedImage platform, Raw managed)
    {
        int components = platform.Geometry.Components;
        if (managed.Components != components || managed.BitsPerComponent != 8)
        {
            return new Difference(255, 0, 1);
        }

        int max = 0;
        long over = 0;
        double squares = 0;
        for (int y = 0; y < managed.Height; y++)
        {
            ReadOnlySpan<byte> a = platform.GetRow(y)[..(managed.Width * components)];
            ReadOnlySpan<byte> b = managed.GetRow(y)[..(managed.Width * components)];
            for (int x = 0; x < a.Length; x += components)
            {
                int pixel = 0;
                for (int c = 0; c < components; c++)
                {
                    int d = Math.Abs(a[x + c] - b[x + c]);
                    squares += d * d;
                    pixel = Math.Max(pixel, d);
                }

                max = Math.Max(max, pixel);
                over += pixel > 2 ? 1 : 0;
            }
        }

        long pixels = (long)managed.Width * managed.Height;
        double mse = squares / (pixels * components);
        return new Difference(max, mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse), over / (double)pixels);
    }

    /// <summary>The JP2 boxes and the codestream's main header facts that decide how a platform decoder may differ from ours.</summary>
    private static string Describe(byte[] data)
    {
        var parts = new List<string>();
        int codestream = 0;
        if (data.Length > 12 && BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)) == 0x6A502020)
        {
            parts.Add("jp2");
            Boxes(data, 0, data.Length, parts, ref codestream);
        }
        else
        {
            parts.Add("raw");
        }

        // SIZ then COD.
        int i = codestream + 2;
        while (i + 4 <= data.Length && data[i] == 0xFF)
        {
            int marker = data[i + 1];
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i + 2));
            if (marker == 0x51)
            {
                int comps = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i + 38));
                var depths = new List<string>();
                for (int c = 0; c < Math.Min(comps, 8); c++)
                {
                    int ssiz = data[i + 40 + (3 * c)];
                    depths.Add(string.Create(CultureInfo.InvariantCulture, $"{(ssiz & 0x7F) + 1}{((ssiz & 0x80) != 0 ? "s" : "u")}{data[i + 41 + (3 * c)]}x{data[i + 42 + (3 * c)]}"));
                }

                long tileW = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 22));
                long tileH = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 26));
                long width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 6));
                long height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i + 10));
                parts.Add($"comps[{string.Join(',', depths)}] tile {tileW}x{tileH} of {width}x{height}");
            }
            else if (marker == 0x52)
            {
                int scod = data[i + 4];
                int order = data[i + 5];
                int layers = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i + 6));
                int mct = data[i + 8];
                int levels = data[i + 9];
                int style = data[i + 12];
                int transform = data[i + 13];
                parts.Add($"COD prog{order} layers{layers} mct{mct} levels{levels} cbstyle{style} {(transform == 1 ? "5/3" : "9/7")}{((scod & 1) != 0 ? " precincts" : string.Empty)}");
                break;
            }

            i += 2 + length;
        }

        return string.Join("; ", parts);
    }

    private static void Boxes(byte[] data, int start, int end, List<string> parts, ref int codestream)
    {
        int position = start;
        while (position + 8 <= end)
        {
            long length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
            string type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
            int header = 8;
            if (length == 1)
            {
                length = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(position + 8));
                header = 16;
            }
            else if (length == 0)
            {
                length = end - position;
            }

            int body = position + header;
            int stop = (int)Math.Min(end, position + length);
            switch (type)
            {
                case "jp2h":
                    Boxes(data, body, stop, parts, ref codestream);
                    break;
                case "colr":
                    int meth = data[body];
                    parts.Add(meth == 1 ? $"colr enum{BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body + 3))}" : $"colr icc({stop - body - 3} bytes)");
                    break;
                case "pclr":
                    parts.Add("pclr");
                    break;
                case "cmap":
                    parts.Add("cmap");
                    break;
                case "cdef":
                    parts.Add("cdef");
                    break;
                case "bpcc":
                    parts.Add("bpcc");
                    break;
                case "jp2c":
                    codestream = body;
                    break;
                case "jpch" or "jplh" or "ftbl":
                    parts.Add(type);
                    break;
            }

            if (length < header)
            {
                break;
            }

            position += (int)length;
        }
    }

    /// <summary>Writes a line to the test output and, when <c>BROADSIDE_IMAGEIO_LOG</c> names a file, appends it there.</summary>
    private void Log(string line)
    {
        output.WriteLine(line);
        if (Environment.GetEnvironmentVariable("BROADSIDE_IMAGEIO_LOG") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private static (DecodedImage? Image, double Ms) Time(Func<DecodedImage?> decode)
    {
        decode()?.Dispose();
        double best = double.MaxValue;
        DecodedImage? kept = null;
        for (int i = 0; i < 3; i++)
        {
            long start = Stopwatch.GetTimestamp();
            DecodedImage? image = decode();
            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            kept?.Dispose();
            kept = image;
        }

        return (kept, best);
    }

    /// <summary>Collects the images a replay draws.</summary>
    private sealed class ImagesRecorder : IRenderingBackend
    {
        public List<DisplayImage> Images { get; } = [];

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) => Images.Add(image);

        public void SaveState()
        {
        }

        public void RestoreState()
        {
        }

        public void SetTransform(in Matrix transform)
        {
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
        }

        public void IntersectClip(PathView path, FillRule rule)
        {
        }

        public void DrawGlyphs(in GlyphRun run)
        {
        }

        public void DrawShading(in DisplayShading shading)
        {
        }

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
        }

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
        }

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
        }

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
        }

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
        {
        }

        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
        {
        }

        public void SetSoftMask(in DisplaySoftMask mask)
        {
        }

        public void SetDeviceParameters(in DisplayDeviceParameters parameters)
        {
        }

        public void DrawGroup(in DisplayGroup group) => group.Replay(this);

        public void BeginAnnotation(in DisplayAnnotation annotation)
        {
        }

        public void EndAnnotation()
        {
        }
    }
}
