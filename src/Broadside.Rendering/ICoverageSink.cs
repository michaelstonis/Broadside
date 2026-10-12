namespace Broadside.Rendering;

/// <summary>
/// Receives the coverage of a shape from <see cref="ScanlineRasterizer"/>, one horizontal run of pixels with the same coverage at a
/// time, rows top to bottom and runs left to right within a row. What a paint does with it (composite a colour, sample an image, a
/// shading) is the sink's.
/// </summary>
/// <remarks>
/// Implement it as a struct (or ref struct) and pass it by reference, so the scan converter's generic sweep calls it directly with
/// no allocation and no virtual call. Through <see cref="ScanlineRasterizer.Paint"/> the runs are already cut by the current clip
/// (ISO 32000-2 §8.5.4); coverage is 1 to 255 there.
/// </remarks>
internal interface ICoverageSink
{
    /// <summary>Takes <paramref name="count"/> pixels of row <paramref name="row"/> from column <paramref name="x"/> on, each covered by <paramref name="coverage"/> (0 to 255).</summary>
    void Span(int row, int x, int count, int coverage);
}
