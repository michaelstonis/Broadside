using System.Text;
using Broadside.Diagnostics;
using Broadside.Images;

namespace Broadside.Graphics.Svg;

/// <summary>The <c>image</c> element: PNG and JPEG as image XObjects, SVG as a nested form XObject.</summary>
/// <remarks>
/// SVG 1.1 §5.7 (the <c>image</c> element) and RFC 2397 (the <c>data</c> URL scheme); ISO 32000-2 §8.9.5 (image XObjects) and §8.10.
/// An <c>href</c> that is not a <c>data:</c> URL is never opened here: only the caller's resource resolver can supply it.
/// </remarks>
internal sealed partial class SvgImporter
{
    private void RenderImage(SvgElement element, SvgStyle style)
    {
        string? href = element.Attribute("href");
        if (href is null || href.Trim().Length == 0)
        {
            return;
        }

        if (Resolve(element, href.Trim()) is not { } bytes)
        {
            return;
        }

        PdfImage? image = null;
        PdfFormXObject? nested = null;
        double intrinsicWidth;
        double intrinsicHeight;
        if (SvgDocument.Sniff(bytes.Span))
        {
            if (_imageDepth >= MaxImageDepth)
            {
                Report(element, SvgDiagnostics.SvgReferenceCycle, $"The SVG image of {element} nests SVG images too deeply; it is not drawn.");
                return;
            }

            if (!_shared.SvgImages.TryGetValue(href, out nested))
            {
                try
                {
                    nested = Import(_document, bytes.Span, new SvgImportOptions().NestedFrom(_options), _imageDepth + 1, _shared);
                }
                catch (ArgumentException exception)
                {
                    Report(element, SvgDiagnostics.SvgImageInvalid, $"The SVG image of {element} cannot be read ({exception.Message}); it is not drawn.");
                    return;
                }

                _shared.SvgImages[href] = nested;
            }

            PdfRectangle box = nested.BoundingBox ?? new PdfRectangle(0, 0, 1, 1);
            intrinsicWidth = box.Width / PointsPerPixel;
            intrinsicHeight = box.Height / PointsPerPixel;
        }
        else
        {
            if (!_images.TryGetValue(href, out image))
            {
                image = Decode(element, bytes.Span);
                if (image is null)
                {
                    return;
                }

                _images[href] = image;
            }

            intrinsicWidth = image.Width;
            intrinsicHeight = image.Height;
        }

        if (!(intrinsicWidth > 0 && intrinsicHeight > 0))
        {
            return;
        }

        double x = LengthX(element, "x", 0, style);
        double y = LengthY(element, "y", 0, style);
        bool hasWidth = element.Attribute("width") is { } w && w.Trim() != "auto";
        bool hasHeight = element.Attribute("height") is { } h && h.Trim() != "auto";
        double width = hasWidth ? LengthX(element, "width", 0, style) : hasHeight ? LengthY(element, "height", 0, style) * intrinsicWidth / intrinsicHeight : intrinsicWidth;
        double height = hasHeight ? LengthY(element, "height", 0, style) : hasWidth ? width * intrinsicHeight / intrinsicWidth : intrinsicHeight;
        if (!(width > 0 && height > 0))
        {
            return;
        }

        var viewport = new SvgRect(x, y, width, height);
        SvgAspectRatio aspect = SvgAspectRatio.Parse(element.Attribute("preserveAspectRatio"));
        Matrix fit = aspect.Fit(new SvgRect(0, 0, intrinsicWidth, intrinsicHeight), viewport);
        WithEffects(
            element,
            style,
            () => viewport,
            alpha =>
            {
                if (!style.Visible)
                {
                    return;
                }

                Save();
                if (aspect.Slice)
                {
                    Canvas.Rectangle(x, y, width, height);
                    Canvas.Clip();
                    Canvas.EndPath();
                }

                Transform(fit);
                if (image is not null)
                {
                    if (alpha < 1)
                    {
                        Canvas.SetFillAlpha(alpha);
                    }

                    Transform(new Matrix(intrinsicWidth, 0, 0, -intrinsicHeight, 0, intrinsicHeight));
                    Canvas.DrawImage(image);
                }
                else
                {
                    PdfRectangle box = nested!.BoundingBox ?? new PdfRectangle(0, 0, 1, 1);
                    Transform(new Matrix(intrinsicWidth / box.Width, 0, 0, -intrinsicHeight / box.Height, 0, intrinsicHeight));
                    Canvas.DrawForm(nested);
                }

                Restore();
            },
            foldable: image is not null);
    }

    /// <summary>Returns the bytes an <c>href</c> refers to: decoded from a <c>data:</c> URL, or from the caller's resolver.</summary>
    private ReadOnlyMemory<byte>? Resolve(SvgElement element, string href)
    {
        if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (DecodeDataUrl(href) is { } data)
            {
                return data;
            }

            Report(element, SvgDiagnostics.SvgImageInvalid, $"The data URL of {element} cannot be decoded; the image is not drawn.");
            return null;
        }

        if (_options.ResourceResolver is { } resolver && resolver(href) is { } resolved)
        {
            return resolved;
        }

        Report(element, SvgDiagnostics.SvgReferenceMissing, $"The image '{Shorten(href)}' of {element} is not supplied by the resource resolver; it is not drawn.");
        return null;
    }

    private static string Shorten(string href) => href.Length <= 120 ? href : string.Concat(href.AsSpan(0, 117), "...");

    /// <summary>Decodes a <c>data:</c> URL (RFC 2397): base64 or percent-encoded.</summary>
    internal static byte[]? DecodeDataUrl(string url)
    {
        int comma = url.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return null;
        }

        ReadOnlySpan<char> header = url.AsSpan(5, comma - 5);
        ReadOnlySpan<char> payload = url.AsSpan(comma + 1);
        if (header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            var clean = new StringBuilder(payload.Length);
            foreach (char c in payload)
            {
                if (!char.IsWhiteSpace(c))
                {
                    _ = clean.Append(c);
                }
            }

            string text = Uri.UnescapeDataString(clean.ToString());
            int padding = (4 - (text.Length % 4)) % 4;
            if (padding is 1 or 2)
            {
                text += new string('=', padding);
            }

            byte[] buffer = new byte[(text.Length * 3 / 4) + 3];
            return Convert.TryFromBase64String(text, buffer, out int written) ? buffer[..written] : null;
        }

        return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload.ToString()));
    }

    private PdfImage? Decode(SvgElement element, ReadOnlySpan<byte> bytes)
    {
        try
        {
            if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            {
                return _document.CreateImageFromPng(bytes);
            }

            if (bytes is [0xFF, 0xD8, 0xFF, ..])
            {
                return _document.CreateImageFromJpeg(bytes);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidDataException)
        {
            Report(element, SvgDiagnostics.SvgImageInvalid, $"The image of {element} cannot be decoded ({exception.Message}); it is not drawn.");
            return null;
        }

        Report(element, SvgDiagnostics.SvgImageInvalid, $"The image of {element} is not PNG, JPEG or SVG; it is not drawn.");
        return null;
    }
}

/// <summary>Option plumbing for nested SVG images.</summary>
internal static class SvgImportOptionsExtensions
{
    /// <summary>Returns options for a nested SVG image: the parent's resolver and default font, the image's own size.</summary>
    public static SvgImportOptions NestedFrom(this SvgImportOptions options, SvgImportOptions parent)
    {
        if (parent.ResourceResolver is { } resolver)
        {
            options = options.WithResourceResolver(resolver);
        }

        if (parent.DefaultFont is { } font)
        {
            options = options.WithDefaultFont(font);
        }

        return options;
    }
}
