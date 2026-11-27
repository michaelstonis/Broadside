using System.Diagnostics.CodeAnalysis;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>Draws a <see cref="Barcode"/> on any <see cref="PdfCanvas"/>: a page, a form XObject, a drawing callback or a page template.</summary>
/// <remarks>ISO 32000-2 §8.5.2 and §8.5.3: rectangles (<c>re</c>) filled with <c>f</c>.</remarks>
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "C# 14 extension blocks, which the analyzer sees as nested types.")]
public static class BarcodeCanvasExtensions
{
    /// <summary>Drawing on a canvas.</summary>
    /// <param name="canvas">The canvas.</param>
    extension(PdfCanvas canvas)
    {
        /// <summary>
        /// Paints <paramref name="barcode"/> into <paramref name="box"/> as filled rectangles: each horizontal run of dark modules is one
        /// rectangle (rows that repeat the row above extend it), all filled at once.
        /// </summary>
        /// <param name="barcode">The barcode.</param>
        /// <param name="box">The box in the canvas's current coordinates; with the quiet zone included (the default), it holds the quiet zone too.</param>
        /// <param name="options">The colors, the text and the quiet zone; null for the defaults.</param>
        /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="box"/> has no area, or is not finite.</exception>
        /// <exception cref="InvalidOperationException">The canvas has a path under construction or a text object open.</exception>
        /// <remarks>
        /// <para>
        /// ISO 32000-2 §8.5.2.1 (<c>re</c>) and §8.5.3.1 (<c>f</c>), inside a saved graphics state (§8.4.2, <c>q</c> ... <c>Q</c>), so the
        /// canvas's colors are unchanged after it. A linear symbol's bars fill the box's width and height, less a band for the
        /// human-readable text when <see cref="BarcodeOptions.ShowText"/> is set and a <see cref="BarcodeOptions.Font"/> is given; the text
        /// is centered under the bars. A two-dimensional symbol keeps square modules: it is as large as the box allows and centered in it.
        /// Modules sit on a whole-module grid, so neighboring rectangles meet exactly; a box smaller than a point per module is drawn all the
        /// same.
        /// </para>
        /// </remarks>
        public void DrawBarcode(Barcode barcode, PdfRectangle box, BarcodeOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(barcode);
            if (!(box.Width > 0 && box.Height > 0 && double.IsFinite(box.Width) && double.IsFinite(box.Height)))
            {
                throw new ArgumentOutOfRangeException(nameof(box), box, "The box shall have a positive, finite width and height.");
            }

            options ??= Defaults;
            int quiet = options.IncludeQuietZone ? barcode.QuietZone : 0;
            int columns = barcode.Columns + (2 * quiet);
            string? text = barcode.IsLinear && options.ShowText && options.Font is not null ? barcode.HumanReadableText : null;

            double moduleWidth;
            double moduleHeight;
            double left;
            double top;
            PdfRectangle area;
            double textBand = 0;
            if (barcode.IsLinear)
            {
                if (text is not null)
                {
                    double size = options.FontSize;
                    textBand = Math.Min(box.Height / 2, (size * (options.Font!.Ascent - options.Font.Descent) / 1000) + (size * 0.25));
                }

                moduleWidth = box.Width / columns;
                moduleHeight = box.Height - textBand;
                left = box.Left + (quiet * moduleWidth);
                top = box.Top;
                area = box;
            }
            else
            {
                int rows = barcode.Rows + (2 * quiet);
                double module = Math.Min(box.Width / columns, box.Height / rows);
                double width = module * columns;
                double height = module * rows;
                double areaLeft = box.Left + ((box.Width - width) / 2);
                double areaBottom = box.Bottom + ((box.Height - height) / 2);
                area = new PdfRectangle(areaLeft, areaBottom, areaLeft + width, areaBottom + height);
                moduleWidth = module;
                moduleHeight = module;
                left = areaLeft + (quiet * module);
                top = area.Top - (quiet * module);
            }

            canvas.SaveState();
            if (options.Background is { } background)
            {
                canvas.SetFillColor(background);
                canvas.Rectangle(area.Left, area.Bottom, area.Width, area.Height);
                canvas.Fill();
            }

            canvas.SetFillColor(options.Foreground);
            if (Paths(canvas, barcode, left, top, moduleWidth, moduleHeight))
            {
                canvas.Fill();
            }

            if (text is not null)
            {
                double size = options.FontSize;
                double width = options.Font!.MeasureWidth(text, size);
                double center = left + (barcode.Columns * moduleWidth / 2);
                canvas.BeginText();
                canvas.SetFont(options.Font, size);
                canvas.MoveText(center - (width / 2), box.Bottom - (options.Font.Descent * size / 1000));
                canvas.ShowText(text);
                canvas.EndText();
            }

            canvas.RestoreState();
        }
    }

    private static BarcodeOptions Defaults { get; } = new();

    /// <summary>Appends one rectangle per run of dark modules, a run spanning every following row that repeats its row; false when none.</summary>
    private static bool Paths(PdfCanvas canvas, Barcode barcode, double left, double top, double moduleWidth, double moduleHeight)
    {
        ReadOnlySpan<bool> modules = barcode.Modules;
        int columns = barcode.Columns;
        bool any = false;
        int row = 0;
        while (row < barcode.Rows)
        {
            ReadOnlySpan<bool> line = modules.Slice(row * columns, columns);
            int repeat = 1;
            while (row + repeat < barcode.Rows && modules.Slice((row + repeat) * columns, columns).SequenceEqual(line))
            {
                repeat++;
            }

            double y = top - ((row + repeat) * moduleHeight);
            int column = 0;
            while (column < columns)
            {
                if (!line[column])
                {
                    column++;
                    continue;
                }

                int start = column;
                while (column < columns && line[column])
                {
                    column++;
                }

                canvas.Rectangle(left + (start * moduleWidth), y, (column - start) * moduleWidth, repeat * moduleHeight);
                any = true;
            }

            row += repeat;
        }

        return any;
    }
}
