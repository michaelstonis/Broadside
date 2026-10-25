using Broadside.Graphics;

namespace Broadside.Viewer.Blazor;

/// <summary>The part of default user space a tile's pixels show, for culling a prefetch to the tile.</summary>
internal static class TileRegion
{
    /// <summary>Returns the region a <paramref name="width"/> × <paramref name="height"/> tile under <paramref name="device"/> shows, widened by 2 pixels; <see langword="null"/> when the transform has no inverse.</summary>
    public static PdfRectangle? Of(in Matrix device, int width, int height)
    {
        if (!device.TryInvert(out Matrix inverse))
        {
            return null;
        }

        PathPoint a = inverse.Transform(-2, -2);
        PathPoint b = inverse.Transform(width + 2, -2);
        PathPoint c = inverse.Transform(width + 2, height + 2);
        PathPoint d = inverse.Transform(-2, height + 2);
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double bottom = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double top = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return double.IsFinite(left) && double.IsFinite(bottom) && double.IsFinite(right) && double.IsFinite(top)
            ? new PdfRectangle(left, bottom, right, top)
            : null;
    }
}
