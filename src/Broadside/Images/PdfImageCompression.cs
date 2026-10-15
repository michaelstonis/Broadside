namespace Broadside.Images;

/// <summary>How <see cref="PdfDocument.CreateImage(ReadOnlySpan{byte}, PdfImageLayout, PdfImageCompression)"/> compresses samples.</summary>
/// <remarks>ISO 32000-2 §7.4.4 (FlateDecode, Table 8 predictors) and §7.4.8 (DCTDecode).</remarks>
public enum PdfImageCompression
{
    /// <summary>Lossless: <c>FlateDecode</c> with the PNG predictors chosen row by row (Predictor 15). PDF 1.2.</summary>
    Flate,

    /// <summary>
    /// Lossy JPEG baseline: <c>DCTDecode</c>, through the document's DCT encoder (<see cref="Filters.DctEncoder"/> unless
    /// <see cref="PdfOptions.UseStreamEncoder"/> registered another). Only 8-bit samples of 1, 3 or 4 components in a space that is
    /// not Indexed; an alpha plane is compressed with Flate.
    /// </summary>
    Dct,
}
