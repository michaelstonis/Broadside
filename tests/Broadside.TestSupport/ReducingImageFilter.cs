using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.TestSupport;

/// <summary>
/// A test image codec that decodes reduced resolutions (issue #362): its data is raw 8-bit samples, the image dictionary's width and
/// height, with as many components as its colour space; a reduction by 2^n delivers the exact box average of each 2^n × 2^n block
/// (the last ones partial), rounded half up. It counts its decodes per reduction asked for, and can be told to fail.
/// </summary>
/// <remarks>
/// Registered with <see cref="PdfOptions.UseFilter"/> under <c>/BroadsideReduce</c>; <see cref="Page"/> builds a page that paints
/// one such image. It stands in for DCT and JPEG 2000, whose own reduced decodes follow, so that the display list's on-demand
/// decode, the reduction rule, the cache and the rasterizer's geometry can be tested on their own.
/// </remarks>
public sealed class ReducingImageFilter(int maxReduction = 3) : IImageFilter
{
    /// <summary>The filter's name.</summary>
    public static readonly CosName FilterName = new("BroadsideReduce");

    private readonly ConcurrentDictionary<int, int> _decodes = new();
    private int _headers;

    /// <summary>Gets or sets a value indicating whether every decode fails with a <c>TestDecodeFailed</c> error and no samples.</summary>
    public bool Fails { get; set; }

    /// <inheritdoc/>
    public CosName Name => FilterName;

    /// <summary>Gets how often a header was read.</summary>
    public int Headers => _headers;

    /// <summary>Gets how often an image was decoded in all.</summary>
    public int Decodes => _decodes.Values.Sum();

    /// <summary>Returns how often an image was decoded with <paramref name="reduction"/> asked for.</summary>
    /// <param name="reduction">The reduction.</param>
    /// <returns>The count.</returns>
    public int DecodesAt(int reduction) => _decodes.GetValueOrDefault(reduction);

    /// <inheritdoc/>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write(encoded.Span);
    }

    /// <inheritdoc/>
    public bool TryReadHeader(ReadOnlySpan<byte> encoded, ImageFilterContext context, out ImageHeader header)
    {
        ArgumentNullException.ThrowIfNull(context);
        Interlocked.Increment(ref _headers);
        header = new ImageHeader(context.Width, context.Height, Math.Max(1, context.ColorComponents), 8) { MaxReduction = maxReduction };
        return context.Width > 0 && context.Height > 0;
    }

    /// <inheritdoc/>
    public DecodedImage? DecodeImage(ReadOnlyMemory<byte> encoded, ImageFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _decodes.AddOrUpdate(context.Reduction, 1, static (_, count) => count + 1);
        if (Fails)
        {
            context.Filter.Report("TestDecodeFailed", DiagnosticSeverity.Error, "The test codec was told to fail.");
            return null;
        }

        int reduction = Math.Min(context.Reduction, maxReduction);
        int components = Math.Max(1, context.ColorComponents);
        int width = context.Width;
        int height = context.Height;
        int scale = 1 << reduction;
        int reducedWidth = (width + scale - 1) / scale;
        int reducedHeight = (height + scale - 1) / scale;
        DecodedImageBuilder? image = null;
        try
        {
            if (!context.TryCreateImage(reducedWidth, reducedHeight, components, 8, out image))
            {
                return null;
            }

            Fill(encoded.Span, image, width, height, components, scale);
            image.Reduction = reduction;
            DecodedImage decoded = image.Build();
            image = null;
            return decoded;
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static void Fill(ReadOnlySpan<byte> data, DecodedImageBuilder image, int width, int height, int components, int scale)
    {
        int reducedWidth = image.Width;
        int reducedHeight = image.Height;
        for (int y = 0; y < reducedHeight; y++)
        {
            Span<byte> row = image.GetRow(y);
            for (int x = 0; x < reducedWidth; x++)
            {
                for (int c = 0; c < components; c++)
                {
                    int sum = 0;
                    int count = 0;
                    for (int sy = y * scale; sy < Math.Min(height, (y + 1) * scale); sy++)
                    {
                        for (int sx = x * scale; sx < Math.Min(width, (x + 1) * scale); sx++)
                        {
                            int at = (((sy * width) + sx) * components) + c;
                            sum += at < data.Length ? data[at] : 0;
                            count++;
                        }
                    }

                    row[(x * components) + c] = (byte)((sum + (count / 2)) / count);
                }
            }
        }
    }

    /// <summary>
    /// A one-page file (612 x 792 points) whose <paramref name="content"/> paints <c>/Im0</c>, a <paramref name="width"/> ×
    /// <paramref name="height"/> image encoded for this codec.
    /// </summary>
    /// <param name="samples">The raw samples, width × height × components bytes.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="colorSpace">The colour space entry, such as <c>/DeviceGray</c>.</param>
    /// <param name="content">The page content painting <c>/Im0</c>.</param>
    /// <param name="extra">More entries for the image dictionary.</param>
    /// <returns>The file.</returns>
    public static byte[] Page(byte[] samples, int width, int height, string colorSpace, string content, string extra = "")
    {
        ArgumentNullException.ThrowIfNull(samples);
        return ContentSamples.OnePage(
            Encoding.ASCII.GetBytes(content),
            "/XObject << /Im0 5 0 R >>",
            ContentSamples.Stream(
                Encoding.Latin1.GetString(samples),
                FormattableString.Invariant($"/Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace {colorSpace} /BitsPerComponent 8 /Filter /BroadsideReduce {extra}")));
    }

    /// <summary>Deterministic photo-like gray samples: gradients, a few hard edges and fine texture.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="components">The components per sample.</param>
    /// <returns>width × height × components bytes.</returns>
    public static byte[] Photo(int width, int height, int components)
    {
        byte[] samples = new byte[width * height * components];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < components; c++)
                {
                    double gradient = (128 * x / (double)width) + (64 * y / (double)height) + (40 * c);
                    double texture = 24 * Math.Sin((x * 0.7) + (c * 1.3)) * Math.Cos(y * 0.45);
                    double edge = (x / 17 + y / 23) % 3 == 0 ? 50 : 0;
                    samples[(((y * width) + x) * components) + c] = (byte)Math.Clamp(gradient + texture + edge, 0, 255);
                }
            }
        }

        return samples;
    }
}
