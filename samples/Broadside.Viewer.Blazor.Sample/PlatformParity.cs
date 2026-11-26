using System.Buffers.Binary;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Broadside.Rendering;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>
/// The same work as the Native AOT smoke host, done in the browser for the browser tests (#272): open a document with the bundled
/// Standard 14 fonts and no operating-system fonts, render page 1 with the software rasterizer, extract its text, save the document
/// and open the saved bytes again. The browser test runs the same steps on the desktop runtime and requires the same results, the
/// render hash included (the software rasterizer draws the same bytes on every platform).
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class PlatformParity
{
    /// <summary>
    /// Runs the steps on the document at <paramref name="address"/> and returns them as JSON:
    /// <c>{"width", "height", "hash", "text", "savedPages", "diagnostics"}</c>.
    /// </summary>
    /// <param name="address">The document.</param>
    /// <param name="dpi">The resolution page 1 is rendered at.</param>
    /// <returns>The results as JSON.</returns>
    [JSExport]
    public static async Task<string> RunAsync(string address, double dpi)
    {
        using var http = new HttpClient();
        byte[] bytes = await http.GetByteArrayAsync(new Uri(address)).ConfigureAwait(true);
        using PdfDocument document = await new PdfEngine(new PdfOptions().UseStandard14Fonts()).OpenAsync(bytes).ConfigureAwait(true);
        PdfPage page = document.Pages[0];

        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(page, new RenderOptions { Dpi = dpi });
        string text = page.ExtractText().PlainText;

        using var saved = new MemoryStream();
        await document.SaveAsync(saved).ConfigureAwait(true);
        using PdfDocument reopened = await new PdfEngine(new PdfOptions().UseStandard14Fonts()).OpenAsync(saved.ToArray()).ConfigureAwait(true);

        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteNumber("width", bitmap.Width);
            writer.WriteNumber("height", bitmap.Height);
            writer.WriteString("hash", Hash(bitmap));
            writer.WriteString("text", text);
            writer.WriteNumber("savedPages", reopened.Pages.Count);
            writer.WriteStartArray("diagnostics");
            foreach (Broadside.Diagnostics.Diagnostic diagnostic in document.Diagnostics)
            {
                writer.WriteStringValue(diagnostic.Code);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(json.ToArray());
    }

    // The render-determinism hash (tests/Broadside.GoldenImages/RenderHashManifest.cs): SHA-256 over the width and height
    // (little-endian) and each row's pixels without the stride padding.
    private static string Hash(Bitmap bitmap)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> size = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(size, bitmap.Width);
        BinaryPrimitives.WriteInt32LittleEndian(size[4..], bitmap.Height);
        hash.AppendData(size);
        for (int y = 0; y < bitmap.Height; y++)
        {
            hash.AppendData(bitmap.GetRow(y)[..(bitmap.Width * 4)]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
