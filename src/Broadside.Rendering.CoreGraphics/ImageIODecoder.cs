using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using Broadside.Images;
using Foundation;
using ObjCRuntime;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>
/// Apple's ImageIO JPEG decoder behind the platform image decoder seam, for documents drawn with the CoreGraphics backend: 1.4 to
/// 2.3 times as fast as the managed decoder at full size on macOS, for the images it decodes within the tolerance (iOS devices are
/// unmeasured; in the iOS simulator it is slower than the managed decoder). Not registered by default
/// (its samples are close to the managed decoder's, not identical): register it with
/// <c>new PdfOptions().UsePlatformImageDecoder(ImageIODecoder.Shared)</c> and await
/// <see cref="CoreGraphicsRenderer.PrefetchImagesAsync"/> before a list render.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8 (DCTDecode) and §8.9.3. The data goes to <c>CGImageSourceCreateWithData</c> without a copy and without
/// caching, with <c>public.jpeg</c> as the type hint and, where the system has <c>kCGImageSourceAllowableTypes</c> (macOS and iOS
/// 27), as the only type ImageIO may parse; <c>CGImageSourceCreateImageAtIndex</c> decodes it at once
/// (<c>kCGImageSourceShouldCacheImmediately</c>), and the image's provider bytes (<c>CGDataProviderCopyData</c>), which ImageIO
/// leaves in the codestream's own colors with no Exif orientation applied, are repacked to 1 or 3 components in the samples the
/// core owns (pooled). The decode is synchronous on the thread that awaits the prefetch; a viewer's tile scheduler runs it off the
/// UI thread.
/// </para>
/// <para>
/// ImageIO is not bit-exact against the managed decoder, and only some images stay within the full-size tolerance of
/// <see cref="PlatformImageFidelity.Tolerance"/> (measured on macOS 27.2 over the real-world corpus; see
/// <c>docs/benchmarks/imageio-image-decoder.md</c>). It upsamples subsampled chroma by interpolation, which puts 4:2:0 and 4:2:2
/// images 25 to 50 dB from the managed decoder, so it takes only images whose components share one sampling (gray and 4:4:4). Its
/// IDCT differs from the managed one in proportion to the quantizer's steps, so it decodes only images whose first component's
/// quantization table has a mean step of at most 12 (about IJG quality 90 and finer); a coarser image decodes with the managed
/// codec, with an <c>ImageDecodedManaged</c> diagnostic. Its reduced decode (<c>kCGImageSourceSubsampleFactor</c>) is 45 to 53 dB
/// from the managed reduced decode on photographs, and a full ImageIO decode averaged down is slower than the managed one, so reduced
/// requests are never offered to it: the managed decoder serves them. Twelve-bit and four-component (CMYK, YCCK) data are never
/// offered, and anything ImageIO rejects or returns in another layout decodes with the managed codec, with an
/// <c>ImageDecodedManaged</c> diagnostic.
/// </para>
/// </remarks>
public sealed class ImageIODecoder : IPlatformImageDecoder
{
    /// <summary>The <see cref="Name"/> of the ImageIO decoder: <c>imageio</c>.</summary>
    public const string DecoderName = "imageio";

    /// <summary>The largest mean step of the first component's quantization table that ImageIO decodes within the tolerance (measured).</summary>
    internal const double MaxMeanQuantizerStep = 12;

    private static readonly Lazy<Options> SharedOptions = new(Options.Create);

    private ImageIODecoder()
    {
    }

    /// <summary>Gets the decoder to register; it keeps no state between decodes and serves every engine and thread.</summary>
    public static ImageIODecoder Shared { get; } = new();

    /// <inheritdoc/>
    public string Name => DecoderName;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.8. <see cref="PlatformImageFidelity.Tolerance"/>: ImageIO's IDCT is not libjpeg's.</remarks>
    public PlatformImageFidelity Fidelity => PlatformImageFidelity.Tolerance;

    /// <inheritdoc/>
    /// <remarks>
    /// ITU-T T.81 Annex D: ImageIO decodes arithmetic-coded frames. It applies no Exif orientation without
    /// <c>kCGImageSourceCreateThumbnailWithTransform</c> and converts no embedded profile in the provider's bytes, so neither payload
    /// needs zero-filling.
    /// </remarks>
    public PlatformImageFeatures Features => PlatformImageFeatures.ArithmeticCoding;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §7.4.8 and ITU-T T.81 A.1.1: DCTDecode, 8-bit samples of 1 or 3 components that share one sampling, at the
    /// image's own resolution (reduction 0).
    /// </remarks>
    public bool CanDecode(in PlatformImageRequest request) =>
        request.Filter.Value == "DCTDecode" && request.BitsPerComponent == 8 && request.Components is 1 or 3 && !request.ChromaSubsampled
        && request.Reduction == 0;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">The quantizer is too coarse for ImageIO to stay within the tolerance; the core decodes the image with the managed codec.</exception>
    public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        double step = MeanQuantizerStep(encoded.Span);
        if (!(step <= MaxMeanQuantizerStep))
        {
            throw new NotSupportedException(string.Create(
                CultureInfo.InvariantCulture,
                $"its quantizer's mean step is {step:0.#}; ImageIO stays within the tolerance up to {MaxMeanQuantizerStep}"));
        }

        return ValueTask.FromResult(Decode(encoded, request, subsample: 0));
    }

    /// <summary>
    /// Returns the mean of the 64 steps of the quantization table the frame's first component uses (ITU-T T.81 B.2.2 Tq, B.2.4.1);
    /// <see cref="double.NaN"/> when the markers before the first scan do not define it.
    /// </summary>
    internal static double MeanQuantizerStep(ReadOnlySpan<byte> jpeg)
    {
        Span<int> sums = [-1, -1, -1, -1];
        int table = -1;
        int i = 2;
        while (i + 4 <= jpeg.Length && jpeg[i] == 0xFF)
        {
            int marker = jpeg[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            int length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            int end = Math.Min(jpeg.Length, i + 2 + length);
            if (marker == 0xDB)
            {
                for (int p = i + 4; p < end;)
                {
                    int precision = jpeg[p] >> 4;
                    int id = jpeg[p] & 0x0F;
                    int size = precision == 0 ? 64 : 128;
                    if (id > 3 || p + 1 + size > end)
                    {
                        break;
                    }

                    int sum = 0;
                    for (int k = 0; k < 64; k++)
                    {
                        sum += precision == 0 ? jpeg[p + 1 + k] : (jpeg[p + 1 + (2 * k)] << 8) | jpeg[p + 2 + (2 * k)];
                    }

                    sums[id] = sum;
                    p += 1 + size;
                }
            }
            else if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                // The first component's Tq: P, Y, X, Nf, then C1, H1V1, Tq1.
                table = i + 12 < end ? jpeg[i + 12] & 0x0F : 4;
            }
            else if (marker == 0xDA)
            {
                break;
            }

            i += 2 + length;
        }

        return table is >= 0 and <= 3 && sums[table] >= 0 ? sums[table] / 64.0 : double.NaN;
    }

    /// <summary>
    /// Decodes <paramref name="encoded"/> with ImageIO, at a reduction of 2^<paramref name="subsample"/> with
    /// <c>kCGImageSourceSubsampleFactor</c> (measurements only) or at full size; <see langword="null"/> when ImageIO cannot, or
    /// returns another layout or size.
    /// </summary>
    internal static unsafe DecodedImage? Decode(ReadOnlyMemory<byte> encoded, in PlatformImageRequest request, int subsample)
    {
        Options options = SharedOptions.Value;
        subsample = Math.Clamp(subsample, 0, Math.Min(request.Reduction, 3));
        using MemoryHandle pin = encoded.Pin();
        IntPtr data = Native.CFDataCreateWithBytesNoCopy(IntPtr.Zero, (IntPtr)pin.Pointer, encoded.Length, Native.AllocatorNull);
        IntPtr source = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        IntPtr pixels = IntPtr.Zero;
        try
        {
            if (data == IntPtr.Zero)
            {
                return null;
            }

            source = Native.CGImageSourceCreateWithData(data, options.Source.Handle);
            if (source == IntPtr.Zero || Native.CGImageSourceGetCount(source) < 1)
            {
                return null;
            }

            image = Native.CGImageSourceCreateImageAtIndex(source, 0, options.Image[subsample].Handle);
            if (image == IntPtr.Zero)
            {
                return null;
            }

            int width = (int)Native.CGImageGetWidth(image);
            int height = (int)Native.CGImageGetHeight(image);
            int delivered = DeliveredReduction(request, width, height, subsample);
            if (delivered < 0 || !Layout.TryRead(image, request.Components, out Layout layout))
            {
                return null;
            }

            pixels = Native.CGDataProviderCopyData(Native.CGImageGetDataProvider(image));
            long length = pixels == IntPtr.Zero ? 0 : Native.CFDataGetLength(pixels);
            if (length < ((long)layout.BytesPerRow * (height - 1)) + ((long)width * layout.PixelBytes) || length > int.MaxValue)
            {
                return null;
            }

            var bytes = new ReadOnlySpan<byte>((void*)Native.CFDataGetBytePtr(pixels), (int)length);
            DecodedImageBuilder builder = request.CreateImage(delivered);
            try
            {
                Repack(bytes, layout, width, height, request.Components, builder);
                return builder.Build();
            }
            finally
            {
                builder.Dispose();
            }
        }
        finally
        {
            Native.Release(pixels);
            Native.Release(image);
            Native.Release(source);
            Native.Release(data);
        }
    }

    /// <summary>
    /// The reduction ImageIO delivered, read from its size: r where its width and height are ⌊W / 2^r⌋ or ⌈W / 2^r⌉ (ImageIO
    /// reports the floor); −1 for any other size.
    /// </summary>
    internal static int DeliveredReduction(in PlatformImageRequest request, int width, int height, int asked)
    {
        for (int r = asked; r >= 0; r--)
        {
            if (Fits(request.Width, width, r) && Fits(request.Height, height, r))
            {
                return r;
            }
        }

        return -1;

        static bool Fits(int size, int delivered, int r) => delivered == size >> r || delivered == (int)(((long)size + (1L << r) - 1) >> r);
    }

    /// <summary>
    /// Copies the provider's pixels into the builder's components, replicating the last column and row where ImageIO's reduced grid,
    /// ⌊W / s⌋ samples, lacks the partial ones of the ⌈W / s⌉ the core expects.
    /// </summary>
    private static void Repack(ReadOnlySpan<byte> pixels, in Layout layout, int width, int height, int components, DecodedImageBuilder builder)
    {
        int stride = layout.PixelBytes;
        for (int y = 0; y < builder.Geometry.Height; y++)
        {
            ReadOnlySpan<byte> source = pixels.Slice(Math.Min(y, height - 1) * layout.BytesPerRow, width * stride);
            Span<byte> row = builder.GetRow(y);
            if (components == 1)
            {
                if (stride == 1)
                {
                    source.CopyTo(row);
                }
                else
                {
                    for (int x = 0, i = layout.Red; x < width; x++, i += stride)
                    {
                        row[x] = source[i];
                    }
                }
            }
            else
            {
                int r = layout.Red;
                int g = layout.Green;
                int b = layout.Blue;
                for (int x = 0, i = 0, o = 0; x < width; x++, i += stride, o += 3)
                {
                    row[o] = source[i + r];
                    row[o + 1] = source[i + g];
                    row[o + 2] = source[i + b];
                }
            }

            for (int x = width; x < builder.Geometry.Width; x++)
            {
                row.Slice((width - 1) * components, components).CopyTo(row[(x * components)..]);
            }
        }
    }

    /// <summary>Where the components are in a pixel of the provider's bytes.</summary>
    private readonly record struct Layout(int BytesPerRow, int PixelBytes, int Red, int Green, int Blue)
    {
        /// <summary>Reads an 8-bit gray or RGB layout with or without a skipped byte; false for anything else (16-bit, float, CMYK, alpha).</summary>
        public static bool TryRead(IntPtr image, int components, out Layout layout)
        {
            layout = default;
            long bitsPerComponent = (long)Native.CGImageGetBitsPerComponent(image);
            long bitsPerPixel = (long)Native.CGImageGetBitsPerPixel(image);
            long bytesPerRow = (long)Native.CGImageGetBytesPerRow(image);
            uint info = Native.CGImageGetBitmapInfo(image);
            uint alpha = info & 0x1F;
            uint order = info & 0x7000;
            bool floats = (info & 0x100) != 0;
            int model = Native.CGColorSpaceGetModel(Native.CGImageGetColorSpace(image));
            if (bitsPerComponent != 8 || floats || bytesPerRow <= 0 || bytesPerRow > int.MaxValue)
            {
                return false;
            }

            if (components == 1)
            {
                // Monochrome, one byte a pixel and no alpha.
                if (model != 0 || bitsPerPixel != 8 || alpha != 0)
                {
                    return false;
                }

                layout = new Layout((int)bytesPerRow, 1, 0, 0, 0);
                return true;
            }

            if (model != 1)
            {
                return false;
            }

            if (bitsPerPixel == 24 && alpha == 0 && order is 0 or 0x1000)
            {
                layout = new Layout((int)bytesPerRow, 3, 0, 1, 2);
                return true;
            }

            // kCGImageAlphaNoneSkipLast (5) is RGBX and kCGImageAlphaNoneSkipFirst (6) XRGB in memory with the default or big-endian
            // order; a 32-bit little-endian order reverses each.
            if (bitsPerPixel != 32 || alpha is not (5 or 6) || order is not (0 or 0x2000 or 0x4000))
            {
                return false;
            }

            int red = alpha == 5 ? 0 : 1;
            layout = order == 0x2000
                ? new Layout((int)bytesPerRow, 4, 3 - red, 2 - red, 1 - red)
                : new Layout((int)bytesPerRow, 4, red, red + 1, red + 2);
            return true;
        }
    }

    /// <summary>The source and image option dictionaries, made once.</summary>
    private sealed class Options
    {
        private Options(NSDictionary source, NSDictionary[] image)
        {
            Source = source;
            Image = image;
        }

        /// <summary>Gets the source's options: no caching, <c>public.jpeg</c> as the hint and, where the system has the key, as the only type allowed.</summary>
        public NSDictionary Source { get; }

        /// <summary>Gets the image's options per ImageIO subsample exponent 0 to 3: decode at once, at a factor of 2^i.</summary>
        public NSDictionary[] Image { get; }

        public static Options Create()
        {
            IntPtr library = Dlfcn.dlopen(Constants.ImageIOLibrary, 0);
            NSString Key(string name) => Dlfcn.GetStringConstant(library, name) ?? throw new PlatformNotSupportedException($"ImageIO has no {name}.");
            var jpeg = new NSString("public.jpeg");
            var keys = new List<NSObject> { Key("kCGImageSourceShouldCache"), Key("kCGImageSourceTypeIdentifierHint") };
            var values = new List<NSObject> { NSNumber.FromBoolean(false), jpeg };
            if (Dlfcn.GetStringConstant(library, "kCGImageSourceAllowableTypes") is { } allowable)
            {
                keys.Add(allowable);
                values.Add(NSArray.FromNSObjects(jpeg));
            }

            NSDictionary source = NSDictionary.FromObjectsAndKeys(values.ToArray(), keys.ToArray());
            NSString immediately = Key("kCGImageSourceShouldCacheImmediately");
            NSString cache = Key("kCGImageSourceShouldCache");
            NSString factor = Key("kCGImageSourceSubsampleFactor");
            var image = new NSDictionary[4];
            for (int i = 0; i < image.Length; i++)
            {
                image[i] = i == 0
                    ? NSDictionary.FromObjectsAndKeys(new NSObject[] { NSNumber.FromBoolean(true), NSNumber.FromBoolean(false) }, new NSObject[] { immediately, cache })
                    : NSDictionary.FromObjectsAndKeys(new NSObject[] { NSNumber.FromBoolean(true), NSNumber.FromBoolean(false), NSNumber.FromInt32(1 << i) }, new NSObject[] { immediately, cache, factor });
            }

            return new Options(source, image);
        }
    }

    /// <summary>The CoreFoundation, ImageIO and CoreGraphics calls, by handle, so that a decode makes no managed wrappers.</summary>
    private static class Native
    {
        public static readonly IntPtr AllocatorNull = Dlfcn.GetIntPtr(Dlfcn.dlopen(Constants.CoreFoundationLibrary, 0), "kCFAllocatorNull");

        public static void Release(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                CFRelease(handle);
            }
        }

        [DllImport(Constants.CoreFoundationLibrary)]
        public static extern IntPtr CFDataCreateWithBytesNoCopy(IntPtr allocator, IntPtr bytes, nint length, IntPtr bytesDeallocator);

        [DllImport(Constants.CoreFoundationLibrary)]
        public static extern nint CFDataGetLength(IntPtr data);

        [DllImport(Constants.CoreFoundationLibrary)]
        public static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(Constants.CoreFoundationLibrary)]
        public static extern void CFRelease(IntPtr handle);

        [DllImport(Constants.ImageIOLibrary)]
        public static extern IntPtr CGImageSourceCreateWithData(IntPtr data, IntPtr options);

        [DllImport(Constants.ImageIOLibrary)]
        public static extern nint CGImageSourceGetCount(IntPtr source);

        [DllImport(Constants.ImageIOLibrary)]
        public static extern IntPtr CGImageSourceCreateImageAtIndex(IntPtr source, nint index, IntPtr options);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern nint CGImageGetWidth(IntPtr image);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern nint CGImageGetHeight(IntPtr image);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern nint CGImageGetBitsPerComponent(IntPtr image);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern nint CGImageGetBitsPerPixel(IntPtr image);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern nint CGImageGetBytesPerRow(IntPtr image);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern uint CGImageGetBitmapInfo(IntPtr image);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern IntPtr CGImageGetColorSpace(IntPtr image);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern int CGColorSpaceGetModel(IntPtr space);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern IntPtr CGImageGetDataProvider(IntPtr image);

        [DllImport(Constants.CoreGraphicsLibrary)]
        public static extern IntPtr CGDataProviderCopyData(IntPtr provider);
    }
}
