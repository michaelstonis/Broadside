using Broadside.Graphics;
using Broadside.Structure;

namespace Broadside.Layout.Flow;

/// <summary>
/// A horizontal line across the content area between blocks, such as a separator above a total. Decoration, not content: tagged output
/// marks it as a layout artifact and adds no structure element. A rule that does not fit below the cursor starts a page.
/// </summary>
/// <remarks>ISO 32000-2 §8.5.3.2 (stroking a path, <c>S</c>, at the line width <c>w</c>) and §14.8.2.2.2 (a <c>Layout</c> artifact).</remarks>
public sealed class Rule : FlowBlock
{
    /// <summary>Creates a black rule half a point thick, with 6 points of space above and below.</summary>
    public Rule()
        : base("Artifact")
    {
    }

    /// <summary>The thickness of the line in points; 0.5 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double Thickness
    {
        get;
        init => field = ImageBlock.Positive(value, nameof(value));
    } = 0.5;

    /// <summary>The colour of the line, or null (the default) for black.</summary>
    public PdfColor? Color { get; init; }

    /// <summary>The space above in points, dropped at the top of a page; 6 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceBefore
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    } = 6;

    /// <summary>The space below in points; 6 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceAfter
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    } = 6;

    internal override void Validate(int index)
    {
    }

    internal override ContentWidths MeasureWidths(FlowServices services) => default;

    internal override void LayOut(FlowContext context)
    {
        context.AddSpace(SpaceBefore);
        context.EnsureRoom(Thickness);
        if (context.Drawing)
        {
            context.BeginPart(new FlowPart("Artifact", PdfArtifactType.Layout, null));
            PdfCanvas canvas = context.Canvas;
            double y = context.CursorY - (Thickness / 2);
            canvas.SaveState();
            canvas.SetLineWidth(Thickness);
            canvas.SetStrokeColor(Color ?? PdfColor.FromGray(0));
            canvas.MoveTo(context.Left, y);
            canvas.LineTo(context.Right, y);
            canvas.Stroke();
            canvas.RestoreState();
            context.EndPart();
        }

        context.Advance(Thickness);
        context.AddSpace(SpaceAfter);
    }
}
