using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Structure;

namespace Broadside.Layout.Flow;

/// <summary>
/// A box of vector drawing between blocks: <see cref="Draw"/> paints it through the page's <see cref="PdfCanvas"/>, in a coordinate
/// system whose origin is the box's bottom-left corner and whose unit is the point. The box is aligned left, centred or right in the
/// content area; one wider than the content area or taller than a page's content area is scaled down, keeping its aspect ratio, and one
/// that does not fit below the cursor starts a page. Tagged output makes it a <c>Figure</c> with <see cref="AlternateText"/> and its
/// bounding box, or a layout artifact when <see cref="IsDecorative"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.4.8.5 (<c>Figure</c>, with the <c>BBox</c> layout attribute of §14.8.5.4.3, Table 379), §14.9.3 (alternate
/// descriptions), §14.8.2.2.2 (artifacts) and §8.4.2 (graphics state stack). The callback runs inside a saved graphics state
/// (<c>q</c> ... <c>Q</c>) that translates and scales to the box, and inside the figure's marked content; it runs once, when the drawing
/// is placed on its page. Graphics states, marked-content sequences or a text object it leaves open (or closes beyond what it opened) are
/// closed or reopened so the content stream stays balanced, and recorded as <see cref="LayoutDiagnostics.DrawingStateUnbalanced"/>; a path
/// it leaves unpainted is ended without painting.
/// </para>
/// </remarks>
public sealed class DrawingBlock : FlowBlock
{
    /// <summary>Creates a drawing with structure type <c>Figure</c>.</summary>
    /// <param name="width">The width of the box in points.</param>
    /// <param name="height">The height of the box in points.</param>
    /// <param name="draw">Paints the drawing: the canvas of the page, and the box <c>[0 0 width height]</c> in the canvas's current coordinates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="draw"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive and finite.</exception>
    public DrawingBlock(double width, double height, Action<PdfCanvas, PdfRectangle> draw)
        : base("Figure")
    {
        ArgumentNullException.ThrowIfNull(draw);
        Width = ImageBlock.Positive(width, nameof(width));
        Height = ImageBlock.Positive(height, nameof(height));
        Draw = draw;
    }

    /// <summary>The width of the box in points before any scaling down.</summary>
    public double Width { get; }

    /// <summary>The height of the box in points before any scaling down.</summary>
    public double Height { get; }

    /// <summary>The callback that paints the drawing.</summary>
    public Action<PdfCanvas, PdfRectangle> Draw { get; }

    /// <summary>The alternate description of the drawing for tagged output, or null.</summary>
    /// <remarks>ISO 32000-2 §14.9.3. Tagged output without one records <c>FigureWithoutAlternateText</c>.</remarks>
    public string? AlternateText { get; init; }

    /// <summary>Gets a value indicating whether the drawing is decoration: tagged output marks it as a layout artifact instead of a <c>Figure</c>.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.2.</remarks>
    public bool IsDecorative { get; init; }

    /// <summary>The horizontal placement; <see cref="TextAlignment.Justify"/> places it as <see cref="TextAlignment.Left"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined alignment.</exception>
    public TextAlignment Alignment
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown alignment.");
    }

    /// <summary>The space above in points, dropped at the top of a page.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceBefore
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    /// <summary>The space below in points.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceAfter
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    internal override void Validate(int index)
    {
    }

    internal override ContentWidths MeasureWidths(FlowServices services) => new(Width, Width);

    internal override void LayOut(FlowContext context)
    {
        context.AddSpace(SpaceBefore);

        // A page is open from here on, so the content area is known even for the first block of the flow.
        context.EnsureRoom(0);
        double scale = Math.Min(1, Math.Min(context.Width / Width, (context.Top - context.Bottom) / Height));
        context.EnsureRoom(Height * scale);

        // The page the drawing lands on decides its width.
        scale = Math.Min(scale, context.Width / Width);
        double width = Width * scale;
        double height = Height * scale;
        double x = Alignment switch
        {
            TextAlignment.Right => context.Right - width,
            TextAlignment.Center => context.Left + ((context.Width - width) / 2),
            _ => context.Left,
        };
        double y = context.CursorY - height;
        context.PlaceMarkersAtTop();
        if (IsDecorative)
        {
            context.BeginPart(new FlowPart("Artifact", PdfArtifactType.Layout, null));
        }
        else
        {
            context.BeginBlock(this);
        }

        if (context.Drawing)
        {
            if (!IsDecorative)
            {
                context.Services.Structure?.SetBoundingBox(this, new PdfRectangle(x, y, x + width, y + height));
            }

            PdfCanvas canvas = context.Canvas;
            canvas.SaveState();
            int state = canvas.StateDepth;
            int marked = canvas.MarkedContentDepth;
            canvas.Transform(new Matrix(scale, 0, 0, scale, x, y));
            Draw(canvas, new PdfRectangle(0, 0, Width, Height));
            CanvasBalance.Restore(canvas, state, marked, context.Diagnostics, string.Create(CultureInfo.InvariantCulture, $"The drawing of {Width:0.##} by {Height:0.##} points"));
        }

        context.Advance(height);
        if (IsDecorative)
        {
            context.EndPart();
        }
        else
        {
            context.EndBlock();
        }

        context.AddSpace(SpaceAfter);
    }
}

/// <summary>Closes or reopens what a drawing callback left unbalanced on a canvas, so the layout's own nesting stays intact.</summary>
internal static class CanvasBalance
{
    /// <summary>
    /// Restores the graphics state saved before a callback ran, once the callback is done: first ends an unpainted path, closes a text
    /// object and the marked-content sequences and graphics states opened beyond <paramref name="marked"/> and <paramref name="state"/> (the
    /// depths when the callback began), and reopens those closed below them; records <see cref="LayoutDiagnostics.DrawingStateUnbalanced"/>
    /// naming <paramref name="what"/> when anything had to change.
    /// </summary>
    public static void Restore(PdfCanvas canvas, int state, int marked, List<Diagnostic> diagnostics, string what)
    {
        bool repaired = false;
        if (canvas.IsInTextObject || canvas.MarkedContentDepth != marked || canvas.StateDepth != state)
        {
            repaired = true;
            Rebalance(canvas, state, marked);
        }

        try
        {
            canvas.RestoreState();
        }
        catch (InvalidOperationException)
        {
            // A path left unpainted (RestoreState refuses one, ISO 32000-2 §8.5.1): end it without painting.
            repaired = true;
            canvas.EndPath();
            canvas.RestoreState();
        }

        if (repaired)
        {
            diagnostics.Add(new Diagnostic(
                LayoutDiagnostics.DrawingStateUnbalanced.Name,
                DiagnosticSeverity.Warning,
                $"{what} left the canvas unbalanced (an unpainted path, an open text object, or graphics states or marked-content sequences not ended as begun, ISO 32000-2 §8.4.2 and §14.6.1); the layout balanced it."));
        }
    }

    private static void Rebalance(PdfCanvas canvas, int state, int marked)
    {
        while (canvas.IsInTextObject || canvas.MarkedContentDepth > marked)
        {
            if (canvas.MarkedContentDepth > marked)
            {
                try
                {
                    canvas.EndMarkedContent();
                    continue;
                }
                catch (InvalidOperationException) when (canvas.IsInTextObject)
                {
                    // The sequence began outside the open text object: end the text object first.
                }
                catch (InvalidOperationException)
                {
                    canvas.EndPath();
                    continue;
                }
            }

            canvas.EndText();
        }

        try
        {
            while (canvas.StateDepth > state)
            {
                canvas.RestoreState();
            }
        }
        catch (InvalidOperationException)
        {
            canvas.EndPath();
            Rebalance(canvas, state, marked);
            return;
        }

        while (canvas.StateDepth < state)
        {
            canvas.SaveState();
        }

        while (canvas.MarkedContentDepth < marked)
        {
            canvas.BeginMarkedContent("Artifact");
        }
    }
}
