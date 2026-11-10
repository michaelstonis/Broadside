using System.Buffers;
using Broadside.Images;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Broadside.Rendering.Direct2D;

/// <summary>How <see cref="WicCodec"/> takes a JPEG's samples from Windows Imaging Component.</summary>
internal enum WicPath
{
    /// <summary>The Y, Cb and Cr planes (<c>IWICPlanarBitmapSourceTransform</c>), put together as the managed decoder does.</summary>
    Planes,

    /// <summary>WIC's own pixels (<c>IWICBitmapSourceTransform.CopyPixels</c>), 24bppBGR or 8bppGray, at the closest size it scales to.</summary>
    Pixels,
}

/// <summary>
/// The JPEG decode through Windows Imaging Component: one factory (<c>CLSID_WICImagingFactory2</c>) for the process, a decoder over
/// the caller's bytes, frame 0, then the YCbCr planes or WIC's pixels at a power-of-two reduction.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.4.8. Microsoft Learn: <c>IWICBitmapSourceTransform</c> (DCT-domain power-of-two scaling through
/// <c>GetClosestSize</c>), <c>IWICPlanarBitmapSourceTransform</c> (Windows 8.1: <c>8bppY</c>, <c>8bppCb</c>, <c>8bppCr</c> planes of
/// JFIF YCbCr data). WIC's JPEG decoder converts no colour and does not apply the Exif orientation. The in-box codecs are
/// MTA-capable, so the factory serves every thread.
/// </remarks>
internal static unsafe class WicCodec
{
    private const int NotInitialized = unchecked((int)0x800401F0); // CO_E_NOTINITIALIZED

    private static readonly Lock Gate = new();
    private static volatile IWICImagingFactory* _factory;
    private static volatile bool _unavailable;

    /// <summary>Gets a value indicating whether WIC's factory could be created (false off Windows).</summary>
    public static bool IsAvailable => OperatingSystem.IsWindows() && Factory() is not null;

    /// <summary>
    /// Decodes <paramref name="data"/> through <paramref name="path"/> at <paramref name="request"/>'s reduction or a finer one;
    /// <see langword="null"/> (with the reason) when WIC cannot take that path for it.
    /// </summary>
    /// <exception cref="System.Runtime.InteropServices.COMException">WIC failed on the data.</exception>
    public static DecodedImage? Decode(ReadOnlySpan<byte> data, in PlatformImageRequest request, WicPath path, out string? reason)
    {
        reason = null;
        IWICImagingFactory* factory = Factory();
        if (factory is null)
        {
            reason = "the WIC imaging factory is not available";
            return null;
        }

        IWICStream* stream = null;
        IWICBitmapDecoder* decoder = null;
        IWICBitmapFrameDecode* frame = null;
        fixed (byte* bytes = data)
        {
            try
            {
                factory->CreateStream(&stream);
                stream->InitializeFromMemory(bytes, (uint)data.Length);
                decoder = factory->CreateDecoderFromStream((IStream*)stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnDemand);
                Guid container;
                decoder->GetContainerFormat(&container);
                if (container != PInvoke.GUID_ContainerFormatJpeg)
                {
                    reason = "WIC does not read the data as a JPEG";
                    return null;
                }

                decoder->GetFrame(0, &frame);
                uint width, height;
                frame->GetSize(&width, &height);
                if (width != (uint)request.Width || height != (uint)request.Height)
                {
                    reason = $"WIC reads a {width} x {height} frame";
                    return null;
                }

                return path == WicPath.Planes ? DecodePlanes(frame, request, out reason) : DecodePixels(frame, request, out reason);
            }
            finally
            {
                Release(ref frame);
                Release(ref decoder);
                Release(ref stream);
            }
        }
    }

    private static DecodedImage? DecodePlanes(IWICBitmapFrameDecode* frame, in PlatformImageRequest request, out string? reason)
    {
        reason = null;
        if (request.Components != 3)
        {
            reason = "only YCbCr frames have planes";
            return null;
        }

        IWICPlanarBitmapSourceTransform* planar = null;
        Guid id = IWICPlanarBitmapSourceTransform.IID_Guid;
        if (((IUnknown*)frame)->QueryInterface(&id, (void**)&planar).Failed)
        {
            reason = "the frame has no IWICPlanarBitmapSourceTransform";
            return null;
        }

        try
        {
            Guid* formats = stackalloc Guid[3] { PInvoke.GUID_WICPixelFormat8bppY, PInvoke.GUID_WICPixelFormat8bppCb, PInvoke.GUID_WICPixelFormat8bppCr };
            WICBitmapPlaneDescription* descriptions = stackalloc WICBitmapPlaneDescription[3];
            uint width = (uint)request.ReducedWidth;
            uint height = (uint)request.ReducedHeight;
            BOOL supported;
            planar->DoesSupportTransform(&width, &height, WICBitmapTransformOptions.WICBitmapTransformRotate0, WICPlanarOptions.WICPlanarOptionsDefault, formats, descriptions, 3, &supported);
            int reduction = supported ? WicSamples.DeliveredReduction(request.Width, request.Height, request.Reduction, width, height) : -1;
            if (reduction < 0)
            {
                // Not at the reduction asked for: at full size, which every planar frame supports when it supports any.
                (width, height) = ((uint)request.Width, (uint)request.Height);
                planar->DoesSupportTransform(&width, &height, WICBitmapTransformOptions.WICBitmapTransformRotate0, WICPlanarOptions.WICPlanarOptionsDefault, formats, descriptions, 3, &supported);
                reduction = supported ? WicSamples.DeliveredReduction(request.Width, request.Height, 0, width, height) : -1;
            }

            if (reduction < 0)
            {
                reason = supported ? $"WIC offers its planes at {width} x {height}" : "WIC has no Y, Cb, Cr planes for the frame";
                return null;
            }

            var luma = new WicSamples.Plane((int)descriptions[0].Width, (int)descriptions[0].Height, (int)descriptions[0].Width);
            var chroma = new WicSamples.Plane((int)descriptions[1].Width, (int)descriptions[1].Height, (int)descriptions[1].Width);
            if (descriptions[2].Width != descriptions[1].Width || descriptions[2].Height != descriptions[1].Height)
            {
                reason = "WIC's Cb and Cr planes differ in size";
                return null;
            }

            long lumaBytes = (long)luma.Stride * luma.Height;
            long chromaBytes = (long)chroma.Stride * chroma.Height;
            DecodedImageBuilder image = request.CreateImage(reduction);
            byte[] planes = ArrayPool<byte>.Shared.Rent(checked((int)(lumaBytes + (2 * chromaBytes) + (3L * image.Width))));
            try
            {
                fixed (byte* buffer = planes)
                {
                    WICBitmapPlane* destination = stackalloc WICBitmapPlane[3];
                    destination[0] = new WICBitmapPlane { Format = formats[0], pbBuffer = buffer, cbStride = (uint)luma.Stride, cbBufferSize = (uint)lumaBytes };
                    destination[1] = new WICBitmapPlane { Format = formats[1], pbBuffer = buffer + lumaBytes, cbStride = (uint)chroma.Stride, cbBufferSize = (uint)chromaBytes };
                    destination[2] = new WICBitmapPlane { Format = formats[2], pbBuffer = buffer + lumaBytes + chromaBytes, cbStride = (uint)chroma.Stride, cbBufferSize = (uint)chromaBytes };
                    planar->CopyPixels(null, width, height, WICBitmapTransformOptions.WICBitmapTransformRotate0, WICPlanarOptions.WICPlanarOptionsDefault, destination, 3);
                }

                ReadOnlySpan<byte> all = planes;
                if (!WicSamples.ConvertPlanes(
                    image,
                    all[..(int)lumaBytes],
                    luma,
                    all.Slice((int)lumaBytes, (int)chromaBytes),
                    all.Slice((int)(lumaBytes + chromaBytes), (int)chromaBytes),
                    chroma,
                    planes.AsSpan((int)(lumaBytes + (2 * chromaBytes)), 3 * image.Width)))
                {
                    reason = $"WIC's planes ({luma.Width} x {luma.Height}, {chroma.Width} x {chroma.Height}) do not fit a {image.Width} x {image.Height} image";
                    image.Dispose();
                    return null;
                }

                return Build(image);
            }
            catch
            {
                image.Dispose();
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(planes);
            }
        }
        finally
        {
            Release(ref planar);
        }
    }

    private static DecodedImage? DecodePixels(IWICBitmapFrameDecode* frame, in PlatformImageRequest request, out string? reason)
    {
        reason = null;
        IWICBitmapSourceTransform* transform = null;
        Guid id = IWICBitmapSourceTransform.IID_Guid;
        if (((IUnknown*)frame)->QueryInterface(&id, (void**)&transform).Failed)
        {
            reason = "the frame has no IWICBitmapSourceTransform";
            return null;
        }

        try
        {
            Guid wanted = request.Components == 3 ? PInvoke.GUID_WICPixelFormat24bppBGR : PInvoke.GUID_WICPixelFormat8bppGray;
            Guid format = wanted;
            transform->GetClosestPixelFormat(&format);
            if (format != wanted)
            {
                reason = request.Components == 3 ? "WIC delivers no 24bppBGR for the frame" : "WIC delivers no 8bppGray for the frame";
                return null;
            }

            uint width = (uint)request.ReducedWidth;
            uint height = (uint)request.ReducedHeight;
            transform->GetClosestSize(&width, &height);
            int reduction = WicSamples.DeliveredReduction(request.Width, request.Height, request.Reduction, width, height);
            if (reduction < 0)
            {
                (width, height, reduction) = ((uint)request.Width, (uint)request.Height, 0);
            }

            var source = new WicSamples.Plane((int)width, (int)height, checked((int)width * request.Components));
            long bytes = (long)source.Stride * source.Height;
            DecodedImageBuilder image = request.CreateImage(reduction);
            byte[] pixels = ArrayPool<byte>.Shared.Rent(checked((int)bytes));
            try
            {
                fixed (byte* buffer = pixels)
                {
                    transform->CopyPixels(null, width, height, &format, WICBitmapTransformOptions.WICBitmapTransformRotate0, (uint)source.Stride, (uint)bytes, buffer);
                }

                if (!WicSamples.CopyPixels(image, pixels.AsSpan(0, (int)bytes), source))
                {
                    reason = $"WIC's {width} x {height} pixels do not fit a {image.Width} x {image.Height} image";
                    image.Dispose();
                    return null;
                }

                return Build(image);
            }
            catch
            {
                image.Dispose();
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixels);
            }
        }
        finally
        {
            Release(ref transform);
        }
    }

    private static DecodedImage Build(DecodedImageBuilder image)
    {
        try
        {
            return image.Build();
        }
        finally
        {
            image.Dispose();
        }
    }

    /// <summary>Returns the process's WIC factory, created on first use; <see langword="null"/> when WIC is not available.</summary>
    private static IWICImagingFactory* Factory()
    {
        IWICImagingFactory* factory = _factory;
        if (factory is not null || _unavailable)
        {
            return factory;
        }

        lock (Gate)
        {
            return CreateFactory();
        }
    }

    /// <summary>Creates the factory unless another thread did (under <see cref="Gate"/>).</summary>
    private static IWICImagingFactory* CreateFactory()
    {
        if (_factory is not null || _unavailable || !OperatingSystem.IsWindows())
        {
            _unavailable = _factory is null;
            return _factory;
        }

        IWICImagingFactory* factory;
        HRESULT result = Create(&factory);
        if (result.Value == NotInitialized)
        {
            // A thread COM was never initialized on: join the multithreaded apartment, which the in-box codecs support.
            _ = PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
            result = Create(&factory);
        }

        if (result.Failed)
        {
            _unavailable = true;
            return null;
        }

        _factory = factory;
        return factory;

        static HRESULT Create(IWICImagingFactory** factory)
        {
            *factory = null;
            Guid clsid = PInvoke.CLSID_WICImagingFactory2;
            Guid iid = IWICImagingFactory.IID_Guid;
            return PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, (void**)factory);
        }
    }

    private static void Release<T>(ref T* pointer)
        where T : unmanaged
    {
        if (pointer is not null)
        {
            ((IUnknown*)pointer)->Release();
            pointer = null;
        }
    }
}
