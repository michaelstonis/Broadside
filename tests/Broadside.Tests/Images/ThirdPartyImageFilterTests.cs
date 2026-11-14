using System.Buffers;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using static Broadside.Tests.Images.ImageTesting;

namespace Broadside.Tests.Images;

/// <summary>
/// An image codec written the way a third party writes one, against the public contract only: it takes the image's geometry as one
/// value from <see cref="ImageFilterContext.Geometry"/>, reports its own as one <see cref="ImageGeometry"/> in its
/// <see cref="ImageHeader"/>, and creates the decoded image from one <see cref="ImageGeometry"/>. ISO 32000-2 §8.9.5.1, Table 87.
/// </summary>
public sealed class ThirdPartyImageFilterTests
{
    [Fact]
    public void A_codec_sizes_the_image_it_creates_from_the_geometry_the_context_gives_it()
    {
        var codec = new RowFillCodec();
        using PdfDocument document = Open(codec, "/Width 3 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /AcmeRowFillDecode");

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal(new ImageGeometry(3, 2, 3, 8), codec.Seen!.Geometry);
        Assert.Equal(new ImageGeometry(3, 2, 3, 8), decoded.Geometry);
        Assert.Equal(new ImageGeometry(3, 2, 3, 8), codec.Header);
        Assert.All(decoded.Samples.ToArray(), value => Assert.Equal(0x5A, value));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_context_says_what_the_dictionary_says_about_masks_and_opacity()
    {
        var codec = new RowFillCodec();
        using PdfDocument document = Open(codec, "/Width 8 /Height 1 /ImageMask true /Filter /AcmeRowFillDecode");

        PdfImage image = document.Pages[0].GetImage("Im0")!;
        using DecodedImage decoded = image.Decode()!;

        Assert.True(codec.Seen!.IsImageMask);
        Assert.Equal(PdfSoftMaskInData.None, codec.Seen.SoftMaskInData);
        Assert.Equal(new ImageGeometry(8, 1, 1, 1), codec.Seen.Geometry);
        Assert.True(image.IsImageMask);
    }

    [Fact]
    public void The_decoded_size_limit_has_the_same_name_and_value_in_the_options_and_the_contexts()
    {
        var codec = new RowFillCodec();
        PdfOptions options = new PdfOptions().UseFilter(codec).WithMaxDecodedLength(123_456);
        using PdfDocument document = PdfDocument.Open(
            OneImage("/Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /AcmeRowFillDecode", "codestream"),
            options);

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal(123_456, codec.Seen!.MaxDecodedLength);
        Assert.Equal(123_456, codec.Seen.Filter.MaxDecodedLength);
        Assert.Equal(123_456, options.MaxDecodedLength);
    }

    [Fact]
    public void A_geometry_over_the_limit_is_refused_by_the_context_before_any_memory_is_rented()
    {
        var context = new ImageFilterContext(new FilterContext()) { MaxPixels = 15 };

        DecodedImageBuilder? rejected = null;
        try
        {
            Assert.False(context.TryCreateImage(new ImageGeometry(4, 4, 1, 8), out rejected));
            Assert.Null(rejected);
        }
        finally
        {
            rejected?.Dispose();
        }

        Assert.True(context.TryCreateImage(new ImageGeometry(3, 5, 1, 8), out DecodedImageBuilder? created));
        using (created)
        {
            Assert.Equal(new ImageGeometry(3, 5, 1, 8), created!.Geometry);
        }
    }

    private static PdfDocument Open(RowFillCodec codec, string entries) =>
        PdfDocument.Open(OneImage(entries, "codestream"), new PdfOptions().UseFilter(codec));

    /// <summary>A codec that fills every sample with 0x5A (or, for an image mask, every bit with 1) at the size the dictionary gives.</summary>
    private sealed class RowFillCodec : IImageFilter
    {
        public CosName Name { get; } = new("AcmeRowFillDecode");

        public ImageFilterContext? Seen { get; private set; }

        public ImageGeometry? Header { get; private set; }

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
        }

        public bool TryReadHeader(ReadOnlySpan<byte> encoded, ImageFilterContext context, out ImageHeader header)
        {
            header = new ImageHeader(context.Geometry);
            Header = context.Geometry;
            return true;
        }

        public DecodedImage? DecodeImage(ReadOnlyMemory<byte> encoded, ImageFilterContext context)
        {
            Seen = context;
            if (!context.TryCreateImage(context.Geometry, out DecodedImageBuilder? builder))
            {
                return null;
            }

            using (builder)
            {
                builder.Samples.Fill(context.IsImageMask ? (byte)0xFF : (byte)0x5A);
                return builder.Build();
            }
        }
    }
}
