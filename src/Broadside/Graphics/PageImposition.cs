using Broadside.Annotations;

namespace Broadside.Graphics;

/// <summary>
/// The layout math and page ordering of the imposition operations (issue #647): n-up sheets, saddle-stitched booklets and tiles.
/// Each source page is made into a form XObject of the target (issue #628) and drawn with one <c>cm</c>, clipped for a tile.
/// </summary>
/// <remarks>ISO 32000-2 §8.10.1, §14.11.2 and §7.7.3.3.</remarks>
internal static class PageImposition
{
    private const double FrameWidth = 0.5;

    /// <summary>Lays the source pages out <paramref name="columns"/> by <paramref name="rows"/> per sheet of <paramref name="target"/>.</summary>
    public static void NUp(PdfDocument target, PdfDocument source, int first, int count, int columns, int rows, PdfImpositionOptions options)
    {
        PdfPageFormOptions formOptions = FormOptions(options);
        PdfRectangle sheet = options.SheetSize ?? NUpSheet(Extent(target.CreateFormXObject(source.Pages[first], formOptions)), columns, rows);
        PdfRectangle[] cells = Cells(sheet, columns, rows, options, options.Order);
        int perSheet = columns * rows;
        for (int start = 0; start < count; start += perSheet)
        {
            PdfPage page = target.Pages.Add(sheet);
            using PdfCanvas canvas = page.CreateCanvas();
            for (int slot = 0; slot < perSheet && start + slot < count; slot++)
            {
                PdfFormXObject form = target.CreateFormXObject(source.Pages[first + start + slot], formOptions);
                Place(canvas, form, Extent(form), cells[slot], options.DrawCellFrames);
            }
        }
    }

    /// <summary>
    /// Lays the source pages out 2-up in saddle-stitch order: padded with blanks to a multiple of 4, sheet <c>s</c> (from 0) has
    /// <c>(n − 2s, 2s + 1)</c> on its front and <c>(2s + 2, n − 2s − 1)</c> on its back, left to right, swapped for right-to-left.
    /// </summary>
    public static void Booklet(PdfDocument target, PdfDocument source, int first, int count, PdfImpositionOptions options)
    {
        PdfPageFormOptions formOptions = FormOptions(options);
        PdfRectangle sheet = options.SheetSize ?? BookletSheet(Extent(target.CreateFormXObject(source.Pages[first], formOptions)));
        PdfRectangle[] cells = Cells(sheet, 2, 1, options, PdfImpositionOrder.LeftToRightThenDown);
        bool rightToLeft = options.Order == PdfImpositionOrder.RightToLeftThenDown
            || source.ViewerPreferences?.Direction == PdfReadingDirection.RightToLeft;
        foreach ((int left, int right) in BookletOrder(count))
        {
            PdfPage page = target.Pages.Add(sheet);
            using PdfCanvas canvas = page.CreateCanvas();
            (int a, int b) = rightToLeft ? (right, left) : (left, right);
            foreach ((int number, PdfRectangle cell) in (ReadOnlySpan<(int, PdfRectangle)>)[(a, cells[0]), (b, cells[1])])
            {
                if (number <= count)
                {
                    PdfFormXObject form = target.CreateFormXObject(source.Pages[first + number - 1], formOptions);
                    Place(canvas, form, Extent(form), cell, options.DrawCellFrames);
                }
            }
        }
    }

    /// <summary>The 1-based page numbers on each side, in output order, for <paramref name="count"/> pages padded to a multiple of 4; a number above <paramref name="count"/> is a blank.</summary>
    internal static IEnumerable<(int Left, int Right)> BookletOrder(int count)
    {
        int n = (count + 3) / 4 * 4;
        for (int sheet = 0; sheet < n / 4; sheet++)
        {
            yield return (n - (2 * sheet), (2 * sheet) + 1);
            yield return ((2 * sheet) + 2, n - (2 * sheet) - 1);
        }
    }

    /// <summary>Splits each source page into <paramref name="columns"/> by <paramref name="rows"/> tiles, each enlarged onto a sheet of its own.</summary>
    public static void Tiles(PdfDocument target, PdfDocument source, int first, int count, int columns, int rows, PdfImpositionOptions options)
    {
        PdfPageFormOptions formOptions = FormOptions(options);
        PdfFormXObject[] forms = new PdfFormXObject[count];
        for (int index = 0; index < count; index++)
        {
            forms[index] = target.CreateFormXObject(source.Pages[first + index], formOptions);
            PdfRectangle extent = Extent(forms[index]);
            if ((columns > 1 && options.Overlap >= extent.Width) || (rows > 1 && options.Overlap >= extent.Height))
            {
                throw new ArgumentException("The overlap shall be smaller than the page it splits.", nameof(options));
            }
        }

        PdfRectangle? sheetSize = options.SheetSize;
        foreach (PdfFormXObject form in forms)
        {
            PdfRectangle extent = Extent(form);
            PdfRectangle sheet = sheetSize ?? new PdfRectangle(0, 0, extent.Width, extent.Height);
            PdfRectangle area = Cells(sheet, 1, 1, options, PdfImpositionOrder.LeftToRightThenDown)[0];
            double tileWidth = (extent.Width + ((columns - 1) * options.Overlap)) / columns;
            double tileHeight = (extent.Height + ((rows - 1) * options.Overlap)) / rows;
            for (int slot = 0; slot < columns * rows; slot++)
            {
                (int column, int row) = Slot(slot, columns, rows, options.Order);
                var region = new PdfRectangle(
                    extent.Left + (column * (tileWidth - options.Overlap)),
                    extent.Top - tileHeight - (row * (tileHeight - options.Overlap)),
                    extent.Left + (column * (tileWidth - options.Overlap)) + tileWidth,
                    extent.Top - (row * (tileHeight - options.Overlap)));
                PdfPage page = target.Pages.Add(sheet);
                using PdfCanvas canvas = page.CreateCanvas();
                Place(canvas, form, region, area, options.DrawCellFrames, clip: true);
            }
        }
    }

    /// <summary>The cell (column, row from the top) that the <paramref name="slot"/>-th page of a sheet fills in <paramref name="order"/>.</summary>
    internal static (int Column, int Row) Slot(int slot, int columns, int rows, PdfImpositionOrder order) => order switch
    {
        PdfImpositionOrder.RightToLeftThenDown => (columns - 1 - (slot % columns), slot / columns),
        PdfImpositionOrder.TopToBottomThenRight => (slot / rows, slot % rows),
        _ => (slot % columns, slot / columns),
    };

    /// <summary>
    /// Draws the <paramref name="region"/> of <paramref name="form"/>'s displayed extent into <paramref name="cell"/>: turned 90°
    /// counterclockwise when the cell's orientation differs from the region's, uniformly scaled to fit and centered.
    /// </summary>
    private static void Place(PdfCanvas canvas, PdfFormXObject form, PdfRectangle region, PdfRectangle cell, bool frame, bool clip = false)
    {
        double width = region.Width;
        double height = region.Height;
        bool turn = Orientation(cell.Width, cell.Height) * Orientation(width, height) < 0;
        Matrix matrix = Matrix.CreateTranslation(-region.Left, -region.Bottom);
        if (turn)
        {
            matrix *= new Matrix(0, 1, -1, 0, height, 0);
            (width, height) = (height, width);
        }

        double scale = Math.Min(cell.Width / width, cell.Height / height);
        matrix *= Matrix.CreateScale(scale, scale);
        double left = cell.Left + ((cell.Width - (scale * width)) / 2);
        double bottom = cell.Bottom + ((cell.Height - (scale * height)) / 2);
        matrix *= Matrix.CreateTranslation(left, bottom);

        canvas.SaveState();
        canvas.Transform(matrix);
        if (clip)
        {
            canvas.Rectangle(region.Left, region.Bottom, region.Width, region.Height);
            canvas.Clip();
            canvas.EndPath();
        }

        canvas.DrawForm(form);
        canvas.RestoreState();
        if (frame)
        {
            PdfRectangle placed = clip ? new PdfRectangle(left, bottom, left + (scale * width), bottom + (scale * height)) : cell;
            canvas.SaveState();
            canvas.SetLineWidth(FrameWidth);
            canvas.SetStrokeColor(PdfColor.FromGray(0));
            canvas.Rectangle(placed.Left, placed.Bottom, placed.Width, placed.Height);
            canvas.Stroke();
            canvas.RestoreState();
        }
    }

    /// <summary>1 for landscape, −1 for portrait, 0 for square.</summary>
    private static int Orientation(double width, double height) => Math.Sign(width - height);

    /// <summary>The form's bounding box mapped through its matrix: the page as displayed, its lower-left corner at the origin (#628).</summary>
    internal static PdfRectangle Extent(PdfFormXObject form) =>
        form.BoundingBox is { } box ? AnnotationGeometry.Bounds(form.Matrix, box) : new PdfRectangle(0, 0, 0, 0);

    private static PdfPageFormOptions FormOptions(PdfImpositionOptions options) =>
        new() { Boundary = options.Boundary, IncludeAnnotations = options.IncludeAnnotations, ApplyRotation = true };

    /// <summary>The default n-up sheet: the page's size, landscape with more columns than rows and portrait with more rows than columns.</summary>
    internal static PdfRectangle NUpSheet(PdfRectangle page, int columns, int rows)
    {
        double width = page.Width;
        double height = page.Height;
        if ((columns > rows && height > width) || (rows > columns && width > height))
        {
            (width, height) = (height, width);
        }

        return new PdfRectangle(0, 0, width, height);
    }

    private static PdfRectangle BookletSheet(PdfRectangle page) => new(0, 0, 2 * page.Width, page.Height);

    /// <summary>The cells of a sheet in the order they are filled: the sheet inset by the margin, divided with gutters between cells.</summary>
    private static PdfRectangle[] Cells(PdfRectangle sheet, int columns, int rows, PdfImpositionOptions options, PdfImpositionOrder order)
    {
        double areaLeft = sheet.Left + options.Margin;
        double areaTop = sheet.Top - options.Margin;
        double width = (sheet.Width - (2 * options.Margin) - ((columns - 1) * options.Gutter)) / columns;
        double height = (sheet.Height - (2 * options.Margin) - ((rows - 1) * options.Gutter)) / rows;
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("The sheet's margin and gutters leave no room for a cell.", nameof(options));
        }

        var cells = new PdfRectangle[columns * rows];
        for (int slot = 0; slot < cells.Length; slot++)
        {
            (int column, int row) = Slot(slot, columns, rows, order);
            double left = areaLeft + (column * (width + options.Gutter));
            double top = areaTop - (row * (height + options.Gutter));
            cells[slot] = new PdfRectangle(left, top - height, left + width, top);
        }

        return cells;
    }
}
