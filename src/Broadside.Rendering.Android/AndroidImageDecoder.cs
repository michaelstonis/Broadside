using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Android.Runtime;
using Broadside.Images;
using Java.Interop;
using AndroidBitmap = Android.Graphics.Bitmap;
using BitmapFactory = Android.Graphics.BitmapFactory;

namespace Broadside.Rendering.Android;

/// <summary>
/// Android's own JPEG decoder behind the platform image decoder seam: <c>BitmapFactory</c>, which is libjpeg-turbo through Skia, with
/// <c>inSampleSize</c> for libjpeg's DCT-scaled decode of a reduced image. Register it with
/// <see cref="AndroidImageDecoderExtensions.UseAndroidImageDecoder"/>, then await
/// <see cref="AndroidCanvasBackend.PrefetchImagesAsync"/> before a render of a display list.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8 (DCTDecode) and §8.9.3. The data is decoded into a software ARGB_8888 bitmap (never a <c>HARDWARE</c> one, which
/// cannot be read back) at <c>inSampleSize</c> 2^r, unscaled and immutable, and its pixels are read in place
/// (<c>AndroidBitmap_lockPixels</c>) into the image's 8-bit gray or RGB samples. <c>ImageDecoder</c> is never used: it applies the Exif
/// orientation; <c>BitmapFactory</c> does not. An embedded ICC profile would be converted to the bitmap's colour space when it is not
/// one Skia can tag the bitmap with, so the core zero-fills the profile's payload first
/// (<see cref="PlatformImageFeatures.ConvertsEmbeddedProfiles"/>): the image dictionary's <c>ColorSpace</c> gives the samples their
/// meaning (§8.9.5.1).
/// </para>
/// <para>
/// Fidelity, measured against the managed decoder on an emulator (Android 15): images whose chroma is not subsampled (4:4:4 and
/// gray) are bit-identical at every reduction, and so are 4:2:0 images reduced (libjpeg's DCT scaling then needs no chroma upsampling).
/// At full size libjpeg's "fancy" upsampling of subsampled chroma interpolates where the managed decoder replicates (measured max 30,
/// 1.08% of pixels over 2 levels on a 4:2:0 photograph, max 37 on sharp chroma edges), and its reduced decode of 4:2:2 chroma is
/// below 55 dB: outside <see cref="PlatformImageFidelity.Tolerance"/>, so a subsampled image at full size is never taken
/// (<see cref="CanDecode"/>) and one reduced is decoded only when it is 4:2:0. Four-component data (CMYK, which Skia converts with an
/// inversion assumed), 12-bit and arithmetic-coded data are never offered and decode with the managed codec; anything else
/// <c>BitmapFactory</c> rejects falls back with an <c>ImageDecodedManaged</c> diagnostic.
/// </para>
/// <para>
/// The decode is synchronous: on a thread-pool thread it runs at once, on any other (such as the main thread) it moves to the
/// thread pool. One instance serves every document and thread.
/// </para>
/// </remarks>
public sealed partial class AndroidImageDecoder : IPlatformImageDecoder
{
    /// <summary>The <see cref="Name"/> of the Android image decoder: <c>android</c>.</summary>
    public const string DecoderName = "android";

    private const string DctDecode = "DCTDecode";

    /// <summary>Gets the decoder <see cref="AndroidImageDecoderExtensions.UseAndroidImageDecoder"/> registers.</summary>
    public static AndroidImageDecoder Shared { get; } = new();

    /// <inheritdoc/>
    public string Name => DecoderName;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §7.4.8. <see cref="PlatformImageFidelity.Tolerance"/>: every image it takes was bit-identical to the managed decode
    /// where measured (Android 15), but Skia's JPEG decoder has changed across Android versions, so it is not declared exact.
    /// </remarks>
    public PlatformImageFidelity Fidelity => PlatformImageFidelity.Tolerance;

    /// <inheritdoc/>
    /// <remarks>
    /// ICC.1 Annex B.4: <c>BitmapFactory</c> converts an embedded profile Skia cannot tag the bitmap with, so the core zero-fills it.
    /// It does not rotate by the Exif orientation, so Exif data is handed over as it is.
    /// </remarks>
    public PlatformImageFeatures Features => PlatformImageFeatures.ConvertsEmbeddedProfiles;

    /// <inheritdoc/>
    /// <remarks>
    /// ITU-T T.81 A.1.1: subsampled chroma only reduced (where libjpeg's DCT scaling replaces its interpolating upsampling); never
    /// arithmetic-coded data, which the core does not offer without <see cref="PlatformImageFeatures.ArithmeticCoding"/>.
    /// </remarks>
    public bool CanDecode(in PlatformImageRequest request) =>
        request.Filter.Value == DctDecode && request.BitsPerComponent == 8 && request.Components is 1 or 3
        && (!request.ChromaSubsampled || request.Reduction > 0);

    /// <inheritdoc/>
    public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ChromaSubsampled && !IsFourTwoZero(encoded.Span))
        {
            throw new NotSupportedException("BitmapFactory's reduced decode of chroma subsampled other than 4:2:0 is outside the tolerance");
        }

        if (Thread.CurrentThread.IsThreadPoolThread)
        {
            return ValueTask.FromResult(Decode(encoded.Span, request));
        }

        return new ValueTask<DecodedImage?>(Task.Run(() => Decode(encoded.Span, request), cancellationToken));
    }

    /// <summary>Decodes <paramref name="data"/> with <c>BitmapFactory</c> at the request's reduction, or <see langword="null"/> when it cannot.</summary>
    internal static DecodedImage? Decode(ReadOnlySpan<byte> data, in PlatformImageRequest request)
    {
        using AndroidBitmap? bitmap = DecodeBitmap(data, request.Reduction);
        if (bitmap is null)
        {
            return null;
        }

        try
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            int reduction = -1;
            for (int r = request.Reduction; r >= 0; r--)
            {
                if (Reduce(request.Width, r) == width && Reduce(request.Height, r) == height)
                {
                    reduction = r;
                    break;
                }
            }

            if (reduction < 0)
            {
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"BitmapFactory delivered {width} x {height} for a {request.Width} x {request.Height} image at inSampleSize {1 << request.Reduction}"));
            }

            DecodedImageBuilder builder = request.CreateImage(reduction);
            try
            {
                Read(bitmap, builder);
                return builder.Build();
            }
            finally
            {
                builder.Dispose();
            }
        }
        finally
        {
            bitmap.Recycle();
        }
    }

    /// <summary>
    /// <c>BitmapFactory.decodeByteArray</c> into a software ARGB_8888 bitmap at <c>inSampleSize</c> 2^<paramref name="reduction"/>,
    /// from a Java array filled straight from the span (the binding's <c>byte[]</c> overload would copy the data in and back again).
    /// </summary>
    private static unsafe AndroidBitmap? DecodeBitmap(ReadOnlySpan<byte> data, int reduction)
    {
        using var options = new BitmapFactory.Options
        {
            InSampleSize = 1 << reduction,
            InPreferredConfig = AndroidBitmap.Config.Argb8888,
            InScaled = false,
            InMutable = false,
        };

        JniObjectReference array = JniEnvironment.Arrays.NewByteArray(data.Length);
        try
        {
            fixed (byte* bytes = data)
            {
                JniEnvironment.Arrays.SetByteArrayRegion(array, 0, data.Length, (sbyte*)bytes);
            }

            JniArgumentValue* arguments = stackalloc JniArgumentValue[4];
            arguments[0] = new JniArgumentValue(array);
            arguments[1] = new JniArgumentValue(0);
            arguments[2] = new JniArgumentValue(data.Length);
            arguments[3] = new JniArgumentValue(options.PeerReference);
            JniObjectReference result = JniEnvironment.StaticMethods.CallStaticObjectMethod(Factory.Type.PeerReference, Factory.DecodeByteArray, arguments);
            return result.IsValid ? Java.Lang.Object.GetObject<AndroidBitmap>(result.Handle, JniHandleOwnership.TransferLocalRef) : null;
        }
        finally
        {
            JniObjectReference.Dispose(ref array);
        }
    }

    /// <summary>Reads the bitmap's pixels in place into the builder's samples: R, G, B of each pixel, or R alone for gray.</summary>
    private static unsafe void Read(AndroidBitmap bitmap, DecodedImageBuilder builder)
    {
        IntPtr env = JniEnvironment.EnvironmentPointer;
        IntPtr handle = bitmap.Handle;
        BitmapInfo info;
        if (NativeMethods.AndroidBitmap_getInfo(env, handle, &info) != 0 || info.Format != NativeMethods.FormatRgba8888
            || info.Width != builder.Geometry.Width || info.Height != builder.Geometry.Height)
        {
            throw new InvalidOperationException(FormattableString.Invariant($"BitmapFactory delivered a bitmap of format {info.Format}, not RGBA_8888"));
        }

        void* pixels;
        if (NativeMethods.AndroidBitmap_lockPixels(env, handle, &pixels) != 0)
        {
            throw new InvalidOperationException("AndroidBitmap_lockPixels failed");
        }

        try
        {
            int width = builder.Geometry.Width;
            for (int y = 0; y < builder.Geometry.Height; y++)
            {
                var row = new ReadOnlySpan<byte>((byte*)pixels + ((nint)y * info.Stride), width * 4);
                if (builder.Geometry.Components == 3)
                {
                    PackRgb(row, builder.GetRow(y));
                }
                else
                {
                    PackGray(row, builder.GetRow(y));
                }
            }
        }
        finally
        {
            _ = NativeMethods.AndroidBitmap_unlockPixels(env, handle);
        }
    }

    /// <summary>Drops every fourth byte: RGBA pixels to RGB samples, four pixels per vector.</summary>
    internal static void PackRgb(ReadOnlySpan<byte> rgba, Span<byte> rgb)
    {
        int pixels = rgba.Length / 4;
        int x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<byte> shuffle = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0, 0, 0, 0);
            ref byte source = ref MemoryMarshal.GetReference(rgba);
            ref byte target = ref MemoryMarshal.GetReference(rgb);

            // Each step stores 16 bytes and keeps 12, so the last pixels are left to the scalar loop.
            for (; x + 6 <= pixels; x += 4)
            {
                Vector128<byte> four = Vector128.LoadUnsafe(ref source, (nuint)(x * 4));
                Vector128.Shuffle(four, shuffle).StoreUnsafe(ref target, (nuint)(x * 3));
            }
        }

        for (; x < pixels; x++)
        {
            rgb[x * 3] = rgba[x * 4];
            rgb[(x * 3) + 1] = rgba[(x * 4) + 1];
            rgb[(x * 3) + 2] = rgba[(x * 4) + 2];
        }
    }

    /// <summary>Keeps the red byte of each RGBA pixel: a gray JPEG decodes to equal R, G and B.</summary>
    internal static void PackGray(ReadOnlySpan<byte> rgba, Span<byte> gray)
    {
        for (int x = 0; x < gray.Length; x++)
        {
            gray[x] = rgba[x * 4];
        }
    }

    /// <summary>
    /// Returns whether the frame header (ITU-T T.81 B.2.2) gives three components sampled 2 × 2, 1 × 1, 1 × 1 (4:2:0): the only
    /// subsampling whose reduced decode matched the managed decoder's.
    /// </summary>
    internal static bool IsFourTwoZero(ReadOnlySpan<byte> jpeg)
    {
        int position = 2;
        while (position + 4 <= jpeg.Length)
        {
            if (jpeg[position] != 0xFF)
            {
                return false;
            }

            byte marker = jpeg[position + 1];
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                position += 2;
                continue;
            }

            int length = (jpeg[position + 2] << 8) | jpeg[position + 3];
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                // P, Y, X, Nf, then (C, Hi Vi, Tq) per component.
                int components = position + 9 < jpeg.Length ? jpeg[position + 9] : 0;
                if (components != 3 || position + 10 + 9 > jpeg.Length)
                {
                    return false;
                }

                return jpeg[position + 11] == 0x22 && jpeg[position + 14] == 0x11 && jpeg[position + 17] == 0x11;
            }

            if (marker is 0xD9 or 0xDA || length < 2)
            {
                return false;
            }

            position += 2 + length;
        }

        return false;
    }

    private static int Reduce(int size, int reduction) => (int)(((long)size + (1L << reduction) - 1) >> reduction);

    /// <summary><c>BitmapFactory</c> and its <c>decodeByteArray(byte[], int, int, Options)</c>, looked up once.</summary>
    private static class Factory
    {
        public static readonly JniType Type = new("android/graphics/BitmapFactory");

        public static readonly JniMethodInfo DecodeByteArray =
            Type.GetStaticMethod("decodeByteArray", "([BIILandroid/graphics/BitmapFactory$Options;)Landroid/graphics/Bitmap;");
    }

    /// <summary>The NDK's <c>AndroidBitmapInfo</c> (android/bitmap.h).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Width;
        public uint Height;
        public uint Stride;
        public int Format;
        public uint Flags;
    }

    /// <summary>The NDK's bitmap functions (libjnigraphics, API level 8 and later): pixels of a software bitmap read in place.</summary>
    private static unsafe partial class NativeMethods
    {
        public const int FormatRgba8888 = 1;

        private const string Library = "jnigraphics";

        [LibraryImport(Library)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int AndroidBitmap_getInfo(IntPtr env, IntPtr bitmap, BitmapInfo* info);

        [LibraryImport(Library)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int AndroidBitmap_lockPixels(IntPtr env, IntPtr bitmap, void** pixels);

        [LibraryImport(Library)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int AndroidBitmap_unlockPixels(IntPtr env, IntPtr bitmap);
    }
}

/// <summary>Registers Android's own image decoder with an engine's options.</summary>
public static class AndroidImageDecoderExtensions
{
    /// <summary>
    /// Has the engine decode JPEG images with Android's <c>BitmapFactory</c> before they are drawn (<see cref="AndroidImageDecoder"/>):
    /// renders of a display list through <see cref="AndroidCanvasBackend"/> use it after
    /// <see cref="AndroidCanvasBackend.PrefetchImagesAsync"/>. Not registered by default.
    /// </summary>
    /// <param name="options">The engine's options.</param>
    /// <returns><paramref name="options"/>.</returns>
    /// <remarks>ISO 32000-2 §7.4.8. <see cref="PdfImage.Decode()"/>, text extraction and export still decode with the managed codec.</remarks>
    public static PdfOptions UseAndroidImageDecoder(this PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UsePlatformImageDecoder(AndroidImageDecoder.Shared);
    }
}
