using System.Diagnostics;
using System.Globalization;
using System.Text;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Issue #377, step 1: the measurement behind <see cref="WicImageDecoder"/>'s fidelity. Every eligible JPEG of the DCT vectors and
/// the generated catalogue photographs decodes through both WIC paths (the YCbCr planes put through the managed transform, and WIC's
/// own BGR or gray pixels) at full size and at each reduction, against the managed decoder at the reduction WIC delivered. The table
/// (largest difference, PSNR, share of pixels over 2 levels, times) goes to the job summary on CI and to
/// <c>artifacts/test-results/wic-jpeg-measurement.md</c>. It asserts only that the planes path decodes; the decoder's exactness is asserted by
/// <see cref="WicImageDecoderTests"/>. Results: <c>docs/benchmarks/wic-image-decoder.md</c>.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class WicMeasurementTests
{
    private static readonly string[] Ineligible = ["rgb", "encoded-rgb-untransformed-q90", "dnl"];

    [Fact]
    public void Both_WIC_paths_are_measured_against_the_managed_decoder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "WIC is Windows-only.");
        var table = new StringBuilder();
        table.AppendLine("## WIC JPEG decode against the managed decoder (issue #377)").AppendLine();
        table.AppendLine("| Image | Size | Path | Asked | Delivered | Max | PSNR (dB) | > 2 levels | Managed (ms) | WIC (ms) | Note |");
        table.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        var worst = new Dictionary<(WicPath, bool), (int Max, double Psnr, double Over)>();
        int planesDecoded = 0;
        foreach ((string name, byte[] jpeg) in Images())
        {
            if (!new DctDecodeFilter().TryReadHeader(jpeg, new ImageFilterContext(new FilterContext()), out ImageHeader header)
                || header.BitsPerComponent != 8 || header.Components is not (1 or 3) || header.Height == 0)
            {
                continue;
            }

            for (int reduction = 0; reduction <= 3; reduction++)
            {
                var request = new PlatformImageRequest
                {
                    Filter = new CosName("DCTDecode"),
                    Width = header.Width,
                    Height = header.Height,
                    Components = header.Components,
                    BitsPerComponent = 8,
                    ColorTransform = header.Components == 3,
                    Reduction = reduction,
                };
                foreach (WicPath path in (WicPath[])[WicPath.Planes, WicPath.Pixels])
                {
                    if (path == WicPath.Planes && header.Components != 3)
                    {
                        continue;
                    }

                    int runs = jpeg.Length > 1_000_000 ? 3 : 1;
                    DecodedImage? wic = null;
                    string? reason = null;
                    double wicTime = double.MaxValue;
                    try
                    {
                        for (int run = 0; run < runs; run++)
                        {
                            wic?.Dispose();
                            long start = Stopwatch.GetTimestamp();
                            wic = WicCodec.Decode(jpeg, request, path, out reason);
                            wicTime = Math.Min(wicTime, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                        }
                    }
                    catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
                    {
                        reason = $"{exception.GetType().Name}: {exception.Message}";
                        wic = null;
                    }

                    if (wic is null)
                    {
                        table.AppendLine(Row(name, header, path, reduction, "-", "-", "-", "-", "-", "-", reason?.Replace('|', '/') ?? "null"));
                        continue;
                    }

                    using (wic)
                    {
                        planesDecoded += path == WicPath.Planes ? 1 : 0;
                        double managedTime = double.MaxValue;
                        DecodedImage? managed = null;
                        for (int run = 0; run < runs; run++)
                        {
                            managed?.Dispose();
                            long start = Stopwatch.GetTimestamp();
                            managed = Managed(jpeg, header, wic.Reduction);
                            managedTime = Math.Min(managedTime, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                        }

                        using (managed)
                        {
                            Assert.NotNull(managed);
                            (int max, double psnr, double over) = Compare(wic, managed);
                            bool scaled = wic.Reduction > 0;
                            worst[(path, scaled)] = worst.TryGetValue((path, scaled), out var w)
                                ? (Math.Max(w.Max, max), Math.Min(w.Psnr, psnr), Math.Max(w.Over, over))
                                : (max, psnr, over);
                            table.AppendLine(Row(
                                name,
                                header,
                                path,
                                reduction,
                                wic.Reduction.ToString(CultureInfo.InvariantCulture),
                                max.ToString(CultureInfo.InvariantCulture),
                                Db(psnr),
                                (over * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%",
                                managedTime.ToString("0.0", CultureInfo.InvariantCulture),
                                wicTime.ToString("0.0", CultureInfo.InvariantCulture),
                                string.Empty));
                        }
                    }
                }
            }
        }

        table.AppendLine().AppendLine("| Path | Scaled | Worst max | Worst PSNR (dB) | Worst > 2 levels |").AppendLine("|---|---|---:|---:|---:|");
        foreach (((WicPath path, bool scaled), (int max, double psnr, double over)) in worst.OrderBy(static p => p.Key.Item1).ThenBy(static p => p.Key.Item2))
        {
            table.AppendLine(CultureInfo.InvariantCulture, $"| {path} | {(scaled ? "yes" : "no")} | {max} | {Db(psnr)} | {over * 100:0.###}% |");
        }

        Publish(table.ToString());
        Assert.True(planesDecoded > 0, "WIC decoded no image through its planes.");
    }

    /// <summary>The eligible DCT vectors and the generated photographs, by name.</summary>
    internal static IEnumerable<(string Name, byte[] Jpeg)> Images()
    {
        string directory = Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Filters", "Dct");
        foreach (string file in Directory.GetFiles(directory, "*.jpg").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!Ineligible.Contains(name))
            {
                yield return (name, File.ReadAllBytes(file));
            }
        }

        yield return ("catalogue 4:4:4", CatalogueSamples.Jpeg(2550, 3150, DctChromaSubsampling.None));
        yield return ("catalogue 4:2:0", CatalogueSamples.Jpeg(2550, 3150, DctChromaSubsampling.HorizontalAndVertical));
        yield return ("photo 4:2:2", CatalogueSamples.Jpeg(1321, 949, DctChromaSubsampling.Horizontal));
        yield return ("photo 4:2:0", CatalogueSamples.Jpeg(1321, 949, DctChromaSubsampling.HorizontalAndVertical));
    }

    /// <summary>The managed decode of <paramref name="jpeg"/> at <paramref name="reduction"/>.</summary>
    internal static DecodedImage? Managed(byte[] jpeg, ImageHeader header, int reduction) =>
        new DctDecodeFilter().DecodeImage(jpeg, new ImageFilterContext(new FilterContext())
        {
            Width = header.Width,
            Height = header.Height,
            BitsPerComponent = 8,
            ColorComponents = header.Components,
            Reduction = reduction,
        });

    /// <summary>The largest difference, the PSNR and the share of pixels over 2 levels between two decodes of one size.</summary>
    internal static (int Max, double Psnr, double Over) Compare(DecodedImage a, DecodedImage b)
    {
        Assert.Equal((a.Width, a.Height, a.Components), (b.Width, b.Height, b.Components));
        int components = a.Components;
        int max = 0;
        long over = 0;
        double squares = 0;
        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<byte> rowA = a.GetRow(y)[..(a.Width * components)];
            ReadOnlySpan<byte> rowB = b.GetRow(y)[..(a.Width * components)];
            for (int x = 0; x < rowA.Length; x += components)
            {
                int pixel = 0;
                for (int c = 0; c < components; c++)
                {
                    int d = Math.Abs(rowA[x + c] - rowB[x + c]);
                    squares += d * d;
                    pixel = Math.Max(pixel, d);
                }

                max = Math.Max(max, pixel);
                over += pixel > 2 ? 1 : 0;
            }
        }

        long pixels = (long)a.Width * a.Height;
        double mse = squares / (pixels * components);
        return (max, mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse), over / (double)pixels);
    }

    private static string Db(double psnr) => double.IsPositiveInfinity(psnr) ? "inf" : psnr.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Row(string name, ImageHeader header, WicPath path, int asked, params string[] cells) =>
        string.Create(CultureInfo.InvariantCulture, $"| {name} | {header.Width} x {header.Height} x {header.Components} | {path} | {asked} | {string.Join(" | ", cells)} |");

    /// <summary>Writes the table to the test output, the CI job summary, and the test results folder.</summary>
    private static void Publish(string table)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(table);
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            File.AppendAllText(summary, table);
        }

        string results = Path.Combine(Corpus.Directory, "..", "..", "artifacts", "test-results");
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, "wic-jpeg-measurement.md"), table);
    }
}
