namespace Broadside.Viewer;

/// <summary>The geometry of one page as a viewer lays it out: its crop box, rotation and user unit, cached by the session.</summary>
/// <param name="Index">The zero-based page.</param>
/// <param name="CropBox">The crop box, in default user space (ISO 32000-2 §14.11.2): the region displayed.</param>
/// <param name="Rotation">The page's rotation in degrees clockwise: 0, 90, 180 or 270 (ISO 32000-2 §7.7.3.3, <c>Rotate</c>).</param>
/// <param name="UserUnit">The size of a default user space unit in points (ISO 32000-2 §14.11.2, <c>UserUnit</c>).</param>
/// <remarks>ISO 32000-2 §7.7.3.3 and §14.11.2. Spec #33 story 54.</remarks>
public readonly record struct PageMetrics(int Index, PdfRectangle CropBox, int Rotation, double UserUnit)
{
    /// <summary>Gets the displayed width in points: the crop box's width, or its height when the page is turned a quarter, times the user unit.</summary>
    public double Width => (IsQuarterTurned ? CropBox.Height : CropBox.Width) * UserUnit;

    /// <summary>Gets the displayed height in points: the crop box's height, or its width when the page is turned a quarter, times the user unit.</summary>
    public double Height => (IsQuarterTurned ? CropBox.Width : CropBox.Height) * UserUnit;

    private bool IsQuarterTurned => Rotation is 90 or 270;

    internal static PageMetrics Of(int index, PdfPage page) =>
        new(index, page.CropBox, ((page.Rotation % 360) + 360) % 360, page.UserUnit);
}
